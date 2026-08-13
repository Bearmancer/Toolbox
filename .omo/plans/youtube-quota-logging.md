# Plan: YouTube Quota + Logging Fixes

**Status:** approved  
**Intent:** CLEAR  
**Date:** 2026-08-13

---

## summary

4 bugs in YouTube sync: (1) misleading "changed" count vs sort-modified count, (2) quota exhaustion from 250+ writes/run with no early-exit, (3) empty repo logs due to CWD-relative path, (4) no diagnostic detail persisted to file.

All fixes use TDD-style verification: document failing state (current bug), implement fix, verify passing state (bug gone).

---

## task 1: fix log path (always inside Toolbox dir)

### failing state (current bug)
```powershell
cd C:\Users\Lance
.\Dev\Toolbox\artifacts\bin\App\debug\App.exe sync youtube
# Observe: C:\Users\Lance\Dev\Toolbox\logs\youtube.jsonl is EMPTY
# Observe: C:\Users\Lance\logs\youtube.jsonl has content (wrong location)
```

### passing state (after fix)
```powershell
cd C:\Users\Lance
.\Dev\Toolbox\artifacts\bin\App\debug\App.exe sync youtube
# Observe: C:\Users\Lance\Dev\Toolbox\logs\youtube.jsonl has content (PASS)
# Observe: C:\Users\Lance\logs\youtube.jsonl doesn't exist or is stale
```

### implementation
**File:** `src/Core/Telemetry.cs:26`

```csharp
// BEFORE:
AddServiceLogger(config, service, $"logs/{service.ToFileSlug()}.jsonl");

// AFTER:
var logDir = Path.Combine(PathResolver.RepoRoot, "logs");
Directory.CreateDirectory(logDir);
AddServiceLogger(config, service, Path.Combine(logDir, $"{service.ToFileSlug()}.jsonl"));
```

`PathResolver.RepoRoot` walks up from `AppContext.BaseDirectory` to find `.git` or `.env` → resolves to `C:\Users\Lance\Dev\Toolbox\`. Logs always inside Toolbox dir, regardless of CWD.

### acceptance criteria
- [x] `logs/youtube.jsonl` has content after sync run from any directory
- [x] No logs created in CWD or .exe directory

---

## task 2: decouple file log level (always capture Debug+)

### failing state (current bug)
```powershell
cd C:\Users\Lance\Dev\Toolbox
.\artifacts\bin\App\debug\App.exe sync youtube
# Run WITHOUT --verbose
# Observe: logs/youtube.jsonl has only Info/Warning/Error entries
# Observe: no Debug entries (SortPlaylist pass details missing)
```

### passing state (after fix)
```powershell
cd C:\Users\Lance\Dev\Toolbox
.\artifacts\bin\App\debug\App.exe sync youtube
# Run WITHOUT --verbose
# Observe: logs/youtube.jsonl has Debug entries (PASS)
# Example: "YouTube.SortPlaylist pass 1: X updated, Y failed"
```

### implementation
**File:** `src/Core/Telemetry.cs:16-56`

```csharp
// BEFORE: file sink shares LevelSwitch with console
// AFTER: file sink has its own fixed Debug level

