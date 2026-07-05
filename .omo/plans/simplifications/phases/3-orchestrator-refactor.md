# Phase 3: YouTubePlaylistOrchestrator — Extract Sort + Archive + Simplify

## Task 8: Add SortMultipleAsync to YouTubeSortService

In `src/Services/Google/YouTube/YouTubeSortService.cs`, add this method after the existing `SortPlaylistAsync` method:

```csharp
    public async Task<ErrorOr<int>> SortMultipleAsync(
        IReadOnlyList<string> playlistIds,
        YouTubeFetchState state,
        CancellationToken ct)
    {
        Telemetry.Info("Sorting {Count} playlist(s) after sync", playlistIds.Count);
        var totalRepositioned = 0;

        foreach (var playlistId in playlistIds)
        {
            ct.ThrowIfCancellationRequested();
            if (!state.PlaylistSnapshots.TryGetValue(playlistId, out var snapshot)) continue;

            var sortResult = await SortPlaylistAsync(playlistId, ct);
            if (sortResult.IsError)
            {
                Telemetry.Error("Sorting failed for {Title}: {Error}", snapshot.Title, sortResult.FirstError.Description);
                break;
            }

            var result = sortResult.Value;
            if (result.Repositioned > 0)
            {
                totalRepositioned += result.Repositioned;
                if (!string.IsNullOrEmpty(result.NewETag))
                    state.PlaylistSnapshots[playlistId] = snapshot with { ETag = result.NewETag };
                Telemetry.Info("{Title}: {Repositioned} items repositioned", snapshot.Title, result.Repositioned);
            }
        }

        return totalRepositioned;
    }
```

**Must NOT:**
- Remove or change existing `SortPlaylistAsync` method
- Use block-scoped namespaces

**QA:**
```bash
dotnet build src/Services/Google/Google.csproj
```

**Commit:** `feat(youtube): add SortMultipleAsync to YouTubeSortService`

---

## Task 9: Add RefreshAfterSort to YouTubePlaylistProcessor

In `src/Services/Google/YouTube/YouTubePlaylistProcessor.cs`, the existing `RefreshLocalStateAsync` method already exists. No change needed — it's already public and takes `(PlaylistSnapshot, CancellationToken)`.

**QA:**
```bash
dotnet build src/Services/Google/Google.csproj
```

**Commit:** `chore(youtube): confirm RefreshLocalStateAsync already public`

---

## Task 10: Add ArchiveDeleted to YouTubeFetchState

In `src/Services/Google/YouTube/YouTubeFetchState.cs`, add this method inside the `YouTubeFetchState` record:

```csharp
    public static void ArchiveDeleted(IReadOnlyList<PlaylistSnapshot> deletedPlaylists)
    {
        foreach (var snapshot in deletedPlaylists)
        {
            var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
            var sourcePath = Path.Combine(YouTubePaths.ProcessedDir, $"{sanitizedTitle}.json");
            var destPath = Path.Combine(YouTubePaths.DeletedDir, $"{sanitizedTitle}.json");
            if (!File.Exists(sourcePath)) continue;
            File.Move(sourcePath, destPath, true);
            Telemetry.Info("Archived deleted playlist: {Title}", snapshot.Title);
        }
    }
```

**Must NOT:**
- Remove or change existing `LoadAsync`/`SaveAsync` methods
- Import anything beyond what's already imported (`System.Text.Json`, `System.Text.Json.Serialization`, `Core`)

**QA:**
```bash
dotnet build src/Services/Google/Google.csproj
```

**Commit:** `feat(youtube): add ArchiveDeleted to YouTubeFetchState`

---

## Task 11: Refactor YouTubePlaylistOrchestrator

Replace entire contents of `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs` with:

