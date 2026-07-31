using Core;
using ErrorOr;

namespace Services.LastFm;

public class LastFmSyncOrchestrator(LastFmService service)
{
	private static readonly string StateDir = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"lastfm"
	);

	public async Task<ErrorOr<SyncResult>> SyncAsync(
		DateTimeOffset? fetchAfter,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.LastFm);

		var syncSw = System.Diagnostics.Stopwatch.StartNew();
		Telemetry.Verbose(
			"LastFm sync started (fetchAfter={Since})",
			fetchAfter?.ToString("yyyy-MM-dd HH:mm") ?? "null"
		);

		if (fetchAfter.HasValue)
			Telemetry.Info(
				"Last.fm sync starting (from {Date})",
				fetchAfter.Value.ToString("yyyy-MM-dd HH:mm")
			);
		else
			Telemetry.Info("Last.fm sync starting");

		Directory.CreateDirectory(StateDir);

		var loadSw = System.Diagnostics.Stopwatch.StartNew();
		List<LastFmScrobble> existing =
		[
			.. (await LastFmState.LoadScrobblesAsync(StateDir)).OrderByDescending(scrobble =>
				scrobble.PlayedAt
			),
		];
		loadSw.Stop();
		Telemetry.Verbose(
			"Loaded {Count} existing scrobbles in {ElapsedMs}ms",
			existing.Count,
			loadSw.ElapsedMilliseconds
		);

		var removedCount = 0;

		if (fetchAfter is { } since)
		{
			removedCount = existing.Count(sc => sc.PlayedAt >= since);
			existing.RemoveAll(sc => sc.PlayedAt >= since);
			if (removedCount > 0)
				Telemetry.Verbose(
					"Removed {Count} scrobbles newer than {Since}",
					removedCount,
					since.ToString("yyyy-MM-dd HH:mm")
				);
		}
		else if (existing.Count > 0)
			fetchAfter = existing[0].PlayedAt;

		List<LastFmScrobble> newScrobbles = await service.FetchRecentTracksAsync(fetchAfter, ct);

		if (newScrobbles.Count == 0)
		{
			syncSw.Stop();
			Telemetry.Info(
				"Last.fm sync: 0 new, {Removed} removed, {Total} total in {ElapsedMs}ms",
				removedCount,
				existing.Count,
				syncSw.ElapsedMilliseconds
			);
			return new SyncResult(0, removedCount, existing.Count, null);
		}

		var mergeSw = System.Diagnostics.Stopwatch.StartNew();
		List<LastFmScrobble> merged = LastFmState.MergeScrobbles(existing, newScrobbles);
		mergeSw.Stop();
		Telemetry.Verbose(
			"Merged {New} new + {Existing} existing = {Total} in {ElapsedMs}ms",
			newScrobbles.Count,
			existing.Count,
			merged.Count,
			mergeSw.ElapsedMilliseconds
		);

		var saveSw = System.Diagnostics.Stopwatch.StartNew();
		await LastFmState.SaveScrobblesAsync(StateDir, merged);
		saveSw.Stop();
		Telemetry.Verbose(
			"Saved {Count} scrobbles in {ElapsedMs}ms",
			merged.Count,
			saveSw.ElapsedMilliseconds
		);

		DateTimeOffset? lastScrobbleDate = merged.Count > 0 ? merged[0].PlayedAt : null;

		syncSw.Stop();
		Telemetry.Info(
			"Last.fm sync: {New} new, {Removed} removed, {Total} total in {ElapsedMs}ms",
			newScrobbles.Count,
			removedCount,
			merged.Count,
			syncSw.ElapsedMilliseconds
		);

		return new SyncResult(newScrobbles.Count, removedCount, merged.Count, lastScrobbleDate);
	}

	public readonly record struct SyncResult(
		int NewCount,
		int RemovedCount,
		int TotalCount,
		DateTimeOffset? LastScrobbleDate
	);
}
