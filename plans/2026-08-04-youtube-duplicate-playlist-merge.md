# YouTube Duplicate Playlist Merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` to implement this plan task-by-task. Every task gets a fresh implementer and a separate task reviewer. Steps use checkbox syntax for tracking.

**Goal:** Automatically consolidate duplicate YouTube playlists during every sync by transferring live playlist items into a deterministic winner, verifying the complete video-ID union, then deleting losers safely.

**Architecture:** Fetch all current playlist summaries before duplicate processing. Group playlists by trimmed exact title using `StringComparer.OrdinalIgnoreCase`. Keep the playlist with the largest reported count; break ties by oldest `LastUpdated`. Transfer missing live video IDs through `playlistItems.insert`, verify the winner contains every transferable source video, delete each loser only after verification, archive local loser state after deletion, then process and sort affected winners through the existing pipeline.

**Tech Stack:** .NET 11 preview, Google.Apis.YouTube.v3, ErrorOr, Serilog through `Core.Telemetry`, Spectre.Console.Cli.

## Global Constraints

- Automatic duplicate consolidation runs on every `sync youtube` execution.
- Duplicate identity is `Title.Trim()` compared with `StringComparer.OrdinalIgnoreCase`.
- Winner selection is descending `ReportedVideoCount`, then ascending `LastUpdated`, then ascending `PlaylistId` for a fully deterministic final tie-break.
- Live YouTube items are the source of truth. Local processed JSON never authorizes deletion.
- Transfer uses `playlistItems.insert` with `part=snippet`, `snippet.playlistId`, `snippet.resourceId.kind="youtube#video"`, and `snippet.resourceId.videoId`.
- `playlistItems.insert` is non-idempotent and costs 50 quota units; re-list the winner before each run and deduplicate by `videoId`.
- Delete a loser only after exact video-ID union verification succeeds for every transferable item in that loser.
- Invalid or missing source `videoId`, failed insertion, failed verification, or over-cap transfer blocks that loser’s deletion.
- Per-group insertion cap is configured by `YOUTUBE_MERGE_INSERT_CAP`; default is `100` missing videos.
- Failed or over-cap groups remain retryable. Do not archive local files for a loser that was not deleted.
- Archive local loser state using playlist ID in the archive filename to avoid sanitized-title collisions.
- Move method names, item IDs, timing, and API diagnostics to Debug. Info remains concise and user-facing.
- Suppress already-sorted playlist Info logs. Emit Info only for actual repositioning and successful duplicate mutations; use Warn/Error for deferred or failed merges.
- Preserve existing ErrorOr railway style, cancellation propagation, one class per file, PascalCase JSON, and no inline comments.
- Do not add test NuGet packages. Repository has no test project; use focused build checks, static pure-policy verification, and controlled live API verification.
- Run `dotnet build` after every implementation task and before any live API execution.
- Do not commit, push, or execute destructive live deletion during plan creation.

## Current State and Gaps

| Component         | Current state                                                            | Desired state                                                 | Gap                                             |
| ----------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------- | ----------------------------------------------- |
| Duplicate scope   | `MergeDuplicatePlaylistsAsync` receives only new/changed playlists       | Scan all current summaries every sync                         | Unchanged duplicates survive indefinitely       |
| Duplicate key     | `Text.SanitizeFileName(p.Title)`                                         | Trimmed exact title, ordinal case-insensitive                 | Filename sanitization can merge distinct titles |
| Winner            | Largest count; API enumeration decides ties                              | Largest count, oldest timestamp, stable ID tie-break          | Nondeterministic deletion                       |
| Content merge     | Local processed JSON only                                                | Live API item transfer and exact ID verification              | Source-only videos can be lost before deletion  |
| Deletion          | `playlists.delete` after local merge                                     | Delete only after transfer and verification                   | Destructive ordering unsafe                     |
| Failure archive   | Archives raw state even when delete fails                                | Archive only after successful delete                          | State can falsely imply deletion                |
| Insert protection | No cap                                                                   | Configurable cap, default 100                                 | Large groups can exhaust quota                  |
| Sort Info         | Includes method name, item count, milliseconds; logs already-sorted Info | Concise mutation Info; detailed Debug; no already-sorted Info | Default logs too noisy                          |

