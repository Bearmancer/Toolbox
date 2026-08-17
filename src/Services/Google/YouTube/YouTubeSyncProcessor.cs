using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubeSyncProcessor(
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
	private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	public async Task<SyncResult> ProcessPlaylistsAsync(
		List<PlaylistSnapshot> playlistsToProcess,
		YouTubeFetchState stored,
		bool noTranslate,
		CancellationToken ct
	)
	{
		SyncCounters counters = SyncCounters.FromStoredState(stored);

		List<PlaylistSnapshot> processedSnapshots = [];
		List<string> playlistsWithNewVideos = [];

		for (var i = 0; i < playlistsToProcess.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			PlaylistSnapshot snapshot = playlistsToProcess[i];

			ProcessResult result = await ProcessSinglePlaylistAsync(
				snapshot,
				noTranslate,
				counters,
				ct
			);
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

		YouTubeFetchState state = new()
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

	public async Task<SortStatistics> SortPlaylistsAsync(
		IReadOnlyList<string> playlistIds,
		YouTubeFetchState state,
		CancellationToken ct
	)
	{
		Telemetry.Debug("Sorting {Count} playlist(s) after sync", playlistIds.Count);

		const int maxWritesPerRun = 150;
		var writesConsumed = 0;
		var modified = 0;
		var attempted = 0;

		foreach (var playlistId in playlistIds)
		{
			ct.ThrowIfCancellationRequested();

			if (writesConsumed >= maxWritesPerRun)
			{
				Telemetry.Info(
					"Quota budget reached ({Writes}/{Max} writes). Stopping sort.",
					writesConsumed,
					maxWritesPerRun
				);
				break;
			}

			if (!state.PlaylistSnapshots.TryGetValue(playlistId, out PlaylistSnapshot? snapshot))
				continue;

			attempted++;
			var remainingBudget = maxWritesPerRun - writesConsumed;
			try
			{
				(var sorted, var consumed, var distinctMoved) = await SortSinglePlaylistAsync(
					playlistId,
					snapshot,
					state,
					remainingBudget,
					ct
				);
				writesConsumed += consumed;
				if (sorted && consumed > 0)
					modified++;
				else if (!sorted)
					break;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Error(
					"Sort failed for {Title} ({Id}): {Error}. Continuing to next playlist.",
					snapshot.Title,
					playlistId,
					ex.Message
				);
			}
		}

		if (attempted > 0)
			await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);

		SortStatistics stats = new(
			Attempted: attempted,
			Modified: modified,
			AlreadySorted: attempted - modified,
			TotalWrites: writesConsumed
		);

		Telemetry.Info(
			"Sort complete: {Attempted} attempted, {Modified} modified, {AlreadySorted} already-sorted | {TotalWrites} writes ({WritesUnits} units)",
			stats.Attempted,
			stats.Modified,
			stats.AlreadySorted,
			stats.TotalWrites,
			stats.TotalWrites * 50
		);

		return stats;
	}

	private async Task<(
		bool Sorted,
		int WritesConsumed,
		int DistinctItemsMoved
	)> SortSinglePlaylistAsync(
		string playlistId,
		PlaylistSnapshot snapshot,
		YouTubeFetchState stored,
		int remainingBudget,
		CancellationToken ct
	)
	{
		IReadOnlyDictionary<string, string> translatedTitles = await LoadTranslatedTitlesAsync(
			snapshot.Title,
			ct
		);

		ErrorOr<YouTubeSortService.SortResult> sortResult = await sortService.SortPlaylistAsync(
			playlistId,
			translatedTitles,
			remainingBudget,
			ct
		);

		if (sortResult.IsError)
		{
			Telemetry.Error(
				"Sorting failed for {Title}: {Error}",
				snapshot.Title,
				sortResult.FirstError.Description
			);
			return (false, 0, 0);
		}

		YouTubeSortService.SortResult result = sortResult.Value;

		if (result.Repositioned > 0)
			await playlistProcessor.RefreshLocalStateAsync(snapshot, ct);

		stored.PlaylistSnapshots[playlistId] = snapshot with
		{
			ETag = !string.IsNullOrEmpty(result.NewETag) ? result.NewETag : snapshot.ETag,
			LastSortMoves = result.DistinctItemsMoved,
			LastSortAttempted = result.LastSortAttempted,
			LastSortCompleted = result.LastSortCompleted,
		};

		Telemetry.Debug(
			"{Title}: {Repositioned} items repositioned",
			snapshot.Title,
			result.Repositioned
		);
		return (true, result.WritesConsumed, result.DistinctItemsMoved);
	}

	private static async Task<IReadOnlyDictionary<string, string>> LoadTranslatedTitlesAsync(
		string playlistTitle,
		CancellationToken ct
	)
	{
		var path = Path.Combine(ProcessedDir, $"{Text.SanitizeFileName(playlistTitle)}.json");
		if (!File.Exists(path))
			return new Dictionary<string, string>();

		try
		{
			await using FileStream stream = File.OpenRead(path);
			List<YouTubeVideo> videos =
				await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
					stream,
					YouTubeFetchState.JsonOptions,
					ct
				) ?? [];
			return videos
				.Where(v => v.TranslatedTitle is { })
				.DistinctBy(v => v.VideoId)
				.ToDictionary(v => v.VideoId, v => v.TranslatedTitle!);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Failed to load translated titles for {Title}: {Error}",
				playlistTitle,
				ex.Message
			);
			return new Dictionary<string, string>();
		}
	}

	public readonly record struct SortStatistics(
		int Attempted,
		int Modified,
		int AlreadySorted,
		int TotalWrites
	);

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
			Dictionary<string, PlaylistSnapshot> snapshots = stored.PlaylistSnapshots.ToDictionary(
				kv => kv.Key,
				kv => kv.Value
			);
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
			Dictionary<string, PlaylistSnapshot> updated = [];
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
