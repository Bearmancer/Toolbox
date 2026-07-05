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

    private static readonly string ManifestFile = Path.Combine(
        StateRoot,
        "manifest.json"
    );

    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
    {
        var outcome = await ExecuteCoreAsync(ct);
        if (outcome.IsError)
            return [];
        return outcome.Value.Ids;
    }

    private async Task<ErrorOr<SyncOutcome>> ExecuteCoreAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.YouTube);
        var syncStopwatch = Stopwatch.StartNew();

        return await LoadStoredStateAsync(ManifestFile, ct)
            .ThenAsync(stored => FetchSummariesAndDetectAsync(stored, ct))
            .ThenAsync(ctx => ProcessIfNeededAsync(ctx, ct))
            .Then(outcome => Finalize(outcome, syncStopwatch));
    }

    private async Task<ErrorOr<SyncContext>> FetchSummariesAndDetectAsync(YouTubeFetchState stored, CancellationToken ct)
    {
        var current = await playlistService.GetPlaylistSummariesAsync(ct);
        var changes = YouTubeChangeDetector.DetectChanges(current, stored);
        syncProcessor.ArchiveDeletedPlaylists(changes.DeletedPlaylists);
        var toProcess = CombineNewAndChanged(changes);
        return new SyncContext(stored, changes, toProcess);
    }

    private async Task<ErrorOr<ProcessOutcome>> ProcessIfNeededAsync(SyncContext ctx, CancellationToken ct)
    {
        if (ctx.ToProcess.Count == 0)
        {
            LogNoChangesNeeded(ctx.Changes);
            return new ProcessOutcome(ctx.Stored, ctx.Changes, null);
        }

        var result = await syncProcessor.ProcessPlaylistsAsync(ctx.ToProcess, ctx.Stored, ct);
        return new ProcessOutcome(ctx.Stored, ctx.Changes, result);
    }

    private static ErrorOr<SyncOutcome> Finalize(ProcessOutcome outcome, Stopwatch syncStopwatch)
    {
        if (outcome.Result is { } result)
            LogSyncSummary(syncStopwatch.Elapsed, outcome.Changes, result);

        IReadOnlyList<string> ids = outcome.Result?.ProcessedIds ?? [];
        return new SyncOutcome(ids, outcome.Stored);
    }

    public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
    {
        var outcomeResult = await ExecuteCoreAsync(ct);
        if (outcomeResult.IsError)
            return [];

        var outcome = outcomeResult.Value;
        if (outcome.Ids.Count > 0)
            await syncProcessor.SortPlaylistsAsync(outcome.Ids, outcome.State, ct);
        return outcome.Ids;
    }

    public async Task<string?> ExecuteForPlaylistTitleAsync(
        string title,
        CancellationToken ct
    )
    {
        var outcome = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        if (outcome.IsError)
            return null;
        return outcome.Value.Id;
    }

    private async Task<ErrorOr<SinglePlaylistOutcome>> ExecuteForPlaylistTitleCoreAsync(
        string title,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.YouTube);

        return await LoadStoredStateAsync(ManifestFile, ct)
            .ThenAsync<YouTubeFetchState, SinglePlaylistOutcome>(
                stored => ProcessTitlePipelineAsync(title, stored, ct)
            );
    }

    private async Task<ErrorOr<SinglePlaylistOutcome>> ProcessTitlePipelineAsync(
        string title,
        YouTubeFetchState stored,
        CancellationToken ct
    )
    {
        var matchResult = await FindPlaylistByTitleAsync(title, stored, ct);
        if (matchResult.IsError)
            return matchResult.FirstError;

        var match = matchResult.Value;

        var currentSummary = await playlistService.GetPlaylistSummaryAsync(match.PlaylistId, ct);
        if (currentSummary is null)
            return Errors.YouTube.ApiError($"Failed to fetch summary for {match.Title}");

        var storedSnapshot = stored.PlaylistSnapshots.GetValueOrDefault(match.PlaylistId);
        if (
            storedSnapshot is not null
            && !string.IsNullOrEmpty(storedSnapshot.ETag)
            && !string.IsNullOrEmpty(currentSummary.ETag)
            && storedSnapshot.ETag == currentSummary.ETag
        )
        {
            Telemetry.Info(
                "Playlist {Title} unchanged (ETag match) — skipping sync",
                match.Title
            );
            return new SinglePlaylistOutcome(match.PlaylistId, stored);
        }

        var processorResult = await playlistProcessor.ProcessPlaylistAsync(currentSummary, ct);
        if (processorResult.IsError)
        {
            Telemetry.Error("Failed to process playlist {Title}: {Error}",
                currentSummary.Title, processorResult.Errors[0].Description);
            return processorResult.FirstError;
        }

        var result = processorResult.Value;

        var updated = stored with
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

        return new SinglePlaylistOutcome(currentSummary.PlaylistId, updated);
    }

    public async Task<string?> ExecuteForPlaylistTitleWithSortAsync(
        string title,
        CancellationToken ct
    )
    {
        var outcomeResult = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        if (outcomeResult.IsError)
            return null;

        var outcome = outcomeResult.Value;
        if (outcome.Id is not null)
            await syncProcessor.SortPlaylistsAsync([outcome.Id], outcome.State, ct);
        return outcome.Id;
    }

    private async Task<ErrorOr<PlaylistSnapshot>> FindPlaylistByTitleAsync(
        string title,
        YouTubeFetchState stored,
        CancellationToken ct
    )
    {
        var match = stored.PlaylistSnapshots.Values.FirstOrDefault(s =>
            s.Title.IsEqualToIgnore(title)
        );

        if (match is not null)
        {
            Telemetry.Debug("Cached ID for {Title} (skipped Playlists.list)", match.Title);
            return match;
        }

        var summaries = await playlistService.GetPlaylistSummariesAsync(ct);
        match = summaries.FirstOrDefault(s => s.Title.IsEqualToIgnore(title));

        if (match is null)
            return Errors.YouTube.ApiError($"Playlist '{title}' not found.");

        return match;
    }

    public readonly record struct SyncOutcome(IReadOnlyList<string> Ids, YouTubeFetchState State);

    public readonly record struct SinglePlaylistOutcome(string? Id, YouTubeFetchState State);

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

    private static List<PlaylistSnapshot> CombineNewAndChanged(ChangeDetectionResult changes) =>
        [.. changes.NewPlaylists, .. changes.ChangedPlaylists];

    private static void LogNoChangesNeeded(ChangeDetectionResult changes) =>
        Telemetry.Info(
            "No playlists need updating — everything is current ({Unchanged} playlists unchanged, {Deleted} deleted)",
            changes.UnchangedPlaylists.Count,
            changes.DeletedPlaylists.Count
        );

    private static void LogSyncSummary(
        TimeSpan elapsed,
        ChangeDetectionResult changes,
        YouTubeSyncProcessor.SyncResult result
    ) =>
        Telemetry.Info(
            "Sync complete in {Elapsed}s: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged | {TotalVideos} videos ({Skipped} skipped) | {PlaylistsProcessed}/{PlaylistsTotal} playlists",
            elapsed.TotalSeconds,
            changes.NewPlaylists.Count,
            changes.ChangedPlaylists.Count,
            changes.DeletedPlaylists.Count,
            changes.UnchangedPlaylists.Count,
            result.TotalVideos,
            result.SkippedVideos,
            result.ProcessedIds.Count,
            changes.NewPlaylists.Count + changes.ChangedPlaylists.Count
        );

    private async Task<ErrorOr<YouTubeFetchState>> LoadStoredStateAsync(string path, CancellationToken ct)
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
}