```csharp
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
    private static readonly string ManifestFile = Path.Combine(YouTubePaths.StateRoot, "manifest.json");

    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
    {
        var outcome = await ExecuteCoreAsync(ct);
        return outcome.IsSuccess ? outcome.Value.Ids : [];
    }

    public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
    {
        var outcomeResult = await ExecuteCoreAsync(ct);
        if (outcomeResult.IsError) return [];
        var outcome = outcomeResult.Value;
        if (outcome.Ids.Count > 0)
            await SortAndRefreshAsync(outcome.Ids, outcome.State, ct);
        return outcome.Ids;
    }

    public async Task<string?> ExecuteForPlaylistTitleAsync(string title, CancellationToken ct)
    {
        var outcome = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        return outcome.IsSuccess ? outcome.Value.Id : null;
    }

    public async Task<string?> ExecuteForPlaylistTitleWithSortAsync(string title, CancellationToken ct)
    {
        var outcomeResult = await ExecuteForPlaylistTitleCoreAsync(title, ct);
        if (outcomeResult.IsError) return null;
        var outcome = outcomeResult.Value;
        if (outcome.Id is not null)
            await SortAndRefreshAsync([outcome.Id], outcome.State, ct);
        return outcome.Id;
    }

    private async Task<ErrorOr<SyncOutcome>> ExecuteCoreAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        var syncStopwatch = Stopwatch.StartNew();

        return await LoadStoredStateAsync(ct)
            .ThenAsync(stored => FetchSummariesAndDetectAsync(stored, ct))
            .ThenAsync(ctx => ProcessIfNeededAsync(ctx, ct))
            .Then(outcome => Finalize(outcome, syncStopwatch));
    }

    private async Task<ErrorOr<SyncContext>> FetchSummariesAndDetectAsync(YouTubeFetchState stored, CancellationToken ct)
    {
        var current = await playlistService.GetPlaylistSummariesAsync(ct);
        var changes = YouTubeChangeDetector.DetectChanges(current, stored);
        YouTubeFetchState.ArchiveDeleted(changes.DeletedPlaylists);
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
        var result = await ProcessPlaylistsAsync(ctx.ToProcess, ctx.Stored, ct);
        return new ProcessOutcome(ctx.Stored, ctx.Changes, result);
    }

    private static ErrorOr<SyncOutcome> Finalize(ProcessOutcome outcome, Stopwatch syncStopwatch)
    {
        if (outcome.Result is { } result)
            LogSyncSummary(syncStopwatch.Elapsed, outcome.Changes, result);
        IReadOnlyList<string> ids = outcome.Result?.ProcessedIds ?? [];
        return new SyncOutcome(ids, outcome.Stored);
    }

    private async Task<ErrorOr<SinglePlaylistOutcome>> ExecuteForPlaylistTitleCoreAsync(string title, CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        return await LoadStoredStateAsync(ct)
            .ThenAsync<YouTubeFetchState, SinglePlaylistOutcome>(
                stored => ProcessTitlePipelineAsync(title, stored, ct));
    }

    private async Task<ErrorOr<SinglePlaylistOutcome>> ProcessTitlePipelineAsync(string title, YouTubeFetchState stored, CancellationToken ct)
    {
        var matchResult = await FindPlaylistByTitleAsync(title, stored, ct);
        if (matchResult.IsError) return matchResult.FirstError;

        var match = matchResult.Value;
        var currentSummary = await playlistService.GetPlaylistSummaryAsync(match.PlaylistId, ct);
        if (currentSummary is null)
            return Errors.YouTube.ApiError($"Failed to fetch summary for {match.Title}");

        var storedSnapshot = stored.PlaylistSnapshots.GetValueOrDefault(match.PlaylistId);
        if (storedSnapshot is not null
            && !string.IsNullOrEmpty(storedSnapshot.ETag)
            && !string.IsNullOrEmpty(currentSummary.ETag)
            && storedSnapshot.ETag == currentSummary.ETag)
        {
            Telemetry.Info("Playlist {Title} unchanged (ETag match) — skipping sync", match.Title);
            return new SinglePlaylistOutcome(match.PlaylistId, stored);
        }

        var processorResult = await playlistProcessor.ProcessPlaylistAsync(currentSummary, ct);
        if (processorResult.IsError)
        {
            Telemetry.Error("Failed to process playlist {Title}: {Error}", currentSummary.Title, processorResult.Errors[0].Description);
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
        Telemetry.Info("Synced playlist {Title}: {Videos} videos ({Skipped} skipped)", currentSummary.Title, result.Videos, result.Skipped);
        return new SinglePlaylistOutcome(currentSummary.PlaylistId, updated);
    }

    private async Task<ErrorOr<PlaylistSnapshot>> FindPlaylistByTitleAsync(string title, YouTubeFetchState stored, CancellationToken ct)
    {
        var match = stored.PlaylistSnapshots.Values.FirstOrDefault(s => s.Title.IsEqualToIgnore(title));
        if (match is not null)
        {
            Telemetry.Debug("Cached ID for {Title} (skipped Playlists.list)", match.Title);
            return match;
        }
        var summaries = await playlistService.GetPlaylistSummariesAsync(ct);
        match = summaries.FirstOrDefault(s => s.Title.IsEqualToIgnore(title));
        return match is null
            ? Errors.YouTube.ApiError($"Playlist '{title}' not found.")
            : match;
    }

    private async Task SortAndRefreshAsync(IReadOnlyList<string> playlistIds, YouTubeFetchState state, CancellationToken ct)
    {
        var sortResult = await sortService.SortMultipleAsync(playlistIds, state, ct);
        if (sortResult.IsError) return;
        if (sortResult.Value > 0)
        {
            foreach (var id in playlistIds)
                if (state.PlaylistSnapshots.TryGetValue(id, out var snapshot))
                    await playlistProcessor.RefreshLocalStateAsync(snapshot, ct);
            await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
        }
    }

    private async Task<SyncResult> ProcessPlaylistsAsync(List<PlaylistSnapshot> playlistsToProcess, YouTubeFetchState stored, CancellationToken ct)
    {
        var currentMonth = DateTimeOffset.UtcNow.Month;
        var azureCharsUsed = stored.AzureCharsMonth == currentMonth ? stored.AzureCharsUsed : 0;
        var totalVideos = 0;
        var skippedVideos = 0;
        var processedIds = new List<string>();
        var updatedSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots);

        Telemetry.Info("Azure Translator: {Used} chars used this month (2,000,000 free tier)", azureCharsUsed);

        for (var i = 0; i < playlistsToProcess.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = playlistsToProcess[i];
            Telemetry.Info("[{Index}/{Total}] {Title}", i + 1, playlistsToProcess.Count, snapshot.Title);

            var processorResult = await playlistProcessor.ProcessPlaylistAsync(snapshot, ct);
            if (processorResult.IsError)
            {
                var error = processorResult.Errors[0];
                if (error.Code is "YT.RateLimit" or "Azure.RateLimit")
                {
                    Telemetry.Warn("Rate limit reached ({Code}). Skipping remaining playlists.", error.Code);
                    break;
                }
                if (error.Code is "Azure.AuthFailed")
                {
                    Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", error.Code);
                    break;
                }
                Telemetry.Error("Unexpected error processing playlist {Title}: {Error}", snapshot.Title, error.Description);
                break;
            }

            var result = processorResult.Value;
            totalVideos += result.Videos;
            skippedVideos += result.Skipped;
            azureCharsUsed += result.AzureChars;
            processedIds.Add(snapshot.PlaylistId);
            updatedSnapshots[snapshot.PlaylistId] = snapshot;

            var state = new YouTubeFetchState
            {
                PlaylistSnapshots = updatedSnapshots,
                LastChecked = DateTimeOffset.UtcNow,
                LastUpdated = DateTimeOffset.UtcNow,
                AzureCharsUsed = azureCharsUsed,
                AzureCharsMonth = currentMonth,
            };
            await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
        }

        return new SyncResult(processedIds, updatedSnapshots, totalVideos, skippedVideos, azureCharsUsed, currentMonth);
    }

    private async Task<ErrorOr<YouTubeFetchState>> LoadStoredStateAsync(CancellationToken ct)
    {
        try { return await YouTubeFetchState.LoadAsync(ManifestFile, ct); }
        catch (Exception ex) { return Errors.YouTube.ApiError($"State load failed: {ex.Message}"); }
    }

    private readonly record struct SyncOutcome(IReadOnlyList<string> Ids, YouTubeFetchState State);
    private readonly record struct SinglePlaylistOutcome(string? Id, YouTubeFetchState State);
    private readonly record struct SyncContext(YouTubeFetchState Stored, ChangeDetectionResult Changes, List<PlaylistSnapshot> ToProcess);
    private readonly record struct ProcessOutcome(YouTubeFetchState Stored, ChangeDetectionResult Changes, SyncResult? Result);
    private readonly record struct SyncResult(IReadOnlyList<string> ProcessedIds, Dictionary<string, PlaylistSnapshot> UpdatedSnapshots, int TotalVideos, int SkippedVideos, int AzureCharsUsed, int CurrentMonth);

    private static List<PlaylistSnapshot> CombineNewAndChanged(ChangeDetectionResult changes) =>
        [.. changes.NewPlaylists, .. changes.ChangedPlaylists];

    private static void LogNoChangesNeeded(ChangeDetectionResult changes) =>
        Telemetry.Info("No playlists need updating — everything is current ({Unchanged} playlists unchanged, {Deleted} deleted)",
            changes.UnchangedPlaylists.Count, changes.DeletedPlaylists.Count);

    private static void LogSyncSummary(TimeSpan elapsed, ChangeDetectionResult changes, SyncResult result) =>
        Telemetry.Info(
            "Sync complete in {Elapsed}s: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged | {TotalVideos} videos ({Skipped} skipped) | {PlaylistsProcessed}/{PlaylistsTotal} playlists",
            elapsed.TotalSeconds, changes.NewPlaylists.Count, changes.ChangedPlaylists.Count,
            changes.DeletedPlaylists.Count, changes.UnchangedPlaylists.Count,
            result.TotalVideos, result.SkippedVideos, result.ProcessedIds.Count,
            changes.NewPlaylists.Count + changes.ChangedPlaylists.Count);
}
```

**Key changes from original:**
- Removed `SyncCounters` mutable nested class (50 lines) — replaced with local variables in `ProcessPlaylistsAsync`
- Removed `SortPlaylistsAsync` + `SortSinglePlaylistAsync` (53 lines) — replaced with `SortAndRefreshAsync` delegating to `sortService.SortMultipleAsync`
- Removed `ArchivePlaylist` + `ArchiveDeletedPlaylists` (12 lines) — replaced with `YouTubeFetchState.ArchiveDeleted` call
- Removed `ProcessResult` record struct — no longer needed without SyncCounters
- `ProcessPlaylistsAsync` uses local variables instead of mutable counter class
- File went from 505 lines → ~200 lines

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Keep `SyncCounters`, `ProcessResult`, `ArchivePlaylist`, `ArchiveDeletedPlaylists`, `SortPlaylistsAsync`, `SortSinglePlaylistAsync`

**QA:**
```bash
dotnet build
```
Expected: Clean build. Orchestrator went from 505 lines → ~200 lines.

**Commit:** `refactor(youtube): slim YouTubePlaylistOrchestrator — extract sort/archive, eliminate SyncCounters`
