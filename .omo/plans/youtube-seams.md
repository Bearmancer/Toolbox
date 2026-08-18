# YouTube Data Seams — Produced ↔ Consumed Gap Map

**Source:** `toolbox-consolidated-spec.md` §1 (N-01..N-05, S-09..S-17, S-01..S-08)
**Verified:** 2026-08-18 | Live: `src/Services/Google/YouTube/*` (12 files, ~2678 LOC) + `src/Core/Errors.cs` + `src/Services/Google/YouTube/YouTubeFetchState.cs`

## Sort/Plan Seams (N-01..N-05)

| ID | Data | Verdict | Evidence |
|----|------|---------|----------|
| N-01 | `SortResult.LastSortCompleted` | consumed | `SortService` → `SyncProcessor` resume |
| N-02 | `SortPlan TotalItems/LisSize` | unconsumed | produced in `ComputeSortPlan`, only logged |
| N-03 | `SortPassResult Failures` | logical-error | collapsed to `ApiError` string, count lost |
| N-04 | `PlaylistUpdate Item+NewPosition` | consumed | `SortService:219→:268` |
| N-05 | `Translation DetectedLanguage hi` | logical-error | transliterated `hi` counts as translated `en` — over-count |

## Dashboard/Sort/Video/Merge Seams (S-09..S-17)

| ID | Data | Verdict | Evidence |
|----|------|---------|----------|
| S-09 | `SyncResult.SkippedVideos` | unconsumed | logged in Finalize, never read by CLI |
| S-10 | `ChangeDetectionResult.UnchangedPlaylists` | unconsumed | never iterated outside detector |
| S-11 | `SortStatistics Attempted/Modified` | logical-error | logged then discarded, CLI never sees |
| S-12 | `SyncResult.TotalVideos vs ProcessedIds.Count` | dupe | keep one metric |
| S-13 | `DashboardService.reverseLookup TryAdd` | logical-error | sanitized Title collision drops data — key by `PlaylistId` |
| S-14 | `YouTubeVideo.Description` | consumed | display off, search on, ~1.5 MB bloat — keep for search |
| S-15 | `YouTubeFetchState.LastChecked/LastUpdated` top-level | unconsumed | written 5 places, never read — dead or missing throttle |
| S-16 | `PlaylistSnapshot.LastChecked` | unconsumed | never read (ETag+count gates cache) |
| S-17 | `HmsTimeSpanConverter` scope | unused | Merger isolated `JsonOptions` — reuse `YouTubeFetchState.JsonOptions` |

## Fresh Unconsumed Seams (S-01..S-08) — dupes flagged

| ID | Data | Verdict |
|----|------|---------|
| S-01 | `TranslatedTitle / DetectedLanguage` | unconsumed — dashboard drops language |
| S-02 | `YouTubeVideo.Description` | duplicate S-14 |
| S-03 | `YouTubeVideo.Duration` | overengineering — stored hh:mm:ss, reformatted same string |
| S-04 | `PlaylistSnapshot.ReportedVideoCount` | consumed — detector logs delta |
| S-05 | `SortStatistics` | duplicate S-11 |
| S-06 | `SyncResult TotalVideos/SkippedVideos` | duplicate S-09/S-12 |
| S-07 | `ChangeDetectionResult UnchangedPlaylists` | duplicate S-10 |
| S-08 | `DashboardData PlaylistCount/VideoCount` | unconsumed — scaffolding dead |

## Notes

- Live YouTube count is 12 files (not 13): `DashboardService`, `YouTubeChangeDetector`, `YouTubeDuplicateMergePolicy`, `YouTubeDuplicateMerger`, `YouTubeFetchState`, `YouTubePlaylistOrchestrator`, `YouTubePlaylistProcessor`, `YouTubePlaylistService`, `YouTubeSortService`, `YouTubeSyncProcessor`, `YouTubeTranslationService`, `YouTubeVideoService` (+ `GoogleSetup.cs` above).
- `YouTubeFetchState` now has `LastSortMoves/LastSortAttempted/LastSortCompleted` — seams S-15/S-16 pre-date churn fix.
