# YouTube Data Seams Spec — Caveman Ultra

## YouTube Seams Hunt (N-01 to N-05)

### Points Enumerated

N-01: SortResult.LastSortCompleted | consumed | produced YouTubeSortService:406. consumed YouTubeSyncProcessor:271. snapshot resume works. Repositioned vs DistinctItemsMoved dupe metric.
N-02: SortPlan TotalItems / LisSize | unconsumed | produced ComputeSortPlan. logged YouTubeSortService:225. dead outside. keep log, drop field.
N-03: SortPassResult Failures | logical-error | produced. logged. collapsed to ApiError. count lost. missing aggregate metric.
N-04: PlaylistUpdate Item + NewPosition | consumed | produced YouTubeSortService:219. consumed YouTubeSortService:268. no gap.
N-05: Translation MaxTextsPerCall / DetectedLanguage hi | logical-error | consumed. transliterate success counts as translated en. over-count bug.

## Dashboard/Sort/Video/Merge Seams (S-09 to S-17)

### Points Enumerated

S-09: SyncResult.SkippedVideos | unconsumed | logged Orchestrator Finalize. never read CLI. dead aggregate.
S-10: ChangeDetectionResult.UnchangedPlaylists | unconsumed | dead list. never read outside detector.
S-11: SortStatistics Attempted/Modified | logical-error | produced YouTubeSyncProcessor:208. logged. discarded Orchestrator. CLI never sees. missing output.
S-12: SyncResult.TotalVideos vs ProcessedIds.Count | unused | dupe metric. keep one.
S-13: DashboardService.reverseLookup TryAdd | logical-error | sanitized Title collides DashboardService:74. data drops. use PlaylistId key.
S-14: YouTubeVideo.Description | consumed | stored JSON. builder hides column. search uses it. keep for search. bloat risk.
S-15: YouTubeFetchState top-level LastChecked/LastUpdated | unconsumed | required top-level YouTubeFetchState:13. written 5 places. never read. missing throttle feature or dead.
S-16: PlaylistSnapshot.LastChecked | unconsumed | produced PlaylistService:203. never read. count+ETag gates cache. dead field.
S-17: HmsTimeSpanConverter scope | unused | YouTubeDuplicateMerger isolated JsonOptions. unnecessary scope. reuse FetchState.JsonOptions.

## Fresh Unconsumed Seams (S-01 to S-08)

### Points Enumerated

S-01: TranslatedTitle / DetectedLanguage | unconsumed | dashboard drops language. produced-not-surfaced.
S-02: YouTubeVideo.Description payload | consumed | display off. search on. 1.5MB dashboard bloat.
S-03: YouTubeVideo.Duration | overengineering | stored hh:mm:ss. reformatted same string. duplicate serialization. waste.
S-04: PlaylistSnapshot.ReportedVideoCount | consumed | detector logs delta. not dead.
S-05: SortStatistics Attempted/Modified | logical-error | duplicate S-11. logged not surfaced.
S-06: SyncResult TotalVideos/SkippedVideos | unconsumed | duplicate S-09/S-12. logged inside. not returned CLI. dead aggregate outside.
S-07: ChangeDetectionResult UnchangedPlaylists | unconsumed | duplicate S-10. dead 4th tuple.
S-08: DashboardData PlaylistCount / VideoCount | unconsumed | duplicate of lists. scaffolding dead payload.

## Deletion Reassessment (16 Candidates)

### Dead Code Catalog

DC-01: Errors.YouTube.PlaylistNotFound @ Errors.cs:31 | unconsumed | 0 producers. 0 consumers. delete.
DC-02: Errors.YouTube.VideoNotFound @ Errors.cs:33 | unconsumed | 0 producers. 0 consumers. delete.
DC-03: Errors.Azure.ServiceUnavailable @ Errors.cs:50 | unconsumed | 0 producers. 0 consumers. delete.
DC-04: Errors.General.Unexpected/Internal @ Errors.cs:9 | unconsumed | no YouTube usage. delete from scope.
DC-05: Errors.Validation.RequiredField @ Errors.cs:21 | unconsumed | no YouTube usage. delete from scope.
DC-06: SyncResult.UpdatedSnapshots @ YouTubeSyncProcessor.cs:326 | unconsumed | rebuilt dict. redundant field. delete.
DC-07: SyncOutcome.IdsWithNewVideos @ YouTubePlaylistOrchestrator.cs:396 | unconsumed | discarded outside Orchestrator. delete.
DC-08: DuplicateMergeOutcome.GroupsProcessed/Deferred @ YouTubeDuplicateMerger.cs:14 | unconsumed | logged inside. dead API outside. drop fields.
DC-09: DashboardService singleton @ GoogleSetup.cs | unconsumed | all static methods. dead DI registration. delete.
DC-10: ArchiveDeleted @ YouTubeFetchState.cs / YouTubeSyncProcessor.cs | unused | duplicate copy/paste owner. delete one.
DC-11: StateRoot @ DashboardService.cs / Orchestrator.cs / SyncProcessor.cs | unused | copy/paste duplicate constant x5. delete four.

### Overengineering Assessment

OE-01: ProcessResult.ShouldBreak | cut | always Break on error. return ErrorOr directly. solo-dev: bool abstraction vs native return = bad overhead.
OE-02: CombineNewAndChanged | cut | pure array concat. single caller. inline it.
OE-03: DetectChanges | cut | pure function. single caller. separation creates false abstraction. inline unless tested.
OE-04: HmsTimeSpanConverter scope | cut | Merger isolated options. drift risk. reuse central FetchState.JsonOptions.

### Features At Risk

FR-01: YouTube Sort Resume | YouTubeSortService.cs / YouTubeSyncProcessor.cs | N-01 LastSortCompleted consumed. quota limit protection. not dead.
FR-02: Dashboard Search Index | DashboardService.cs | S-14 Description. UI display off but search requires it. not dead.
FR-03: Translate Failover | YouTubeTranslationService.cs | Azure.RateLimit logical error missing mapper. framework needed. not dead.
FR-04: API Rate Limiting / Quota Tracking | Errors.cs / YouTubeSortService.cs | consumers exist but producers swallow generic. fix mappers, do not delete types.

### Cross-Reference Verification

CR-01: N-01 LastSortCompleted | YouTubeSortService.cs:406 | match. verified present and consumed.
CR-02: N-03 Failures | YouTubeSortService.cs:417 | match. tracked then squashed to generic ApiError.
CR-03: S-13 reverseLookup | DashboardService.cs:74 | match. SanitizeFileName collision drops titles.
CR-04: S-15 Top-level LastChecked | YouTubeFetchState.cs:13 | match. written 5 times. never read.
CR-05: Deletion Cand 1 YT.RateLimit | Errors.cs:27 | mismatch. consumed SyncProcessor:79. producer fails to emit. logical error not dead.
CR-06: Deletion Cand 2 PlaylistNotFound | Errors.cs:31 | match. 0 callers.
CR-07: Deletion Cand 10 GroupsProcessed | YouTubeDuplicateMerger.cs:14 | match. logged then ignored.