## Dependency and Subagent Order

| Task                                         | Depends on | Domain            | Subagent category  | Review gate                                     |
| -------------------------------------------- | ---------- | ----------------- | ------------------ | ----------------------------------------------- |
| 1. Playlist-item insert API                  | None       | C# API wrapper    | `quick`            | Build, API shape review                         |
| 2. Pure duplicate policy and merge planner   | None       | C# logic          | `ultrabrain`       | Manual policy verification, build, logic review |
| 3. Live merger and archive safety            | 1, 2       | API orchestration | `deep`             | Build, failure-path review                      |
| 4. Orchestrator/state integration            | 3          | Cross-file C#     | `unspecified-high` | Build, reference scan, state-flow review        |
| 5. Sort and duplicate logging                | None       | C# logging        | `quick`            | Build, output-template review                   |
| 6. Full verification and controlled live run | 1-5        | QA/operations     | `deep`             | Build, diagnostics, API evidence                |

Tasks 1, 2, and 5 are logically independent, but implementation agents must run sequentially in the current session because each task receives a separate review gate and no overlapping writes are allowed. Tasks 3 and 4 follow the critical path.

## Subagent-Driven Execution Protocol

For each task:

1. Record `BASE=$(git rev-parse HEAD)` before dispatch.
2. Generate a task brief containing only that task’s requirements.
3. Dispatch one fresh implementer with the task brief, exact files, constraints, and report path.
4. Implementer writes code, runs required verification, self-reviews, and reports status.
5. Inspect the diff and dispatch a separate task reviewer for spec compliance and code quality.
6. If reviewer finds Critical/Important issues, resume implementer for fix rounds 1-3; use a fresh stronger implementer for rounds 4-5. Re-review every fix.
7. Record task completion and commit range in the SDD ledger before the next task.
8. Never fix reviewer findings in the controller session.

Use `using-git-worktrees` before implementation. Keep a plan-specific ledger under `.superpowers/sdd/<plan-basename>/progress.md`. Do not run multiple implementation agents against overlapping files.

## Task 1: Add Live Playlist-Item Insert API

**Files:**

- Modify: `src/Services/Google/YouTube/YouTubePlaylistService.cs`

**Interface produced:**

```csharp
Task<ErrorOr<string>> InsertPlaylistItemAsync(
    string playlistId,
    string videoId,
    CancellationToken ct
)
```

**Implementation steps:**

- [ ] Add `InsertPlaylistItemAsync` after existing playlist mutation methods.
- [ ] Construct `PlaylistItem` with `PlaylistItemSnippet.PlaylistId`, `ResourceId.Kind = "youtube#video"`, and `ResourceId.VideoId`.
- [ ] Call `yt.PlaylistItems.Insert(item, "snippet").ExecuteAsync(ct)`.
- [ ] Return inserted playlist-item ID through `ErrorOr<string>`.
- [ ] Follow existing `Telemetry.ForService`, `StartActivity`, cancellation, and `Errors.YouTube.ApiError` patterns.
- [ ] Keep request/response details at Debug; do not add Info noise per inserted item.
- [ ] Run `dotnet build` and `lsp_diagnostics` on the changed file.
- [ ] Commit: `feat(youtube): add playlist item insert API`.

**Reviewer checks:** request body uses video ID, not playlist-item ID; cancellation reaches API; failure returns ErrorOr; no deletion behavior is introduced.

## Task 2: Add Pure Duplicate Policy and Transfer Planning

**Files:**

- Create: `src/Services/Google/YouTube/YouTubeDuplicateMergePolicy.cs`

