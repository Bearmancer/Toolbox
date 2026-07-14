using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubeSyncProcessor(
	YouTubePlaylistService playlistService,
	YouTubePlaylistProcessor playlistProcessor,
	YouTubeSortService sortService
)
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);
	private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
	private static readonly string RawDir = Path.Combine(StateRoot, "raw");
	private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	public async Task<SyncResult> ProcessPlaylistsAsync(
		List<PlaylistSnapshot> playlistsToProcess,
		YouTubeFetchState stored,
		bool noTranslate,
		CancellationToken ct
	)
	{
		var counters = SyncCounters.FromStoredState(stored);

		List<PlaylistSnapshot> processedSnapshots = [];
		List<string> playlistsWithNewVideos = [];

		for (var i = 0; i < playlistsToProcess.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			PlaylistSnapshot snapshot = playlistsToProcess[i];

			ProcessResult result = await ProcessSinglePlaylistAsync(snapshot, noTranslate, counters, ct);
			if (result.ShouldBreak)
				break;

			Telemetry.Debug(
				"Playlist {Title}: {Videos} videos, {New} new",
				snapshot.Title,
				result.Videos,
				result.NewVideoCount
			);

			processedSnapshots.Add(snapshot);
			if (result.NewVideoCount > 0)
				playlistsWithNewVideos.Add(snapshot.PlaylistId);
			counters.UpdatedSnapshots[snapshot.PlaylistId] = snapshot;
			counters.UpdateFrom(result);
		}

		return counters.ToResult(processedSnapshots, playlistsWithNewVideos);
	}

	private async Task<ProcessResult> ProcessSinglePlaylistAsync(
		PlaylistSnapshot snapshot,
		bool noTranslate,
		SyncCounters counters,
		CancellationToken ct
	)
	{
		ErrorOr<YouTubePlaylistProcessor.ProcessResult> processorResult =
			await playlistProcessor.ProcessPlaylistAsync(snapshot, noTranslate, ct);

		if (processorResult.IsError)
		{
			Error error = processorResult.Errors[0];

			if (error.Code is "YT.RateLimit" or "Azure.RateLimit")
			{
				Telemetry.Warn(
					"Rate limit reached ({Code}). Skipping remaining playlists.",
					error.Code
				);
				return ProcessResult.Break;
			}

			if (error.Code is "Azure.AuthFailed")
			{
				Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", error.Code);
				return ProcessResult.Break;
			}

			Telemetry.Error(
				"Unexpected error processing playlist {Title}: {Error}",
				snapshot.Title,
				error.Description
			);
			return ProcessResult.Break;
		}

		YouTubePlaylistProcessor.ProcessResult result = processorResult.Value;
		await SaveIncrementalStateAsync(counters.UpdatedSnapshots, snapshot, ct);

		return new(result.Videos, result.Skipped, result.NewVideoCount, false);
	}

	private static async Task SaveIncrementalStateAsync(
		IReadOnlyDictionary<string, PlaylistSnapshot> updatedSnapshots,
		PlaylistSnapshot snapshot,
		CancellationToken ct
	)
	{
		Dictionary<string, PlaylistSnapshot> snapshots = new(updatedSnapshots)
		{
			[snapshot.PlaylistId] = snapshot,
		};

		var state = new YouTubeFetchState
		{
			PlaylistSnapshots = snapshots,
			LastChecked = DateTimeOffset.UtcNow,
			LastUpdated = DateTimeOffset.UtcNow,
		};
		await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
	}

	public static void ArchiveDeletedPlaylists(IReadOnlyList<PlaylistSnapshot> deletedPlaylists)
	{
		foreach (PlaylistSnapshot snapshot in deletedPlaylists)
			ArchivePlaylist(snapshot);
	}

	private static void ArchivePlaylist(PlaylistSnapshot snapshot)
	{
		var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
		var sourcePath = Path.Combine(ProcessedDir, $"{sanitizedTitle}.json");
		var destPath = Path.Combine(DeletedDir, $"{sanitizedTitle}.json");

		if (!File.Exists(sourcePath))
			return;

		File.Move(sourcePath, destPath, true);
		Telemetry.Debug("Archived deleted playlist: {Title}", snapshot.Title);
	}

	public async Task SortPlaylistsAsync(
		IReadOnlyList<string> playlistIds,
		YouTubeFetchState state,
		CancellationToken ct
	)
	{
		Telemetry.Debug("Sorting {Count} playlist(s) after sync", playlistIds.Count);

		var anySorted = false;

		foreach (var playlistId in playlistIds)
		{
			ct.ThrowIfCancellationRequested();

			if (!state.PlaylistSnapshots.TryGetValue(playlistId, out PlaylistSnapshot? snapshot))
				continue;

			var sorted = await SortSinglePlaylistAsync(playlistId, snapshot, state, ct);
			if (sorted)
				anySorted = true;
			else
				break;
		}

		if (anySorted)
			await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
	}

	private async Task<bool> SortSinglePlaylistAsync(
		string playlistId,
		PlaylistSnapshot snapshot,
		YouTubeFetchState stored,
		CancellationToken ct
	)
	{
		ErrorOr<YouTubeSortService.SortResult> sortResult = await sortService.SortPlaylistAsync(
			playlistId,
			ct
		);

		if (sortResult.IsError)
		{
			Telemetry.Error(
				"Sorting failed for {Title}: {Error}",
				snapshot.Title,
				sortResult.FirstError.Description
			);
			return false;
		}

		YouTubeSortService.SortResult result = sortResult.Value;

		if (result.Repositioned > 0)
			await playlistProcessor.RefreshLocalStateAsync(snapshot, ct);

		if (!string.IsNullOrEmpty(result.NewETag))
			stored.PlaylistSnapshots[playlistId] = snapshot with { ETag = result.NewETag };

		Telemetry.Debug(
			"{Title}: {Repositioned} items repositioned",
			snapshot.Title,
			result.Repositioned
		);
		return true;
	}

	public async Task<List<PlaylistSnapshot>> MergeDuplicatePlaylistsAsync(
		List<PlaylistSnapshot> playlists,
		CancellationToken ct
	)
	{
		var groups = playlists.GroupBy(p => Text.SanitizeFileName(p.Title)).ToList();

		var duplicateGroups = groups.Where(g => g.Count() > 1).ToList();
		if (duplicateGroups.Count == 0)
			return playlists;

		List<PlaylistSnapshot> toRemove = [];

		foreach (var group in duplicateGroups)
		{
			var ordered = group.OrderByDescending(p => p.ReportedVideoCount).ToList();
			var winner = ordered[0];
			var losers = ordered.Skip(1).ToList();

			Telemetry.Info(
				"Duplicate playlists for '{Sanitized}': keeping '{Winner}' ({Count} videos), merging {LoserCount} smaller playlist(s)",
				Text.SanitizeFileName(winner.Title),
				winner.Title,
				winner.ReportedVideoCount,
				losers.Count
			);

			await MergeProcessedVideosAsync(winner, losers, ct);

			foreach (var loser in losers)
			{
				ErrorOr<string> deleteResult = await playlistService.DeletePlaylistAsync(
					loser.PlaylistId,
					ct
				);

				if (deleteResult.IsError)
					Telemetry.Error(
						"Failed to delete duplicate playlist {Title}: {Error}",
						loser.Title,
						deleteResult.FirstError.Description
					);
				else
					Telemetry.Info("Deleted duplicate playlist: {Title}", loser.Title);

				ArchiveRawPlaylist(loser);
				toRemove.Add(loser);
			}
		}

		return [.. playlists.Except(toRemove)];
	}

	private static async Task MergeProcessedVideosAsync(
		PlaylistSnapshot winner,
		List<PlaylistSnapshot> losers,
		CancellationToken ct
	)
	{
		var winnerPath = Path.Combine(ProcessedDir, $"{Text.SanitizeFileName(winner.Title)}.json");
		List<YouTubeVideo> winnerVideos = await LoadProcessedVideosAsync(winnerPath, ct);
		Dictionary<string, YouTubeVideo> videoDict = [];
		foreach (YouTubeVideo v in winnerVideos)
			videoDict.TryAdd(v.VideoId, v);

		foreach (PlaylistSnapshot loser in losers)
		{
			var loserPath = Path.Combine(
				ProcessedDir,
				$"{Text.SanitizeFileName(loser.Title)}.json"
			);
			List<YouTubeVideo> loserVideos = await LoadProcessedVideosAsync(loserPath, ct);

			var added = 0;
			foreach (YouTubeVideo v in loserVideos)
			{
				if (videoDict.TryAdd(v.VideoId, v))
					added++;
			}

			Telemetry.Info(
				"Merged {Added} unique videos from '{Loser}' into '{Winner}'",
				added,
				loser.Title,
				winner.Title
			);
		}

		if (videoDict.Count > winnerVideos.Count)
		{
			var merged = videoDict.Values.ToList();
			var json = JsonSerializer.Serialize(merged, YouTubeFetchState.JsonOptions);
			await File.WriteAllTextAsync(winnerPath, json, ct);
		}
	}

	private static async Task<List<YouTubeVideo>> LoadProcessedVideosAsync(
		string path,
		CancellationToken ct
	)
	{
		if (!File.Exists(path))
			return [];

		try
		{
			await using FileStream stream = File.OpenRead(path);
			return await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
					stream,
					YouTubeFetchState.JsonOptions,
					ct
				) ?? [];
		}
		catch (Exception ex) when (ex is JsonException or FormatException)
		{
			Telemetry.Error("Invalid JSON in processed file {Path}: {Error}", path, ex.Message);
			return [];
		}
	}

	private static void ArchiveRawPlaylist(PlaylistSnapshot snapshot)
	{
		var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
		var sourcePath = Path.Combine(RawDir, $"{sanitizedTitle}.json");
		var destPath = Path.Combine(DeletedDir, $"{sanitizedTitle}.json");

		if (!File.Exists(sourcePath))
			return;

		var dir = Path.GetDirectoryName(destPath);
		if (dir is { } && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		File.Move(sourcePath, destPath, true);
		Telemetry.Info("Archived raw playlist file: {Title}", snapshot.Title);
	}

	public readonly record struct SyncResult(
		IReadOnlyList<string> ProcessedIds,
		IReadOnlyList<string> PlaylistsWithNewVideos,
		Dictionary<string, PlaylistSnapshot> UpdatedSnapshots,
		int TotalVideos,
		int SkippedVideos
	);

	public readonly record struct ProcessResult(
		int Videos,
		int Skipped,
		int NewVideoCount,
		bool ShouldBreak
	)
	{
		public static ProcessResult Break { get; } = new(0, 0, 0, true);
	}

	public sealed class SyncCounters
	{
		private SyncCounters(Dictionary<string, PlaylistSnapshot> updatedSnapshots) =>
			UpdatedSnapshots = updatedSnapshots;

		public Dictionary<string, PlaylistSnapshot> UpdatedSnapshots { get; }
		public int TotalVideos { get; set; }
		public int SkippedVideos { get; set; }

		public static SyncCounters FromStoredState(YouTubeFetchState stored)
		{
			Dictionary<string, PlaylistSnapshot> snapshots = new(stored.PlaylistSnapshots);
			return new SyncCounters(snapshots);
		}

		public void UpdateFrom(ProcessResult result)
		{
			TotalVideos += result.Videos;
			SkippedVideos += result.Skipped;
		}

		public SyncResult ToResult(
			List<PlaylistSnapshot> processedSnapshots,
			List<string> playlistsWithNewVideos
		)
		{
			Dictionary<string, PlaylistSnapshot> updated = new(processedSnapshots.Count);
			foreach (PlaylistSnapshot s in processedSnapshots)
				updated[s.PlaylistId] = s;

			return new SyncResult(
				[.. processedSnapshots.Select(s => s.PlaylistId)],
				[.. playlistsWithNewVideos],
				updated,
				TotalVideos,
				SkippedVideos
			);
		}
	}
}
