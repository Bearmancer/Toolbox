# Dead Code Catalog — Unconsumed vs Unused vs Logical-Error

**Source:** `toolbox-consolidated-spec.md` §8 (18+5+5 symbols)
**Verified:** 2026-08-18 | Live: `src/Core/Errors.cs`, `src/Core/Text.cs`, `src/Services/Google/YouTube/*`, `src/Services/Audio/*`, `src/CLI/**`, `Directory.Packages.props`

## Unconsumed (no code path reads it)

| # | Symbol | Location | Evidence |
|---|--------|----------|----------|
| 1 | `Errors.PlaylistNotFound/VideoNotFound` | `Core/Errors.cs` | 0 producers, 0 consumers — see `error-taxonomy.md` |
| 2 | `Errors.General.Unexpected/Internal` | `Core/Errors.cs:9` | 0 callers |
| 3 | `Errors.Validation.RequiredField` | `Core/Errors.cs:21` | 0 callers |
| 4 | `Errors.Azure.ServiceUnavailable` | `Core/Errors.cs:50` | 0+0 |
| 5 | `Text.Has/StartsWith` | `Core/Text.cs` | 0 callers |
| 6 | `SyncResult.UpdatedSnapshots` | `YouTubeSyncProcessor.cs:326` | populated → never read |
| 7 | `SyncOutcome.IdsWithNewVideos` | `YouTubePlaylistOrchestrator.cs:396` | computed → discarded |
| 8 | `DuplicateMergeOutcome.GroupsProcessed/Deferred` | `YouTubeDuplicateMerger.cs:14` | logged → discarded |
| 9 | `PathValidator.ValidateOutputDirectory` | `Services/Audio/PathValidator.cs:18` | 0 callers |
| 10 | `SacdProbeService` | `Services/Audio/SacdProbeService.cs:3` | pure delegation, 0 pipeline callers |
| 11 | `DashboardService DI singleton` | `GoogleSetup.cs:69` | all methods static — registration dead |
| 12 | `TranslateCommand --from` | `CLI/Azure/TranslateCommand.cs:59` | registered → ignored |
| 13 | `SyncResult.SkippedVideos` | `SyncProcessor→Orchestrator` | logged → never read |
| 14 | `ChangeDetectionResult.UnchangedPlaylists` | `ChangeDetector→Orchestrator` | never iterated |
| 15 | `PlaylistSnapshot.LastChecked` | `PlaylistService:203` | written → never read |
| 16 | `YouTubeFetchState.LastChecked/LastUpdated` top-level | `YouTubeFetchState:13` | written 5 places → never read |
| 17 | `DashboardData PlaylistCount/VideoCount` | `DashboardDataBuilder:20` | scaffolding dead |
| 18 | `ArchiveDeleted duplicate` | `YouTubeFetchState` vs `YouTubeSyncProcessor` | duplicate path |

## Unused (code path never triggers)

| # | Symbol | Location | Evidence |
|---|--------|----------|----------|
| 1 | `SSH.NET` in 5 non-CLI projects | `Core/Azure/Audio/Google/LastFm .csproj` | 0 `Renci` usage outside CLI — leave if OCI `OciDashboardDeployer` needs it; verify first |
| 2 | `SacdConvertCommand 24/both format` | `CLI/Audio/SacdConvertCommand.cs:18` | advertised → validation rejects |
| 3 | `ProcessResult.ShouldBreak=false` | `YouTubeSyncProcessor.cs:335` | always true on error |
| 4 | `HmsTimeSpanConverter` in Merger isolated `JsonOptions` | `YouTubeDuplicateMerger.cs` | 0 `TimeSpan` fields in manifest — reuse `YouTubeFetchState.JsonOptions` |

## Consumed but Logical-Error (fix, don't delete)

| # | Symbol | Location | Bug |
|---|--------|----------|-----|
| 1 | `YT.RateLimit` | `Errors.cs:27→SyncProcessor:79` | consumed, never produced — producers emit `ApiError` |
| 2 | `YT.QuotaExceeded` | `SortService:312` | produced, consumer never checks |
| 3 | `Azure.AuthFailed/RateLimit` | `SyncProcessor:88` | consumed, `TranslateService` emits generic |
| 4 | `S-13 reverseLookup` | `DashboardService:74` | sanitized title collision drops data |
| 5 | `N-05 transliterate` | `TranslationService:213` | `hi` transliterated counts as translated `en` |

## Notes

- `Serilog.Sinks.Console` `PackageVersion` entry in `Directory.Packages.props:19` — 0 `PackageReference` consumers. Safe to drop.
- `state/logs/*.jsonl` — 8 of 10 currently 0 B in dev; `audio.jsonl` + `youtube.jsonl` active. Empty ≠ dead architecture, but confirms missing `ForService` callers or level drop.
