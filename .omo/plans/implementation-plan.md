# Implementation Plan — LIS Fix + Manifest Cleanup

## Scope

| #   | Item                                            | File(s)                          | Severity |
| --- | ----------------------------------------------- | -------------------------------- | -------- |
| 1   | LIS O(n²) → O(n log n) + index domain bugs      | `YouTubeSortService.cs`          | Medium   |
| 2   | Remove `FetchComplete` (written but never read) | `YouTubeFetchState.cs`           | Low      |
| 3   | Optimize Sort Phase State Passing               | `YouTubePlaylistOrchestrator.cs` | Low      |

> **Rule:** build-verify after every commit. 1–3 files per commit.

---

## Commit 1 — Fix LIS (`YouTubeSortService.cs`)

**Touches:** `YouTubeSortService.cs` only.

### Pre-state

```csharp
// SortPlaylistAsync — input construction
var sorted = items.OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase).ToList();

var desiredPositions = new int[items.Count];
foreach (var item in sorted)
{
    var desiredIndex = sorted.IndexOf(item);           // O(n) per item; reference equality
    var currentPosition = item.Snippet.Position ?? 0;  // NOT guaranteed 0-based dense
    if (currentPosition < desiredPositions.Length)
        desiredPositions[currentPosition] = desiredIndex;
}

var lisIndices = LongestIncreasingSubsequence(desiredPositions);
var lisSet = new HashSet<int>(lisIndices);  // indices in current-position domain

for (var i = 0; i < sorted.Count; i++)     // i in target-slot domain — WRONG DOMAIN
{
    if (!lisSet.Contains(i))
        toUpdate.Add((sorted[i], i));
}

// LongestIncreasingSubsequence — O(n²)
private static List<int> LongestIncreasingSubsequence(int[] arr)
{
    ...
    for (var i = 1; i < n; i++)
        for (var j = 0; j < i; j++)        // inner loop — O(n²) total
            if (arr[j] < arr[i] && dp[j] + 1 > dp[i]) { ... }
    ...
}
```

**Bugs:**
1. `sorted.IndexOf(item)` — O(n), reference equality — accidental correctness only
2. `desiredPositions[currentPosition]` — `Position` is nullable, not guaranteed dense 0-based
3. `lisSet.Contains(i)` — tests target-slot domain against current-position domain result
4. Double loop is O(n²)

### Post-state

```csharp
// SortPlaylistAsync — input construction (fixed)
var sorted = items.OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase).ToList();

// O(1) rank lookup by item Id
var targetRank = sorted
    .Select((item, idx) => (item.Id, idx))
    .ToDictionary(x => x.Id, x => x.idx);

// Permutation in current-position order (0-based, dense by loop index not Position field)
var currentOrder = items.OrderBy(i => i.Snippet.Position ?? 0).ToList();
var permutation = currentOrder
    .Select(item => targetRank[item.Id])
    .ToArray();

var lisCurrentIndices = LongestIncreasingSubsequence(permutation);
// lisCurrentIndices are indices into currentOrder (current-position domain — correct)

var keptIds = lisCurrentIndices.Select(i => currentOrder[i].Id).ToHashSet();

for (var i = 0; i < sorted.Count; i++)  // i in target-slot domain
{
    if (!keptIds.Contains(sorted[i].Id))  // compare by ID — same domain as sorted[]
        toUpdate.Add((sorted[i], i));
}

// LongestIncreasingSubsequence — O(n log n) patience sort with reconstruction
private static List<int> LongestIncreasingSubsequence(int[] arr)
{
    var n = arr.Length;
    if (n == 0) return [];

    var tails = new List<int>();       // tails[k] = smallest tail of IS of length k+1
    var tailsIdx = new List<int>();    // which arr index holds that tail
    var predecessor = new int[n];
    Array.Fill(predecessor, -1);

    for (var i = 0; i < n; i++)
    {
        // Binary search: leftmost position where tails[pos] >= arr[i]
        int lo = 0, hi = tails.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (tails[mid] < arr[i]) lo = mid + 1;
            else hi = mid;
        }

        if (lo == tails.Count)
        {
            tails.Add(arr[i]);
            tailsIdx.Add(i);
        }
        else
        {
            tails[lo] = arr[i];
            tailsIdx[lo] = i;
        }

        if (lo > 0)
            predecessor[i] = tailsIdx[lo - 1];
    }

    // Reconstruct indices
    var result = new List<int>();
    var cur = tailsIdx[^1];
    while (cur >= 0)
    {
        result.Add(cur);
        cur = predecessor[cur];
    }
    result.Reverse();
    return result;
}
```