**Interfaces produced:**

```csharp
public static IReadOnlyList<DuplicatePlaylistGroup> FindGroups(
    IReadOnlyList<PlaylistSnapshot> playlists
)

public static PlaylistSnapshot SelectWinner(
    IReadOnlyList<PlaylistSnapshot> group
)

public static TransferCandidateSet GetTransferCandidates(
    IReadOnlySet<string> winnerVideoIds,
    IReadOnlyList<PlaylistItem> loserItems
)

public static bool ContainsAll(
    IReadOnlySet<string> winnerVideoIds,
    IReadOnlySet<string> sourceVideoIds
)
```

`DuplicatePlaylistGroup` is `record struct DuplicatePlaylistGroup(string Key, IReadOnlyList<PlaylistSnapshot> Playlists)`. `TransferCandidateSet` is `record struct TransferCandidateSet(IReadOnlyList<string> MissingVideoIds, bool HasInvalidItems)`.

**Policy steps:**

- [ ] Group by `playlist.Title.Trim()` using `StringComparer.OrdinalIgnoreCase`.
- [ ] Ignore singleton groups.
- [ ] Select winner by `ReportedVideoCount` descending, `LastUpdated` ascending, `PlaylistId` ascending.
- [ ] Extract only non-empty `Snippet.ResourceId.VideoId` values. Preserve source order, remove duplicates, and never insert a video already present in winner.
- [ ] Return `HasInvalidItems = true` when any source item has no usable video ID. Caller must block deletion for that source; it must not silently discard the item.
- [ ] Make verification set-based, not count-only. `winnerVideoIds` must contain every transferable source ID; count is only supplementary telemetry.
- [ ] Keep policy deterministic and side-effect free so it can be checked without Google credentials.
- [ ] Write a temporary standalone harness at `.superpowers/sdd/<plan-basename>/YouTubeDuplicateMergePolicyVerification.cs` with `Main()` and a `#:project` reference to `src/Services/Google/Google.csproj`. Run `dotnet run --file .superpowers/sdd/<plan-basename>/YouTubeDuplicateMergePolicyVerification.cs`; verify no duplicates, case/whitespace duplicate, punctuation-distinct titles, largest winner, oldest equal-count winner, duplicate source IDs, empty IDs, and cap boundary values. Delete harness after the task review.
- [ ] Run `dotnet build` and `lsp_diagnostics`.
- [ ] Commit: `feat(youtube): add deterministic duplicate merge policy`.

**Reviewer checks:** sanitized filenames are absent from identity logic; equal-size selection is stable; missing IDs cannot silently authorize deletion; policy does not call APIs or mutate files.

## Task 3: Implement Live Merger and Safe Archive Ordering

**Files:**

- Create: `src/Services/Google/YouTube/YouTubeDuplicateMerger.cs`

**Interface produced:**

```csharp
Task<DuplicateMergeOutcome> MergeDuplicateGroupsAsync(
    IReadOnlyList<PlaylistSnapshot> allCurrentPlaylists,
    CancellationToken ct
)
```

`DuplicateMergeOutcome` is:

```csharp
public readonly record struct DuplicateMergeOutcome(
    IReadOnlyList<PlaylistSnapshot> Survivors,
    IReadOnlyList<PlaylistSnapshot> RemovedLosers,
    IReadOnlySet<string> WinnersRequiringProcessing,
    int GroupsProcessed,
    int GroupsDeferred
)
```

The merger owns duplicate-delete archive creation. `YouTubeSyncProcessor` keeps only ordinary deleted-playlist archival.

**Implementation steps:**