private static async Task AddServiceLogger(
    LoggerConfiguration config,
    ServiceName service,
    string path
)
{
    _ = config.WriteTo.Logger(lc =>
        lc.Filter.ByIncludingOnly(e =>
                e.Properties.TryGetValue("Service", out LogEventPropertyValue? propValue)
                && propValue is ScalarValue sv
                && sv.Value is string serviceName
                && serviceName == service.ToString()
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                path,
                rollingInterval: RollingInterval.Infinite,
                retainedFileCountLimit: null,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                restrictedToMinimumLevel: LogEventLevel.Debug  // <-- ADD: always capture Debug+
            )
    );
}
```

Console still respects `--verbose`/`--debug`/default. File always captures Debug+ for diagnostics.

### acceptance criteria
- [x] JSONL files contain Debug entries after default (non-verbose) run
- [x] Console output unchanged (Info by default, Debug with --verbose)

---

## task 3: early-exit on quota/rate-limit errors

### failing state (current bug)
```powershell
# Run sync that hits quota
# Observe console: 109+ "Failed to update ... quota" errors
# Observe: each 403 costs 50 units (wasted), hammering continues
# Observe: total errors ~109 (lines 16-124 in youtube.jsonl)
```

### passing state (after fix)
```powershell
# Run sync that hits quota
# Observe console: "Quota exhausted, stopping sort" after FIRST quota error
# Observe: no hammering, no 100+ failed calls
# Observe: remaining playlists skipped gracefully
```

### implementation
**File:** `src/Services/Google/YouTube/YouTubeSortService.cs:196-266` (`ExecuteSortPlanAsync`)

```csharp
// BEFORE: catches exception, increments failures, CONTINUES loop
// AFTER: detect quota/rate-limit, BREAK immediately

for (var i = 0; i < plan.Updates.Count; i++)
{
    // ... existing code ...
    try
    {
        await yt.PlaylistItems.Update(item, "snippet").ExecuteAsync(ct);
        successes++;
    }
    catch (GoogleApiException ex) when (IsQuotaOrRateLimit(ex))
    {
        Telemetry.Error("Quota/rate-limit exhausted at item {Index}/{Total}. Stopping sort.",
            i + 1, plan.Updates.Count);
        return Errors.YouTube.QuotaExceeded($"Quota exhausted after {successes} updates");
    }
    catch (Exception ex)
    {
        failures++;
        Telemetry.Error("Failed to update ItemId={ItemId} to position {Position}: {Error}",
            itemId, newPosition, ex.Message);
    }
    // ...
}

private static bool IsQuotaOrRateLimit(GoogleApiException ex)
    => ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
       && ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);
```

Return `ErrorOr` error → caller breaks pass loop → caller breaks playlist loop → graceful stop.

### acceptance criteria
- [x] First quota error triggers immediate stop (no hammering)
- [x] Console shows "Quota exhausted" message
- [x] Remaining playlists skipped without attempting updates

---

## task 4: quota budget per run (cap at 150 writes)

### failing state (current bug)
```powershell
# Run sync with large backlog
# Observe: 250+ write attempts
# Observe: quota exhausted mid-batch (positions 78-241+)
# Observe: 12,800+ units consumed (> 10,000/day limit)
```

### passing state (after fix)
```powershell
# Run sync with large backlog
# Observe: max ~150 write attempts
# Observe: "Quota budget reached (150/150 writes). Stopping sort." message
# Observe: graceful stop before quota exhaustion
# Observe: next run continues with remaining playlists
```

### implementation
**Files:**
- `src/Services/Google/YouTube/YouTubeSyncProcessor.cs:147-173` (`SortPlaylistsAsync`)
- `src/Services/Google/YouTube/YouTubeSortService.cs:11-114` (`SortPlaylistAsync`)

```csharp
// YouTubeSyncProcessor.cs - pass budget to sort

public async Task SortPlaylistsAsync(
    IReadOnlyList<string> playlistIds,
    YouTubeFetchState state,
    CancellationToken ct
)
{
    const int maxWritesPerRun = 150;  // 150 * 50 = 7,500 units, leaves headroom
    var writesConsumed = 0;

    foreach (var playlistId in playlistIds)
    {
        if (writesConsumed >= maxWritesPerRun)
        {
            Telemetry.Info("Quota budget reached ({Writes}/{Max} writes). Stopping sort.",
                writesConsumed, maxWritesPerRun);
            break;
        }

        var result = await SortSinglePlaylistAsync(playlistId, snapshot, state, ct,
            remainingBudget: maxWritesPerRun - writesConsumed);
        
        if (result.IsError)
            break;  // quota error or other failure
        
        writesConsumed += result.WritesConsumed;
    }
}
```

```csharp
// YouTubeSortService.cs - enforce budget within sort