---

## Commit 2 — Remove `FetchComplete` (`YouTubeFetchState.cs`)

**Touches:** `YouTubeFetchState.cs` and `YouTubePlaylistOrchestrator.cs`.

`FetchComplete` is written in 3 places but never read. It adds no value and clutters the state.

### Post-state

1. **`YouTubeFetchState.cs`**: Delete the `FetchComplete` property entirely. Remove it from the `LoadAsync` fallback object.
2. **`YouTubePlaylistOrchestrator.cs`**: Remove `FetchComplete` assignments in `UpdateManifestForSinglePlaylistAsync`, `SaveIncrementalStateAsync`, and `SaveFinalStateAsync`. Remove it from the `SyncResult` record.

Existing `manifest.json` files on disk that contain `"FetchComplete": true/false` will deserialize cleanly — `System.Text.Json` ignores unknown fields by default.

---

## Commit 3 — Optimize Sort Phase State (`YouTubePlaylistOrchestrator.cs`)

We keep the incremental manifest saves during sync (for crash resilience), but we eliminate the unnecessary disk read/write cycle during the sort phase.

### Pre-state

```csharp
// SortPlaylistsAsync (reloads from disk after sync just wrote it)
private async Task SortPlaylistsAsync(IReadOnlyList<string> playlistIds, CancellationToken ct)
{
    var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);  // ← disk read
    ...
    if (anySorted)
        await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);   // ← disk write
}

// ExecuteWithSortAsync
public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
{
    var syncedIds = await ExecuteAsync(ct);
    if (syncedIds.Count > 0)
        await SortPlaylistsAsync(syncedIds, ct);
    return syncedIds;
}
```

### Post-state

Change `ExecuteAsync` to return the final `YouTubeFetchState` alongside the synced IDs, passing it directly into the sort phase in memory.

```csharp
// ExecuteAsync (Internal Core)
private async Task<(IReadOnlyList<string> Ids, YouTubeFetchState State)> ExecuteCoreAsync(CancellationToken ct)
{
    // ... normal sync logic with incremental saves ...
    await SaveFinalStateAsync(result, ct);
    
    // Build the final state to return
    var finalState = new YouTubeFetchState
    {
        PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(result.UpdatedSnapshots),
        LastChecked = DateTimeOffset.UtcNow,
        LastUpdated = DateTimeOffset.UtcNow,
        AzureCharsUsed = result.AzureCharsUsed,
        AzureCharsMonth = result.CurrentMonth,
    };
    
    return (result.ProcessedIds, finalState);
}

// ExecuteAsync (Public)
public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
{
    var (ids, _) = await ExecuteCoreAsync(ct);
    return ids;
}

// SortPlaylistsAsync: accept state, no disk read
private async Task SortPlaylistsAsync(
    IReadOnlyList<string> playlistIds,
    YouTubeFetchState state,           // ← passed in, not loaded from disk
    CancellationToken ct
)
{
    Telemetry.Info("Sorting {Count} playlist(s) after sync", playlistIds.Count);
    var anySorted = false;
    
    foreach (var playlistId in playlistIds)
    {
        if (!state.PlaylistSnapshots.TryGetValue(playlistId, out var snapshot))
            continue;
        var sorted = await SortSinglePlaylistAsync(playlistId, snapshot, state, ct);
        if (sorted) anySorted = true;
        else break;
    }
    
    if (anySorted)
        await YouTubeFetchState.SaveAsync(ManifestFile, state, ct); // single write after sort
}

// ExecuteWithSortAsync
public async Task<IReadOnlyList<string>> ExecuteWithSortAsync(CancellationToken ct)
{
    var (ids, state) = await ExecuteCoreAsync(ct);
    if (ids.Count > 0)
        await SortPlaylistsAsync(ids, state, ct);   // no disk read
    return ids;
}
```

*Note: The same pattern applies to `ExecuteForPlaylistTitleWithSortAsync` — we change `ExecuteForPlaylistTitleAsync` to return `(string? Id, YouTubeFetchState? State)`.*

---

## Commit Order

```
commit 1: fix(sort): O(n log n) LIS with correct index domain mapping
commit 2: chore(state): remove unused FetchComplete flag
commit 3: refactor(orchestrator): pass in-memory state to sort phase
```
