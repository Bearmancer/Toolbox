using System.Diagnostics;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubePlaylistOrchestrator(
	YouTubePlaylistService playlistService,
	YouTubePlaylistProcessor playlistProcessor,
	YouTubeSyncProcessor syncProcessor
)
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
	{
		ErrorOr<SyncOutcome> outcome = await ExecuteCoreAsync(ct);
		return outcome.IsError ? [] : outcome.Value.Ids;
	}

	private async Task<ErrorOr<SyncOutcome>> ExecuteCoreAsync(CancellationToken ct)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		var syncStopwatch = Stopwatch.StartNew();

		Telemetry.Info("YouTube sync starting");

		return await LoadStoredStateAsync(ManifestFile, ct)
			.ThenAsync(stored => FetchSummariesAndDetectAsync(stored, ct))
			.ThenAsync(ctx => MergePlaylistsAsync(ctx, ct))
			.ThenAsync(ctx => ProcessIfNeededAsync(ctx, ct))
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
		foreach (PlaylistSnapshot deleted in changes.DeletedPlaylists)
			Telemetry.Info("Deleted: \"{Title}\"", deleted.Title);
		YouTubeSyncProcessor.ArchiveDeletedPlaylists(changes.DeletedPlaylists);
		List<PlaylistSnapshot> toProcess = CombineNewAndChanged(changes);
		return new SyncContext(stored, changes, toProcess);
	}

	private async Task<ErrorOr<SyncContext>> MergePlaylistsAsync(
		SyncContext ctx,
		CancellationToken ct
	)
	{
		List<PlaylistSnapshot> deduplicated = await syncProcessor.MergeDuplicatePlaylistsAsync(
			ctx.ToProcess,
			ct
		);

		if (deduplicated.Count < ctx.ToProcess.Count)
			Telemetry.Info(
				"Merged {Count} duplicate playlist(s): {Before} -> {After}",
				ctx.ToProcess.Count - deduplicated.Count,
				ctx.ToProcess.Count,
				deduplicated.Count
			);

		return new SyncContext(ctx.Stored, ctx.Changes, deduplicated);
	}

	private async Task<ErrorOr<ProcessOutcome>> ProcessIfNeededAsync(
		SyncContext ctx,
		CancellationToken ct
	)
	{
		Telemetry.Info(
			"Changes: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged",
			ctx.Changes.NewPlaylists.Count,
			ctx.Changes.ChangedPlaylists.Count,
			ctx.Changes.DeletedPlaylists.Count,
			ctx.Changes.UnchangedPlaylists.Count
		);

		if (ctx.ToProcess.Count == 0)
		{
			Telemetry.Info("Sync done: nothing to update");
			return new ProcessOutcome(ctx.Stored, ctx.Changes, null);
		}

		YouTubeSyncProcessor.SyncResult result = await syncProcessor.ProcessPlaylistsAsync(
			ctx.ToProcess,
			ctx.Stored,
			ct
		);
		return new ProcessOutcome(ctx.Stored, ctx.Changes, result);
	}

	private static ErrorOr<SyncOutcome> Finalize(ProcessOutcome outcome, Stopwatch syncStopwatch)
	{
		if (outcome.Result is { } result)
			Telemetry.Info(
				"Sync done in {Elapsed:F1}s: {New} new, {Changed} changed, {Deleted} deleted | {TotalVideos} videos",
				syncStopwatch.Elapsed.TotalSeconds,
				outcome.Changes.NewPlaylists.Count,
				outcome.Changes.ChangedPlaylists.Count,
				outcome.Changes.DeletedPlaylists.Count,
				result.TotalVideos
			);

		IReadOnlyList<string> ids = outcome.Result?.ProcessedIds ?? [];
		IReadOnlyList<string> idsWithNewVideos = outcome.Result?.PlaylistsWithNewVideos ?? [];
		return new SyncOutcome(ids, idsWithNewVideos, outcome.Stored);
	}

	public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
	{
		ErrorOr<SyncOutcome> outcomeResult = await ExecuteCoreAsync(ct);
		if (outcomeResult.IsError)
			return [];

		SyncOutcome outcome = outcomeResult.Value;
		if (outcome.IdsWithNewVideos.Count > 0)
			await syncProcessor.SortPlaylistsAsync(outcome.IdsWithNewVideos, outcome.State, ct);
		await syncProcessor.SortPlaylistsAsync(outcome.Ids, outcome.State, ct);
		return outcome.Ids;
	}

	public async Task<string?> ExecuteForPlaylistTitleAsync(string title, CancellationToken ct)
	{
		ErrorOr<SinglePlaylistOutcome> outcome = await ExecuteForPlaylistTitleCoreAsync(title, ct);
		return outcome.IsError ? null : outcome.Value.Id;
	}

	private async Task<ErrorOr<SinglePlaylistOutcome>> ExecuteForPlaylistTitleCoreAsync(
		string title,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

		return await LoadStoredStateAsync(ManifestFile, ct)
			.ThenAsync(stored => ProcessTitlePipelineAsync(title, stored, ct));
	}

	private async Task<ErrorOr<SinglePlaylistOutcome>> ProcessTitlePipelineAsync(
		string title,
		YouTubeFetchState stored,
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
			await playlistProcessor.ProcessPlaylistAsync(currentSummary, ct);
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
		CancellationToken ct
	)
	{
		ErrorOr<SinglePlaylistOutcome> outcomeResult = await ExecuteForPlaylistTitleCoreAsync(
			title,
			ct
		);
		if (outcomeResult.IsError)
			return null;

		SinglePlaylistOutcome outcome = outcomeResult.Value;
		if (outcome.Id is { } && outcome.NewVideoCount > 0)
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
		List<PlaylistSnapshot> ToProcess
	);

	public readonly record struct ProcessOutcome(
		YouTubeFetchState Stored,
		ChangeDetectionResult Changes,
		YouTubeSyncProcessor.SyncResult? Result
	);
}