- [ ] Read `YOUTUBE_MERGE_INSERT_CAP`; use `100` when absent, invalid, or non-positive; log invalid configuration at Warn.
- [ ] Process groups serially to avoid concurrent mutations and quota spikes.
- [ ] For each group, list complete winner items and complete loser items through the existing paginated `GetPlaylistItemsAsync` method.
- [ ] Build target video-ID set. For each loser, reject deletion eligibility if any item lacks a video ID.
- [ ] Build the complete missing-ID list across all losers before inserting anything. If missing count exceeds cap, log Warn and leave every playlist intact.
- [ ] Insert missing IDs through `InsertPlaylistItemAsync`, one at a time, with cancellation checks. If any insert fails, stop the group and delete nothing.
- [ ] Re-list winner after inserts. Verify every transferable source ID is present. Do not rely solely on `ReportedVideoCount` because item-count metadata can lag.
- [ ] Persist a deletion archive manifest containing winner ID, loser ID, source item IDs/video IDs, transfer counts, and timestamp before deletion. Use playlist ID in archive paths.
- [ ] Delete losers only after union verification. Because YouTube has no transaction, delete sequentially and record any loser delete failure without deleting that loser’s local archive.
- [ ] Archive loser processed/raw files only after the corresponding `playlists.delete` succeeds.
- [ ] If one loser deletion fails after another succeeds, report partial group completion; remaining loser stays retryable and winner remains authoritative.
- [ ] Return winner IDs needing reprocessing when inserts occurred. Return no winner-processing requirement when loser content was already a verified subset.
- [ ] Never call old local-JSON merge logic as a substitute for live transfer.
- [ ] Run build and diagnostics on every changed file.
- [ ] Commit: `feat(youtube): merge duplicate playlists through live API`.

**Reviewer checks:** no source deletion before exact union verification; over-cap path performs zero inserts; partial insert path performs zero deletes; archive occurs after delete only; rerun after partial inserts skips already-present IDs.

## Task 4: Wire All-Playlist Consolidation and State Flow

**Files:**

- Modify: `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs`
- Modify: `src/Services/Google/YouTube/YouTubeSyncProcessor.cs`
- Modify: `src/Services/Google/GoogleSetup.cs`

**Implementation steps:**

- [ ] Register `YouTubeDuplicateMerger` in the existing Google service setup.
- [ ] Extend `SyncContext` so merge stage can access all current summaries, not only `CombineNewAndChanged` output.
- [ ] Invoke duplicate consolidation for all current playlists after summaries are fetched and before normal processing.
- [ ] Remove deleted loser IDs from stored manifest snapshots and all change lists before `ProcessIfNeededAsync` and `Finalize` calculate counts.
- [ ] Add affected winner IDs to processing when live inserts changed winner contents, including winners previously classified unchanged.
- [ ] Refresh winner snapshot after merge where needed; do not leave stale `ReportedVideoCount` or `ETag` in state.
- [ ] Preserve normal new/changed processing for non-duplicate playlists.
- [ ] Remove obsolete `MergeDuplicatePlaylistsAsync`, `MergeProcessedVideosAsync`, and any local-only duplicate merge path after the new merger is wired.
- [ ] Keep archive behavior for ordinary YouTube-deleted playlists separate from duplicate-delete archives.
- [ ] Run `dotnet build`, `lsp_diagnostics`, and a workspace search confirming removed methods have no callers.
- [ ] Commit: `feat(youtube): run duplicate consolidation across all playlists`.

**Reviewer checks:** unchanged duplicate groups are detected; deleted losers do not remain in manifest or final counters; merged winners are processed; no duplicate group can be processed twice in one sync due stale context.

## Task 5: Refactor Sort and Duplicate Logging

**Files:**

- Modify: `src/Services/Google/YouTube/YouTubeSortService.cs`

**Implementation steps:**

