using Core;
using ErrorOr;

namespace Services.Pristine;

public sealed class PristineApiPollService(
	PristineApiClient api,
	PristineDownloader downloader,
	PristineAudioVerifier verifier
)
{
	private const string BaseUrl = "https://pristinestreaming.com";
	private const int MaxConcurrent = 5;

	public async Task<ErrorOr<PristineAlbumResult>> DownloadSingleAlbumAsync(
		HttpClient http,
		string apiKey,
		string code,
		string outDir,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);

		return await api.ResolveAlbumAsync(http, apiKey, code, ct)
			.ThenAsync(album =>
				api.GetAlbumTracksAsync(http, apiKey, album.Id, ct)
					.ThenAsync(tracks =>
						ProcessAlbumAsync(http, apiKey, code, outDir, album, tracks, ct)
					)
			);
	}

	private async Task<ErrorOr<PristineAlbumResult>> ProcessAlbumAsync(
		HttpClient http,
		string apiKey,
		string code,
		string outDir,
		PristineApiAlbum album,
		List<PristineApiTrack> tracks,
		CancellationToken ct
	)
	{
		var albumTitle = PristineText.SanitizePathComponent(album.Title);
		var folderName = PristineText.FormatAlbumFolderName(code, album.Title);
		var albumOut = Path.Combine(outDir, folderName);
		Directory.CreateDirectory(albumOut);
		Telemetry.Debug(
			"Pristine.ApiPoll.AlbumTitle code={Code} title={Title} folder={Folder}",
			code,
			albumTitle,
			folderName
		);

		if (album.ArtworkPath is not null)
		{
			var artworkUrl = $"{BaseUrl}{album.ArtworkPath}";
			var imgDest = Path.Combine(albumOut, $"{albumTitle}.jpg");
			var imgOk = await downloader.DownloadAsync(artworkUrl, imgDest, http, ct);
			Telemetry.Debug("Pristine.ApiPoll.ArtworkResult code={Code} ok={Ok}", code, imgOk);
		}

		List<PristineApiTrack> available =
		[
			.. tracks.Where(t => t.Available).OrderBy(t => t.Position),
		];
		Telemetry.Debug(
			"Pristine.ApiPoll.Expected code={Code} count={Count}",
			code,
			available.Count
		);
		if (available.Count == 0)
		{
			Telemetry.Warn("Pristine.ApiPoll.NoTracksAvailable code={Code}", code);
			return new PristineAlbumResult
			{
				Code = code,
				Title = albumTitle,
				OutPath = albumOut,
				Expected = 0,
				Downloaded = 0,
			};
		}

		Telemetry.Info("{Code}: downloading {Count} track(s)", code, available.Count);

		using SemaphoreSlim gate = new(MaxConcurrent);
		List<string> results = [];
		List<Task> pending = [];
		foreach (PristineApiTrack track in available)
		{
			await gate.WaitAsync(ct);
			PristineApiTrack capturedTrack = track;
			Task task = Task.Run(
				async () =>
				{
					try
					{
						await DownloadTrackAsync(
							http,
							apiKey,
							code,
							capturedTrack,
							albumOut,
							results,
							ct
						);
					}
					finally
					{
						gate.Release();
					}
				},
				CancellationToken.None
			);
			pending.Add(task);
		}

		try
		{
			await Task.WhenAll(pending);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.PendingDrainFailed code={Code}: {Error}",
				code,
				ex.Message
			);
		}

		Telemetry.Debug(
			"Pristine.ApiPoll.Done code={Code} downloaded={Downloaded} expected={Expected}",
			code,
			results.Count,
			available.Count
		);
		return new PristineAlbumResult
		{
			Code = code,
			Title = albumTitle,
			OutPath = albumOut,
			Expected = available.Count,
			Downloaded = results.Count,
		};
	}

	private async Task DownloadTrackAsync(
		HttpClient http,
		string apiKey,
		string code,
		PristineApiTrack track,
		string albumOut,
		List<string> results,
		CancellationToken ct
	)
	{
		var safeTitle = PristineText.SanitizePathComponent(
			PristineText.NormalizeTrackTitle(track.Title)
		);
		var stem = PristineText.ClampFileName(
			albumOut,
			$"{track.Position:00}. {safeTitle}",
			".flac"
		);
		var dest = Path.Combine(albumOut, $"{stem}.flac");
		if (File.Exists(dest) && new FileInfo(dest).Length > 0)
		{
			ErrorOr<PristineProbeResult> resumeProbeOr = await verifier.VerifyAsync(
				dest,
				code,
				track.Position,
				ct
			);
			var usable =
				resumeProbeOr.IsError is false
				&& resumeProbeOr.Value.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
				&& resumeProbeOr.Value.Bits is 16 or 24;
			if (usable)
			{
				Telemetry.Info(
					"  [{Num:00}] {Title} — already present, skipping",
					track.Position,
					safeTitle
				);
				lock (results)
				{
					results.Add(dest);
				}
				return;
			}

			PristineVerification.DeleteRejectedFile(
				dest,
				code,
				track.Position,
				resumeProbeOr.IsError
					? resumeProbeOr.FirstError.Description
					: $"resume-invalid bits={resumeProbeOr.Value.Bits}"
			);
		}

		ErrorOr<PristineApiListenSources> listenOr = await api.GetListenSourcesAsync(
			http,
			apiKey,
			track.Id,
			ct
		);
		if (listenOr.IsError)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.ListenFailed code={Code} track={Track} err={Err}",
				code,
				track.Position,
				listenOr.FirstError.Description
			);
			return;
		}

		var flacRel = listenOr.Value.Flac;
		if (flacRel is null)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.NoFlacSource code={Code} track={Track}",
				code,
				track.Position
			);
			return;
		}

		var flacUrl = $"{BaseUrl}{flacRel}";
		Telemetry.Debug(
			"Pristine.ApiPoll.Track code={Code} [{Num:00}] {Title} -> {Dest}",
			code,
			track.Position,
			safeTitle,
			Path.GetFileName(dest)
		);

		var dlOk = await downloader.DownloadAsync(flacUrl, dest, http, ct);
		if (dlOk is false)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.DownloadFailed code={Code} track={Track} dest={Dest}",
				code,
				track.Position,
				Path.GetFileName(dest)
			);
			return;
		}

		Telemetry.Debug(
			"Pristine.ApiPoll.DownloadOk code={Code} track={Track} dest={Dest}",
			code,
			track.Position,
			Path.GetFileName(dest)
		);
		if (await PristineVerification.VerifyAndKeepAsync(verifier, dest, code, track.Position, ct))
		{
			Telemetry.Info("  [{Num:00}] {Title} — kept", track.Position, safeTitle);
			lock (results)
			{
				results.Add(dest);
			}
		}
		else
		{
			Telemetry.Info("  [{Num:00}] {Title} — rejected", track.Position, safeTitle);
		}
	}
}
