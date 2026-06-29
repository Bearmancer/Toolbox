using System.Diagnostics;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubePlaylistOrchestrator(
    YouTubePlaylistService playlistService,
    YouTubePlaylistProcessor playlistProcessor,
    YouTubeSortService sortService
)
{
    private static readonly string ManifestFile = Path.Combine(
        YouTubePaths.StateRoot,
        "manifest.json"
    );

    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
    {
        var outcome = await ExecuteCoreAsync(ct);
        return outcome.IsError ? [] : outcome.Value.Ids;
    }

    private async Task<ErrorOr<SyncOutcome>> ExecuteCoreAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        var syncStopwatch = Stopwatch.StartNew();

        return await PrepareSyncStateAsync(ct)
            .ThenAsync(state => AnalyzeChanges(state))
            .ThenAsync(state => ExecuteProcessingAsync(state, ct))
            .ThenAsync(outcome => PersistAndSummarizeAsync(outcome, syncStopwatch, ct));
    }

    public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
    {
        var outcomeResult = await ExecuteCoreAsync(ct);
        if (outcomeResult.IsError)
            return [];

        var outcome = outcomeResult.Value;
        if (outcome.Ids.Count > 0)
            await SortPlaylistsAsync(outcome.Ids, outcome.State, ct);
        return outcome.Ids;
    }

    public async Task<string?> ExecuteForPlaylistTitleAsync(
        string title,
        CancellationToken ct
    )
    {
        var outcome = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        return outcome.IsError ? null : outcome.Value.Id;
    }

    private async Task<ErrorOr<SinglePlaylistOutcome>> ExecuteForPlaylistTitleCoreAsync(
        string title,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.Google);

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
            await SortPlaylistsAsync([outcome.Id], outcome.State, ct);
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

    private readonly record struct SyncOutcome(IReadOnlyList<string> Ids, YouTubeFetchState State);

    private readonly record struct SinglePlaylistOutcome(string? Id, YouTubeFetchState State);

    private static List<PlaylistSnapshot> CombineNewAndChanged(ChangeDetectionResult changes) =>
        [.. changes.NewPlaylists, .. changes.ChangedPlaylists];

    private static void LogNoChangesNeeded(ChangeDetectionResult changes) =>
        Telemetry.Info(
            "No playlists need updating — everything is current ({Unchanged} playlists unchanged, {Deleted} deleted)",
            changes.UnchangedPlaylists.Count,
            changes.DeletedPlaylists.Count
        );

    private void ArchiveDeletedPlaylists(IReadOnlyList<PlaylistSnapshot> deletedPlaylists)
    {
        foreach (var snapshot in deletedPlaylists)
            ArchivePlaylist(snapshot);
    }

    private async Task<SyncResult> ProcessPlaylistsAsync(
        List<PlaylistSnapshot> playlistsToProcess,
        YouTubeFetchState stored,
        CancellationToken ct
    )
    {
        var counters = SyncCounters.FromStoredState(stored);

        Telemetry.Info(
            "Azure Translator: {Used} chars used this month (2,000,000 free tier)",
            counters.AzureCharsUsed
        );

        var processedSnapshots = new List<PlaylistSnapshot>();

        for (var i = 0; i < playlistsToProcess.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = playlistsToProcess[i];

            Telemetry.Info(
                "[{Index}/{Total}] {Title}",
                i + 1,
                playlistsToProcess.Count,
                snapshot.Title
            );

            var result = await ProcessSinglePlaylistAsync(snapshot, counters, ct);
            if (result.ShouldBreak)
                break;

            processedSnapshots.Add(snapshot);
            counters.UpdateFrom(result);
        }

        return counters.ToResult(processedSnapshots);
    }

    private async Task<ProcessResult> ProcessSinglePlaylistAsync(
        PlaylistSnapshot snapshot,
        SyncCounters counters,
        CancellationToken ct
    )
    {
        var processorResult = await playlistProcessor.ProcessPlaylistAsync(snapshot, ct);

        if (processorResult.IsError)
        {
            var error = processorResult.Errors[0];

            if (error.Code is "YT.RateLimit" or "Azure.RateLimit")
            {
                Telemetry.Warn("Rate limit reached ({Code}). Skipping remaining playlists.", error.Code);
                return ProcessResult.Break;
            }

            if (error.Code is "Azure.AuthFailed")
            {
                Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", error.Code);
                return ProcessResult.Break;
            }

            Telemetry.Error("Unexpected error processing playlist {Title}: {Error}",
                snapshot.Title, error.Description);
            return ProcessResult.Break;
        }

        var result = processorResult.Value;
        await SaveIncrementalStateAsync(
            counters.UpdatedSnapshots,
            snapshot,
            counters.AzureCharsUsed + result.AzureChars,
            counters.CurrentMonth,
            ct
        );

        return new ProcessResult(result.Videos, result.Skipped, result.AzureChars, false);
    }

    private async Task SaveIncrementalStateAsync(
        IReadOnlyDictionary<string, PlaylistSnapshot> updatedSnapshots,
        PlaylistSnapshot snapshot,
        int azureCharsUsed,
        int currentMonth,
        CancellationToken ct
    )
    {
        var snapshots = new Dictionary<string, PlaylistSnapshot>(updatedSnapshots)
        {
            [snapshot.PlaylistId] = snapshot,
        };

        var state = new YouTubeFetchState
        {
            PlaylistSnapshots = snapshots,
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            AzureCharsUsed = azureCharsUsed,
            AzureCharsMonth = currentMonth,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
    }

    private static void LogSyncSummary(
        TimeSpan elapsed,
        ChangeDetectionResult changes,
        SyncResult result
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

    private async Task SortPlaylistsAsync(
        IReadOnlyList<string> playlistIds,
        YouTubeFetchState state,
        CancellationToken ct
    )
    {
        Telemetry.Info("Sorting {Count} playlist(s) after sync", playlistIds.Count);

        var anySorted = false;

        foreach (var playlistId in playlistIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!state.PlaylistSnapshots.TryGetValue(playlistId, out var snapshot))
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
        var sortResult = await sortService.SortPlaylistAsync(playlistId, ct);

        if (sortResult.IsError)
        {
            Telemetry.Error("Sorting failed for {Title}: {Error}", snapshot.Title, sortResult.FirstError.Description);
            return false;
        }

        var result = sortResult.Value;

        if (result.Repositioned > 0)
            await playlistProcessor.RefreshLocalStateAsync(snapshot, ct);

        if (!string.IsNullOrEmpty(result.NewETag))
            stored.PlaylistSnapshots[playlistId] = snapshot with { ETag = result.NewETag };

        Telemetry.Info("{Title}: {Repositioned} items repositioned", snapshot.Title, result.Repositioned);
        return true;
    }

    private static void ArchivePlaylist(PlaylistSnapshot snapshot)
    {
        var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
        var sourcePath = Path.Combine(YouTubePaths.ProcessedDir, $"{sanitizedTitle}.json");
        var destPath = Path.Combine(YouTubePaths.DeletedDir, $"{sanitizedTitle}.json");

        if (!File.Exists(sourcePath))
            return;

        File.Move(sourcePath, destPath, true);
        Telemetry.Info("Archived deleted playlist: {Title}", snapshot.Title);
    }

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

    private readonly record struct SyncResult(
        IReadOnlyList<string> ProcessedIds,
        Dictionary<string, PlaylistSnapshot> UpdatedSnapshots,
        int TotalVideos,
        int SkippedVideos,
        int AzureCharsUsed,
        int CurrentMonth
    );

    private readonly record struct ProcessResult(
        int Videos,
        int Skipped,
        int AzureChars,
        bool ShouldBreak
    )
    {
        public static ProcessResult Break { get; } = new(0, 0, 0, true);
    }

    private sealed class SyncCounters
    {
        public Dictionary<string, PlaylistSnapshot> UpdatedSnapshots { get; }
        public int CurrentMonth { get; }
        public int AzureCharsUsed { get; set; }
        public int TotalVideos { get; set; }
        public int SkippedVideos { get; set; }

        private SyncCounters(Dictionary<string, PlaylistSnapshot> updatedSnapshots, int currentMonth, int azureCharsUsed)
        {
            UpdatedSnapshots = updatedSnapshots;
            CurrentMonth = currentMonth;
            AzureCharsUsed = azureCharsUsed;
        }

        public static SyncCounters FromStoredState(YouTubeFetchState stored)
        {
            var currentMonth = DateTimeOffset.UtcNow.Month;
            return new SyncCounters(
                new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots),
                currentMonth,
                stored.AzureCharsMonth == currentMonth ? stored.AzureCharsUsed : 0
            );
        }

        public void UpdateFrom(ProcessResult result)
        {
            TotalVideos += result.Videos;
            SkippedVideos += result.Skipped;
            AzureCharsUsed += result.AzureChars;
        }

        public SyncResult ToResult(List<PlaylistSnapshot> processedSnapshots)
        {
            var updated = new Dictionary<string, PlaylistSnapshot>(processedSnapshots.Count);
            foreach (var s in processedSnapshots)
                updated[s.PlaylistId] = s;

            return new SyncResult(
                [.. processedSnapshots.Select(s => s.PlaylistId)],
                updated,
                TotalVideos,
                SkippedVideos,
                AzureCharsUsed,
                CurrentMonth
            );
        }
    }
}