public async Task<ErrorOr<SortResult>> SortPlaylistAsync(
    string playlistId,
    IReadOnlyDictionary<string, string> translatedTitles,
    int remainingBudget,  // <-- ADD parameter
    CancellationToken ct
)
{
    // ... existing code ...
    var passResult = await ExecuteSortPlanAsync(plan, remainingBudget, ct);
    // ...
}

private async Task<ErrorOr<SortPassResult>> ExecuteSortPlanAsync(
    SortPlan plan,
    int remainingBudget,  // <-- ADD parameter
    CancellationToken ct
)
{
    var maxUpdatesThisPass = Math.Min(plan.Updates.Count, remainingBudget);
    
    for (var i = 0; i < maxUpdatesThisPass; i++)
    {
        // ... existing update logic ...
        if (writesConsumed >= remainingBudget)
        {
            Telemetry.Warn("Quota budget exhausted mid-playlist. Stopping after {Count} writes.",
                writesConsumed);
            return new SortPassResult(successes, failures, writesConsumed);
        }
    }
}
```

### acceptance criteria
- [x] Max 150 writes per run (configurable constant)
- [x] "Quota budget reached" message when limit hit
- [x] Graceful stop, no quota exhaustion errors
- [x] Next run continues with remaining playlists

---

## task 5: break churn cycle (track sort state, prioritize intelligently)

### failing state (current bug)
```powershell
# Run sync twice
# Observe: same 20 playlists sorted both times (alphabetical by ID)
# Observe: partially-sorted playlists re-attempted every run
# Observe: churn cycle never resolves
```

### passing state (after fix)
```powershell
# Run sync twice
# Observe: first run sorts changed + interrupted playlists
# Observe: second run skips already-sorted playlists (0 moves)
# Observe: churn cycle broken, progress made each run
```

### implementation
**Files:**
- `src/Services/Google/YouTube/YouTubeFetchState.cs` - add sort state tracking
- `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:187-216` (`ExecuteWithSortAsync`)

```csharp
// YouTubeFetchState.cs - add sort state to PlaylistSnapshot

public record PlaylistSnapshot
{
    // ... existing fields ...
    public int? LastSortMoves { get; init; }  // moves needed last sort (null = never sorted)
    public DateTimeOffset? LastSortAttempted { get; init; }
    public bool LastSortCompleted { get; init; }  // true if 0 moves or fully sorted
}
```

```csharp
// YouTubePlaylistOrchestrator.cs - prioritize changed + interrupted

var prioritizedIds = allPlaylistIds
    .OrderByDescending(id => processedIds.Contains(id))  // changed first
    .ThenByDescending(id => state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortMoves ?? 0)  // most-unsorted first
    .ThenBy(id => state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortAttempted ?? DateTimeOffset.MinValue)  // oldest attempt first
    .Where(id => !state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortCompleted ?? true)  // skip already-sorted
    .Take(20)
    .ToList();
```

```csharp
// YouTubeSortService.cs - update sort state after sort

var sortState = new SortState
{
    LastSortMoves = totalRepositioned,
    LastSortAttempted = DateTimeOffset.UtcNow,
    LastSortCompleted = totalRepositioned == 0 || allSorted
};
```

### acceptance criteria
- [x] Sort state tracked per playlist in manifest
- [x] Prioritization: changed > most-unsorted > oldest-attempt
- [x] Already-sorted playlists skipped
- [x] Churn cycle broken (progress each run)

---

## task 6: accurate sort reporting (separate counts)

### failing state (current bug)
```powershell
# Run sync
# Observe: "1 changed" but 9+ playlists repositioned
# Observe: misleading report (changed != modified)
```

### passing state (after fix)
```powershell
# Run sync
# Observe: "Sync: 1 changed (YouTube)" (accurate)
# Observe: "Sort: 9 modified, 11 already-sorted, 20 attempted" (accurate)
# Observe: clear separation of sync vs sort work
```

### implementation
**File:** `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:168-185` (`Finalize`)

```csharp
// BEFORE:
Telemetry.Info("Sync done ... {New} new, {Changed} changed ...", ...);

