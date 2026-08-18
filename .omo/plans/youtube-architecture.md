# YouTube Architecture — Live Spec

**Status:** active reference (deduplicated, replaces `youtube-architecture-spec.md` + `toolbox-consolidated-spec.md` §3/§9 bucket excerpts)
**Verified:** 2026-08-18 | Live: `src/Services/Google/YouTube` (12 files, ~2678 LOC) + `src/Services/Google/GoogleSetup.cs`

## Files (12)

| File | LOC | Role |
|------|-----|------|
| `YouTubePlaylistOrchestrator.cs` | ~363 | Top-level sync: fetch → detect → merge → process → sort; 4 `Execute*` → collapse to 2 |
| `YouTubeSyncProcessor.cs` | ~332 | Batch `ProcessIfNeededAsync`, `SortPlaylistsAsync` (150-write budget), `Finalize`, archive |
| `YouTubeSortService.cs` | ~369 | LIS sort, `ExecuteSortPlanAsync` with `remainingBudget`, `IsQuotaOrRateLimit` |
| `YouTubePlaylistService.cs` | ~299 | YouTube API: `PlaylistItems.Insert`/`Delete`/`List`, `PaginateAsync`, ETag |
| `YouTubeDuplicateMerger.cs` | ~386 | Live winner/loser merge, `YOUTUBE_MERGE_INSERT_CAP=100`, verification-before-delete |
| `YouTubeDuplicateMergePolicy.cs` | ~54 | Pure policy: grouping by `Title.Trim()` ordinal, winner select |
| `YouTubeFetchState.cs` | ~115 | `YouTubeFetchState` + `PlaylistSnapshot` (incl. `LastSortMoves/Attempted/Completed`), `LoadAsync`/`SaveAsync` `state/youtube/manifest.json` |
| `YouTubeChangeDetector.cs` | ~51 | `DetectChanges` diff stored vs live (62 LOC in old audit → now 51) |
| `YouTubePlaylistProcessor.cs` | ~313 | Per-playlist: fetch videos, translate, save `state/youtube/{processed,raw}/` |
| `YouTubeTranslationService.cs` | ~231 | Azure `TranslateService` batching |
| `YouTubeVideoService.cs` | ~71 | Video details |
| `DashboardService.cs` | ~94 | Reads `state/youtube/`, builds dashboard model |

`GoogleSetup.cs` (above YouTube): `extension AddGoogleServicesAsync()` — async OAuth2, registers YouTube stack + `DashboardService`.

## Features Preserved (F1–F10)

All keep — 0 features proposed for removal. Deletes/shrinks preserve:

F1 state cache (`YouTubeFetchState`), F2 change detection (inline file but keep logic), F3 bulk sync (`Orchestrator`+`SyncProcessor`), F4 single-playlist by title (fix layer bypass), F5 duplicate merge (`DuplicateMerger`+`Policy`), F6 LIS sort, F7 resume incomplete sort (`ExecuteWithSortAsync`), F8 translate batch, F9 archive+incremental save, F10 `state/logs/youtube.jsonl` logging.

## Buckets (YouTube-Only)

**A — DEAD (0 feature loss):** `PlaylistNotFound/VideoNotFound` factories; `SyncResult.UpdatedSnapshots`; `SyncOutcome.IdsWithNewVideos`; `DuplicateMergeOutcome.GroupsProcessed/Deferred`; `ProcessResult.ShouldBreak`; `CombineNewAndChanged` inline; `DashboardService` DI singleton; `FetchState.ArchiveDeleted` duplicate.

**B — DUPE (one source):** `StateRoot`/`Manifest` path×5 → `PathResolver` constant; `dict-filter Where(!ids).ToDictionary ×2` → `WithoutIds`; `Delete`/`Insert` `ApiError` copy-paste → typed `GoogleApiException` mapper.

**C — YAGNI file (inline):** `YouTubeChangeDetector` 51 LOC → inline unless tested; 4 `Execute*` → 2 methods with `SyncOptions`; `ProcessResult.ShouldBreak` → `ErrorOr`.

**D — LAYER MISPLACE:** title path bypasses `SyncProcessor` → delegate to `SyncProcessor`.

**E — KEEP (legit SRP):** `SortService` LIS, `Merger`+`Policy`, `PaginateAsync`, processor checkpoint, translation batching.

## Points & Overengineering (from old arch spec)

- P-01 full spec 12 files not 13 — corrected.
- P-02/`YT.RateLimit` taxonomy — see `error-taxonomy.md`.
- P-03 wrappers/dupe — see `overengineering-verdict.md`.
- P-06/P-07 `4 Execute*` sprawl — solo-dev: 1 interface with `SyncOptions`.
- `ChangeDetector` (1 caller), `ProcessResult.ShouldBreak` — inline unless test seam.

## State Paths (Ground Truth)

```
state/youtube/manifest.json          # YouTubeFetchState (115 LOC)
state/youtube/processed/*.json       # 145 tracked
state/youtube/raw/*.json             # 145 tracked
state/youtube/deleted/*.json         # 3
state/youtube/merge-manifests/*.json # 1
state/logs/youtube.jsonl             # per-service log (Spec fix: PathResolver.RepoRoot/state/logs)
```
