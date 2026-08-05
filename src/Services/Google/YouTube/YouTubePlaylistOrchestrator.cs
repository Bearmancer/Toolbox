using System.Diagnostics;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubePlaylistOrchestrator(
	YouTubePlaylistService playlistService,
	YouTubePlaylistProcessor playlistProcessor,
	YouTubeSyncProcessor syncProcessor,
	YouTubeDuplicateMerger merger
)
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	public async Task<IReadOnlyList<string>> ExecuteAsync(bool noTranslate, CancellationToken ct)
	{
		ErrorOr<SyncOutcome> outcome = await ExecuteCoreAsync(noTranslate, ct);
		return outcome.IsError ? [] : outcome.Value.Ids;
	}

	private async Task<ErrorOr<SyncOutcome>> ExecuteCoreAsync(
		bool noTranslate,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		var syncStopwatch = Stopwatch.StartNew();

		Telemetry.Info("YouTube sync starting");

		return await LoadStoredStateAsync(ManifestFile, ct)
			.ThenAsync(stored => FetchSummariesAndDetectAsync(stored, ct))
			.ThenAsync(ctx => MergePlaylistsAsync(ctx, ct))
			.ThenAsync(ctx => ProcessIfNeededAsync(ctx, noTranslate, ct))
			.Then(outcome => Finalize(outcome, syncStopwatch));
	}

	private async Task<ErrorOr<SyncContext>> FetchSummariesAndDetectAsync(
		YouTubeFetchState stored,
		CancellationToken ct
	)
	{
		IReadOnlyList<PlaylistSnapshot> current = await playlistService.GetPlaylistSummariesAsync(
			ct
		);
		ChangeDetectionResult changes = YouTubeChangeDetector.DetectChanges(current, stored);

		foreach (PlaylistSnapshot playlist in changes.NewPlaylists)
			Telemetry.Info(
				"New: {Title} ({Count} videos)",
				playlist.Title,
				playlist.ReportedVideoCount
			);

		foreach (PlaylistSnapshot playlist in changes.ChangedPlaylists)
		{
			var delta = stored.PlaylistSnapshots.TryGetValue(
				playlist.PlaylistId,
				out PlaylistSnapshot? storedSnapshot
			)
				? playlist.ReportedVideoCount - storedSnapshot.ReportedVideoCount
				: playlist.ReportedVideoCount;
			var deltaStr = delta >= 0 ? $"+{delta}" : $"{delta}";
			Telemetry.Info("Changed: {Title} ({Delta} videos)", playlist.Title, deltaStr);
		}

		if (changes.DeletedPlaylists.Count > 0)
		{
			var deletedIds = changes.DeletedPlaylists.Select(d => d.PlaylistId).ToHashSet();
			foreach (PlaylistSnapshot deleted in changes.DeletedPlaylists)
				Telemetry.Info("Deleted: {Title}", deleted.Title);
			YouTubeSyncProcessor.ArchiveDeletedPlaylists(changes.DeletedPlaylists);
			stored = stored with
			{
				PlaylistSnapshots = stored
					.PlaylistSnapshots.Where(kvp => !deletedIds.Contains(kvp.Key))
					.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
			};
			await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);
		}

		List<PlaylistSnapshot> toProcess = CombineNewAndChanged(changes);
		return new SyncContext(stored, changes, toProcess, current);
	}

	private async Task<ErrorOr<SyncContext>> MergePlaylistsAsync(
		SyncContext ctx,
		CancellationToken ct
	)
	{
		DuplicateMergeOutcome mergeOutcome = await merger.MergeDuplicateGroupsAsync(
			ctx.AllCurrentPlaylists,
			ct
		);

		YouTubeFetchState stored = ctx.Stored;
		ChangeDetectionResult changes = ctx.Changes;

		if (mergeOutcome.RemovedLosers.Count > 0)
		{
			var loserIds = mergeOutcome.RemovedLosers.Select(l => l.PlaylistId).ToHashSet();
			stored = stored with
			{
				PlaylistSnapshots = stored
					.PlaylistSnapshots.Where(kvp => !loserIds.Contains(kvp.Key))
					.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
			};
			await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);

			changes = changes with
			{
				NewPlaylists = [.. changes.NewPlaylists.Where(p => !loserIds.Contains(p.PlaylistId))],
				ChangedPlaylists = [.. changes.ChangedPlaylists.Where(p => !loserIds.Contains(p.PlaylistId))],
				DeletedPlaylists = [.. changes.DeletedPlaylists.Where(p => !loserIds.Contains(p.PlaylistId))],
			};
		}

		List<PlaylistSnapshot> toProcess = [.. ctx.ToProcess];
		foreach (var winnerId in mergeOutcome.WinnersRequiringProcessing)
		{
			PlaylistSnapshot? winner = mergeOutcome.Survivors.FirstOrDefault(s =>
				s.PlaylistId == winnerId
			);
			if (winner is { } && !toProcess.Any(p => p.PlaylistId == winnerId))
				toProcess.Add(winner);
		}

		return new SyncContext(stored, changes, toProcess, ctx.AllCurrentPlaylists);
	}

	private async Task<ErrorOr<ProcessOutcome>> ProcessIfNeededAsync(
		SyncContext ctx,
		bool noTranslate,
		CancellationToken ct
	)
	{
		if (ctx.ToProcess.Count == 0)
		{
			Telemetry.Info("Sync done: nothing to update");
			return new ProcessOutcome(ctx.Stored, ctx.Changes, null);
		}

		YouTubeSyncProcessor.SyncResult result = await syncProcessor.ProcessPlaylistsAsync(
			ctx.ToProcess,
			ctx.Stored,
			noTranslate,
			ct
		);
		return new ProcessOutcome(ctx.Stored, ctx.Changes, result);
	}

	private static ErrorOr<SyncOutcome> Finalize(ProcessOutcome outcome, Stopwatch syncStopwatch)
	{
		if (outcome.Result is { } result)
		{
			Telemetry.Info(
				"Sync done in {Elapsed:F1}s: {New} new, {Changed} changed, {Deleted} deleted | {TotalVideos} videos",
				syncStopwatch.Elapsed.TotalSeconds,
				outcome.Changes.NewPlaylists.Count,
				outcome.Changes.ChangedPlaylists.Count,
				outcome.Changes.DeletedPlaylists.Count,
				result.TotalVideos
			);
		}

		IReadOnlyList<string> ids = outcome.Result?.ProcessedIds ?? [];
		IReadOnlyList<string> idsWithNewVideos = outcome.Result?.PlaylistsWithNewVideos ?? [];
		return new SyncOutcome(ids, idsWithNewVideos, outcome.Stored);
	}

	public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(
		bool noTranslate,
		CancellationToken ct
	)
	{
		ErrorOr<SyncOutcome> outcomeResult = await ExecuteCoreAsync(noTranslate, ct);
		if (outcomeResult.IsError)
			return [];

		SyncOutcome outcome = outcomeResult.Value;
		
		// Sort ALL playlists in manifest, not just processed ones
		var allPlaylistIds = outcome.State.PlaylistSnapshots.Keys.ToList();
		await syncProcessor.SortPlaylistsAsync(allPlaylistIds, outcome.State, ct);
		
		return outcome.Ids;
	}

	public async Task<string?> ExecuteForPlaylistTitleAsync(
		string title,
		bool noTranslate,
		CancellationToken ct
	)
	{
		ErrorOr<SinglePlaylistOutcome> outcome = await ExecuteForPlaylistTitleCoreAsync(
			title,
			noTranslate,
			ct
		);
		return outcome.IsError ? null : outcome.Value.Id;
	}

	private async Task<ErrorOr<SinglePlaylistOutcome>> ExecuteForPlaylistTitleCoreAsync(
		string title,
		bool noTranslate,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

		return await LoadStoredStateAsync(ManifestFile, ct)
			.ThenAsync(stored => ProcessTitlePipelineAsync(title, stored, noTranslate, ct));
	}

	private async Task<ErrorOr<SinglePlaylistOutcome>> ProcessTitlePipelineAsync(
		string title,
		YouTubeFetchState stored,
		bool noTranslate,
		CancellationToken ct
	)
	{
		ErrorOr<PlaylistSnapshot> matchResult = await FindPlaylistByTitleAsync(title, stored, ct);
		if (matchResult.IsError)
			return matchResult.FirstError;

		PlaylistSnapshot match = matchResult.Value;

		PlaylistSnapshot? currentSummary = await playlistService.GetPlaylistSummaryAsync(
			match.PlaylistId,
			ct
		);
		if (currentSummary is null)
			return Errors.YouTube.ApiError($"Failed to fetch summary for {match.Title}");

		PlaylistSnapshot? storedSnapshot = stored.PlaylistSnapshots.GetValueOrDefault(
			match.PlaylistId
		);
		if (
			storedSnapshot is { }
			&& !string.IsNullOrEmpty(storedSnapshot.ETag)
			&& !string.IsNullOrEmpty(currentSummary.ETag)
			&& storedSnapshot.ETag == currentSummary.ETag
		)
		{
			Telemetry.Info("Playlist {Title} unchanged (ETag match) — skipping sync", match.Title);
			return new SinglePlaylistOutcome(match.PlaylistId, 0, stored);
		}

		ErrorOr<YouTubePlaylistProcessor.ProcessResult> processorResult =
			await playlistProcessor.ProcessPlaylistAsync(currentSummary, noTranslate, ct);
		if (processorResult.IsError)
		{
			Telemetry.Error(
				"Failed to process playlist {Title}: {Error}",
				currentSummary.Title,
				processorResult.Errors[0].Description
			);
			return processorResult.FirstError;
		}

		YouTubePlaylistProcessor.ProcessResult result = processorResult.Value;

		YouTubeFetchState updated = stored with
		{
			PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots)
			{
				[currentSummary.PlaylistId] = currentSummary,
			},
			LastChecked = DateTimeOffset.UtcNow,
			LastUpdated = DateTimeOffset.UtcNow,
		};
		await YouTubeFetchState.SaveAsync(ManifestFile, updated, ct);

		Telemetry.Info(
			"Synced playlist {Title}: {Videos} videos ({Skipped} skipped)",
			currentSummary.Title,
			result.Videos,
			result.Skipped
		);

		return new SinglePlaylistOutcome(currentSummary.PlaylistId, result.NewVideoCount, updated);
	}

	public async Task<string?> ExecuteForPlaylistTitleWithSortAsync(
		string title,
		bool noTranslate,
		CancellationToken ct
	)
	{
		ErrorOr<SinglePlaylistOutcome> outcomeResult = await ExecuteForPlaylistTitleCoreAsync(
			title,
			noTranslate,
			ct
		);
		if (outcomeResult.IsError)
			return null;

		SinglePlaylistOutcome outcome = outcomeResult.Value;
		
		// Always sort, regardless of NewVideoCount
		if (outcome.Id is { })
			await syncProcessor.SortPlaylistsAsync([outcome.Id], outcome.State, ct);
		
		return outcome.Id;
	}

	private async Task<ErrorOr<PlaylistSnapshot>> FindPlaylistByTitleAsync(
		string title,
		YouTubeFetchState stored,
		CancellationToken ct
	)
	{
		PlaylistSnapshot? match = stored.PlaylistSnapshots.Values.FirstOrDefault(s =>
			s.Title.IsEqualToIgnore(title)
		);

		if (match is { })
		{
			Telemetry.Debug("Cached ID for {Title} (skipped Playlists.list)", match.Title);
			return match;
		}

		IReadOnlyList<PlaylistSnapshot> summaries = await playlistService.GetPlaylistSummariesAsync(
			ct
		);
		match = summaries.FirstOrDefault(s => s.Title.IsEqualToIgnore(title));

		return match is null
			? Errors.YouTube.ApiError($"Playlist '{title}' not found.")
			: (ErrorOr<PlaylistSnapshot>)match;
	}

	private static List<PlaylistSnapshot> CombineNewAndChanged(ChangeDetectionResult changes) =>
		[.. changes.NewPlaylists, .. changes.ChangedPlaylists];

	private static async Task<ErrorOr<YouTubeFetchState>> LoadStoredStateAsync(
		string path,
		CancellationToken ct
	)
	{
		try
		{
			return await YouTubeFetchState.LoadAsync(path, ct);
		}
		catch (Exception ex)
		{
			return Errors.YouTube.ApiError($"State load failed: {ex.Message}");
		}
	}

	public readonly record struct SyncOutcome(
		IReadOnlyList<string> Ids,
		IReadOnlyList<string> IdsWithNewVideos,
		YouTubeFetchState State
	);

	public readonly record struct SinglePlaylistOutcome(
		string? Id,
		int NewVideoCount,
		YouTubeFetchState State
	);

	public readonly record struct SyncContext(
		YouTubeFetchState Stored,
		ChangeDetectionResult Changes,
		List<PlaylistSnapshot> ToProcess,
		IReadOnlyList<PlaylistSnapshot> AllCurrentPlaylists
	);

	public readonly record struct ProcessOutcome(
		YouTubeFetchState Stored,
		ChangeDetectionResult Changes,
		YouTubeSyncProcessor.SyncResult? Result
	);
}