// AFTER:
Telemetry.Info(
    "Sync done in {Elapsed:F1}s: {New} new, {Changed} changed (YouTube) | {TotalVideos} videos",
    syncStopwatch.Elapsed.TotalSeconds,
    outcome.Changes.NewPlaylists.Count,
    outcome.Changes.ChangedPlaylists.Count,
    result.TotalVideos
);

// Add sort summary after sort completes
if (sortResult is { })
{
    Telemetry.Info(
        "Sort complete: {Attempted} attempted, {Modified} modified, {AlreadySorted} already-sorted | {TotalWrites} writes ({WritesUnits} units)",
        sortResult.Attempted,
        sortResult.Modified,
        sortResult.AlreadySorted,
        sortResult.TotalWrites,
        sortResult.TotalWrites * 50
    );
}
```

**File:** `src/Services/Google/YouTube/YouTubeSortService.cs:56-62` (per-playlist log)

```csharp
// BEFORE: "X repositioned" (ambiguous across passes)
// AFTER: track distinct item IDs

var distinctItemsMoved = updates
    .Where(u => u.Success)
    .Select(u => u.Item.Id)
    .Distinct()
    .Count();

Telemetry.Info("{PlaylistName} — {Distinct}/{Total} items sorted ({ApiCalls} API calls)",
    playlistName, distinctItemsMoved, itemCount, totalRepositioned);
```

### acceptance criteria
- [x] Sync summary: "X changed (YouTube)" (accurate)
- [x] Sort summary: "X modified, Y already-sorted, Z attempted" (accurate)
- [x] Per-playlist: distinct items sorted vs API calls (no inflation)
- [x] Write count + units logged

---

## verification summary

| task | failing command | passing command |
|------|----------------|-----------------|
| 1 (log path) | sync from `C:\Users\Lance\`, repo `logs/` empty | sync from `C:\Users\Lance\`, repo `logs/` has content |
| 2 (file level) | sync without --verbose, JSONL has no Debug | sync without --verbose, JSONL has Debug |
| 3 (early-exit) | sync hits quota, 109+ failed 403s | sync hits quota, stops after first error |
| 4 (quota budget) | sync attempts 250+ writes, quota dies | sync caps at 150 writes, stops gracefully |
| 5 (churn cycle) | sync twice, same 20 playlists both times | sync twice, second run skips sorted |
| 6 (reporting) | "1 changed" but 9+ modified | "1 changed (YouTube)" + "9 modified (sort)" |

---

## implementation order

1. **Task 1** (log path) — foundational, enables verification of other tasks
2. **Task 2** (file level) — enables diagnostic logging for debugging
3. **Task 3** (early-exit) — immediate safety improvement
4. **Task 4** (quota budget) — prevents quota exhaustion
5. **Task 5** (churn cycle) — breaks the perpetual re-sort loop
6. **Task 6** (reporting) — accurate counts, depends on tasks 3-5

Tasks 1-2 are logging infrastructure. Tasks 3-5 are quota fixes. Task 6 is reporting polish.

---

## dependencies

- Task 5 depends on Task 3 (need sort state to track interrupted playlists)
- Task 6 depends on Tasks 3-5 (need accurate counts from quota budgeting)
- Tasks 1-2 independent, can be done first

---

## must-not-have

- no test NuGet packages (xUnit, NUnit, MSTest) — per AGENTS.md
- no new dependencies
- no concurrent sort (would worsen quota)
- no batch size < 20 (quota cap handles safety)
- no reducing write limit below 150 (leaves no headroom for reads)
