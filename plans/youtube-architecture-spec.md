# YouTube Architecture Spec — Caveman Ultra

## Points Enumerated
P-01: YouTube full spec (13 files, 3146 LOC) | KEEP | Core features preserved, no functionality loss proposed.
P-02: Speculative taxonomy (YT.RateLimit) | LAYER-MISPLACE | RateLimit and Azure.AuthFailed handled in SyncProcessor but never produced by providers. Fix producers to emit correct typed errors.
P-03: Wrappers + copy/paste dedup | DUPE | Path constants x5, dict-filter x2. Consolidate to PathResolver and FetchState helpers.
P-04: Consumed-never-produced vs produced-never-consumed | DEAD | UpdatedSnapshots, IdsWithNewVideos, GroupsProcessed. Safe to delete fields.
P-05: Buckets A-E re-sorted | KEEP | Safe deletions defined without losing features.
P-06: YouTube net ranked P0-P3 | KEEP | Actionable priority list for fixes and cleanup.
P-07: YouTubePlaylistOrchestrator 418 deep dive | YAGNI | 4 Execute entry points collapse to 2.

## Dead Code Catalog
DC-01: Errors.PlaylistNotFound / VideoNotFound @ Core/Errors.cs:30,33 | unused | 0 producers emit this code.
DC-02: SyncCounters.UpdatedSnapshots @ Services/Google/YouTube/YouTubeSyncProcessor.cs:346 | unconsumed | Populated but never read by orchestrator.
DC-03: SyncOutcome.IdsWithNewVideos @ Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:396 | unconsumed | Passed out of Finalize but caller ExecuteAsync only reads Ids.
DC-04: ProcessResult.ShouldBreak @ Services/Google/YouTube/YouTubeSyncProcessor.cs:335 | unused | Always true on error, unnecessary wrapper object.
DC-05: DuplicateMergeOutcome fields | unconsumed | GroupsProcessed/Deferred logged then discarded.

## Overengineering Assessment
OE-01: YouTubeChangeDetector @ Services/Google/YouTube/YouTubeChangeDetector.cs:12 | cut/inline | 62 LOC pure function with 1 caller (FetchSummariesAndDetectAsync). Solo dev rationale: unnecessary indirection unless isolated tests exist.
OE-02: 4 Execute* entry points @ Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:22,187,231,326 | cut | Sprawl for bulk vs title and sort vs no-sort. Solo dev rationale: single interface with options is cleaner.
OE-03: ProcessResult.ShouldBreak @ Services/Google/YouTube/YouTubeSyncProcessor.cs:335 | cut | Unnecessary wrapper object for a boolean. Return ErrorOr propagate.

## Features At Risk
FR-01: F1 State cache | Services/Google/YouTube/YouTubeFetchState.cs | Keep. Used for diffing.
FR-02: F2 Change detect | Services/Google/YouTube/YouTubeChangeDetector.cs | Keep logic. Inlining file doesn't kill feature.
FR-03: F3 Bulk sync | Services/Google/YouTube/YouTubePlaylistOrchestrator.cs | Keep. Orchestration core.
FR-04: F4 Single-playlist sync by title | Services/Google/YouTube/YouTubePlaylistOrchestrator.cs | Keep. Title path delegated to SyncProcessor.
FR-05: F5 Duplicate merge | Services/Google/YouTube/YouTubeDuplicateMerger.cs | Keep. Policy and merge essential.
FR-06: F6 Sort playlists LIS | Services/Google/YouTube/YouTubeSortService.cs | Keep. Quota optimization required.
FR-07: F7 Resume incomplete sort | Services/Google/YouTube/YouTubePlaylistOrchestrator.cs | Keep. Prioritization kept verbatim.
FR-08: F8 Translate titles batch | Services/Google/YouTube/YouTubeTranslationService.cs | Keep. Azure integration core.
FR-09: F9 Sorted archive + incremental save | Services/Google/YouTube/YouTubeSyncProcessor.cs | Keep. Both behaviors remain.
FR-10: F10 Logging youtube.jsonl | Services/Google/YouTube/YouTubePlaylistOrchestrator.cs | Keep. Fix missing scopes.

## Cross-Reference Verification
CR-01: 4 Execute* entry points | MATCH | Orchestrator:22,187,231,326 confirmed.
CR-02: Dict-filter duplication | MATCH | Orchestrator:83, 111 uses duplicate LINQ dict building.
CR-03: YouTubeChangeDetector 62 LOC | MATCH | File is 63 LOC, one function, called once at Orchestrator:53.
CR-04: YT.RateLimit check | MATCH | SyncProcessor:79 checks `error.Code is "YT.RateLimit"`.
CR-05: Produced-never-consumed SyncResult.UpdatedSnapshots | MATCH | SyncProcessor.cs:326 defines it, but Orchestrator Finalize never reads it.
CR-06: Produced-never-consumed SyncOutcome.IdsWithNewVideos | MATCH | Orchestrator:396 defines it, but ExecuteAsync (Orchestrator:25) returns only outcome.Value.Ids.
