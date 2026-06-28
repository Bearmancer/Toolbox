using System.Diagnostics;
using System.Net;
using Azure;
using Core;
using GoogleApiException = Google.GoogleApiException;

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
        var (ids, _) = await ExecuteCoreAsync(ct);
        return ids;
    }

    private async Task<(IReadOnlyList<string> Ids, YouTubeFetchState State)> ExecuteCoreAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        var syncStopwatch = Stopwatch.StartNew();

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
        var current = await playlistService.GetPlaylistSummariesAsync(ct);

        Telemetry.Debug("Fetched {Count} playlist summaries from API", current.Count);

        var changes = YouTubeChangeDetector.DetectChanges(current, stored);
        ArchiveDeletedPlaylists(changes.DeletedPlaylists);

        var playlistsToProcess = CombineNewAndChanged(changes);
        if (playlistsToProcess.Count == 0)
        {
            LogNoChangesNeeded(changes);
            return ([], stored);
        }

        var result = await ProcessPlaylistsAsync(playlistsToProcess, stored, ct);

        var finalState = new YouTubeFetchState
        {
            PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(result.UpdatedSnapshots),
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            AzureCharsUsed = result.AzureCharsUsed,
            AzureCharsMonth = result.CurrentMonth,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, finalState, ct);

        syncStopwatch.Stop();
        LogSyncSummary(syncStopwatch.Elapsed, changes, result);

        return (result.ProcessedIds, finalState);
    }

    public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
    {
        var (syncedIds, state) = await ExecuteCoreAsync(ct);
        if (syncedIds.Count > 0)
            await SortPlaylistsAsync(syncedIds, state, ct);
        return syncedIds;
    }

    public async Task<string?> ExecuteForPlaylistTitleAsync(
        string title,
        CancellationToken ct
    )
    {
        var (id, _) = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        return id;
    }

    private async Task<(string? Id, YouTubeFetchState State)> ExecuteForPlaylistTitleCoreAsync(
        string title,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.Google);

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
        var match = await FindPlaylistByTitleAsync(title, stored, ct);
        if (match is null)
            return (null, stored);

        var (videos, skipped, _) = await playlistProcessor.ProcessPlaylistAsync(match, ct);

        var updated = stored with
        {
            PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots)
            {
                [match.PlaylistId] = match,
            },
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, updated, ct);

        Telemetry.Info(
            "Synced playlist {Title}: {Videos} videos ({Skipped} skipped)",
            match.Title,
            videos,
            skipped
        );

        return (match.PlaylistId, updated);
    }

    public async Task<string?> ExecuteForPlaylistTitleWithSortAsync(
        string title,
        CancellationToken ct
    )
    {
        var (playlistId, state) = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        if (playlistId is not null)
            await SortPlaylistsAsync([playlistId], state, ct);
        return playlistId;
    }

    private async Task<PlaylistSnapshot?> FindPlaylistByTitleAsync(
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
            Telemetry.Error("Playlist not found: {Title}", title);

        return match;
    }

    private static List<PlaylistSnapshot> CombineNewAndChanged(
        (
            IReadOnlyList<PlaylistSnapshot> NewPlaylists,
            IReadOnlyList<PlaylistSnapshot> ChangedPlaylists,
            IReadOnlyList<PlaylistSnapshot> DeletedPlaylists,
            IReadOnlyList<PlaylistSnapshot> UnchangedPlaylists
        ) changes
    ) =>
        [.. changes.NewPlaylists, .. changes.ChangedPlaylists];

    private static void LogNoChangesNeeded(
        (
            IReadOnlyList<PlaylistSnapshot> NewPlaylists,
            IReadOnlyList<PlaylistSnapshot> ChangedPlaylists,
            IReadOnlyList<PlaylistSnapshot> DeletedPlaylists,
            IReadOnlyList<PlaylistSnapshot> UnchangedPlaylists
        ) changes
    ) =>
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
        var counters = new SyncCounters(stored);

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
        try
        {
            var (videos, skipped, azureChars) = await playlistProcessor.ProcessPlaylistAsync(
                snapshot,
                ct
            );

            await SaveIncrementalStateAsync(
                counters.UpdatedSnapshots,
                snapshot,
                counters.AzureCharsUsed + azureChars,
                counters.CurrentMonth,
                ct
            );

            return new ProcessResult(videos, skipped, azureChars, false);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.TooManyRequests)
        {
            Telemetry.Warn("Google API rate limit reached (429). Skipping remaining playlists.");
            return ProcessResult.Break;
        }
        catch (GoogleApiException ex)
            when (ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Telemetry.Error("Google API key invalid or forbidden ({Code}).", (int)ex.HttpStatusCode);
            return ProcessResult.Break;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            Telemetry.Warn("Azure translation rate limit reached (429). Skipping remaining playlists.");
            return ProcessResult.Break;
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", ex.Status);
            return ProcessResult.Break;
        }
    }

    private async Task SaveIncrementalStateAsync(
        Dictionary<string, PlaylistSnapshot> updatedSnapshots,
        PlaylistSnapshot snapshot,
        int azureCharsUsed,
        int currentMonth,
        CancellationToken ct
    )
    {
        updatedSnapshots[snapshot.PlaylistId] = snapshot;

        var state = new YouTubeFetchState
        {
            PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(updatedSnapshots),
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            AzureCharsUsed = azureCharsUsed,
            AzureCharsMonth = currentMonth,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
    }

    private static void LogSyncSummary(
        TimeSpan elapsed,
        (
            IReadOnlyList<PlaylistSnapshot> NewPlaylists,
            IReadOnlyList<PlaylistSnapshot> ChangedPlaylists,
            IReadOnlyList<PlaylistSnapshot> DeletedPlaylists,
            IReadOnlyList<PlaylistSnapshot> UnchangedPlaylists
        ) changes,
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
        try
        {
            var (repositioned, newETag) = await sortService.SortPlaylistAsync(playlistId, ct);

            if (!string.IsNullOrEmpty(newETag))
                stored.PlaylistSnapshots[playlistId] = snapshot with { ETag = newETag };

            Telemetry.Info("{Title}: {Repositioned} items repositioned", snapshot.Title, repositioned);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            Telemetry.Error("Sorting blocked for {Title}: {Error}", snapshot.Title, ex.Message);
            return false;
        }
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

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(YouTubePaths.RawDir);
        Directory.CreateDirectory(YouTubePaths.ProcessedDir);
        Directory.CreateDirectory(YouTubePaths.DeletedDir);
    }

    private sealed record SyncResult(
        IReadOnlyList<string> ProcessedIds,
        Dictionary<string, PlaylistSnapshot> UpdatedSnapshots,
        int TotalVideos,
        int SkippedVideos,
        int AzureCharsUsed,
        int CurrentMonth
    );

    private sealed record ProcessResult(
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
        public int AzureCharsUsed { get; private set; }
        public int TotalVideos { get; private set; }
        public int SkippedVideos { get; private set; }

        public SyncCounters(YouTubeFetchState stored)
        {
            UpdatedSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots);
            CurrentMonth = DateTimeOffset.UtcNow.Month;
            AzureCharsUsed = stored.AzureCharsMonth == CurrentMonth ? stored.AzureCharsUsed : 0;
        }

        public void UpdateFrom(ProcessResult result)
        {
            TotalVideos += result.Videos;
            SkippedVideos += result.Skipped;
            AzureCharsUsed += result.AzureChars;
        }

        public SyncResult ToResult(List<PlaylistSnapshot> processedSnapshots) =>
            new(
                [.. processedSnapshots.Select(s => s.PlaylistId)],
                UpdatedSnapshots,
                TotalVideos,
                SkippedVideos,
                AzureCharsUsed,
                CurrentMonth
            );
    }
}