- [ ] Move existing already-sorted summary from Info to Debug, retaining item count and elapsed milliseconds there.
- [ ] Change repositioning Info to omit `YouTube.SortPlaylist`, method wording, and milliseconds. Use a concise template equivalent to `{PlaylistName} — {Repositioned}/{ItemCount} repositioned`.
- [ ] Keep pass timings, method names, item IDs, and API timings at Debug/Verbose.
- [ ] Emit duplicate detection and successful deletion summaries at Info without method names or timing.
- [ ] Emit deferred cap groups at Warn; failed transfer/verification/delete paths at Error or Warn according to existing Telemetry conventions.
- [ ] Run build and diagnostics.
- [ ] Commit: `refactor(youtube): reduce default playlist logging noise`.

**Reviewer checks:** no already-sorted Info output; mutation Info contains playlist/user outcome only; detailed diagnostics remain available at Debug; no sorting behavior changes.

## Task 6: Full Verification and Controlled Live Run

**Files:** None for verification; do not alter state until preflight is captured.

**Implementation steps:**

- [ ] Run `dotnet build` on the full solution. Require exit code 0 and no warnings/errors.
- [ ] Run `lsp_diagnostics` on every changed C# file.
- [ ] Inspect `git diff`, `git status`, and recent commits. Confirm only planned files changed.
- [ ] Capture a preflight export of duplicate candidate playlist summaries and live item video IDs before the first destructive run.
- [ ] Set `YOUTUBE_MERGE_INSERT_CAP=100` explicitly for first live run.
- [ ] Run `dotnet run --project src\App -- sync youtube` once.
- [ ] Verify logs show duplicate detection, transfer counts, exact verification, deletion only after verification, concise sort Info, and no already-sorted Info.
- [ ] Verify `state/youtube/manifest.json`: loser IDs absent, winner ID present with refreshed count/ETag.
- [ ] Verify `state/youtube/deleted/`: loser archive manifest and local files exist only for successfully deleted losers.
- [ ] Verify live YouTube: winner contains the union of all preflight source/winner video IDs; loser no longer exists; no duplicate video IDs were introduced.
- [ ] Run sync a second time. Expected: no repeat inserts/deletes for successfully consolidated groups; deferred/failed groups retry with existing target IDs skipped.
- [ ] Exercise cap behavior using a known group requiring more than 100 inserts: expect Warn, zero deletion, loser remains live.
- [ ] Exercise failure behavior only with a controlled invalid/non-transferable source item if available: expect no deletion and retryable state.
- [ ] Do not claim completion until all evidence is recorded in the SDD ledger.

**Live-run rollback reality:** YouTube playlist deletion is not transactional. Local archives preserve IDs and metadata for manual recreation, but cannot restore a deleted playlist automatically without additional API inserts. Do not run live deletion against production duplicates until the preflight export is complete.

## Commit and Review Strategy

| Commit | Scope                 | Reviewer focus                                              |
| ------ | --------------------- | ----------------------------------------------------------- |
| 1      | Playlist insert API   | Request shape, ErrorOr, cancellation                        |
| 2      | Pure duplicate policy | Identity, deterministic winner, ID-set correctness          |
| 3      | Live merger           | Cap, partial failures, verification-before-delete, archives |
| 4      | Orchestrator/state    | All-playlist scope, survivor state, affected winners        |
| 5      | Logging               | Info/Debug separation, no behavior regression               |

Every commit receives a task-scoped review package. After all tasks, dispatch one broad whole-branch reviewer against the merge base. Any final Critical/Important finding gets one fix subagent and one scoped re-review; residual load-bearing findings block handoff.

## Success Criteria

- All current playlists scanned every sync.
- Only trimmed exact case-insensitive title matches become duplicate groups.
- Largest playlist survives; equal-size ties keep oldest playlist.
- Live missing items transfer through YouTube API.
- Exact source video-ID union verified before any loser deletion.
- Over-cap and failed groups remain intact and retryable.
- Local archives created only after successful deletion.
- Manifest and processing pipeline reflect survivors and affected winners.
- Default Info logs contain no method names, timing, or already-sorted lines.
- `dotnet build` exits 0 with zero warnings/errors.
- Second sync is idempotent for successfully merged groups.
