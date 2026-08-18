# Google Services

YouTube Data API v3 sync pipeline. Depends on `Services.Azure.TranslateService`.

## STRUCTURE

```
Google/
├── GoogleSetup.cs                       # DI: extension AddGoogleServicesAsync(), OAuth2 (FileDataStore state/google-auth, scope Youtube)
└── YouTube/                             # 12 files
    ├── YouTubePlaylistOrchestrator.cs   # Top-level sync: fetch → detect → merge → process → sort (ExecuteAsync/ExecuteWithSortAsync)
    ├── YouTubePlaylistProcessor.cs      # Per-playlist: fetch videos, translate, save processed/raw JSON
    ├── YouTubePlaylistService.cs        # YouTube API: list playlists, get items, insert/delete
    ├── YouTubeVideoService.cs           # YouTube API: video details (batch fetch)
    ├── YouTubeSortService.cs            # Sort by translated title: LIS + budgeted moves (IsQuotaOrRateLimit)
    ├── YouTubeTranslationService.cs     # Translates titles via Azure TranslateService
    ├── YouTubeChangeDetector.cs         # Diff stored vs current: DetectChanges() → New/Changed/Deleted
    ├── YouTubeFetchState.cs             # Manifest state/youtube/manifest.json; PlaylistSnapshot + LastSortMoves/Attempted/Completed
    ├── YouTubeSyncProcessor.cs          # Batch orchestration: ProcessPlaylistsAsync, SortPlaylistsAsync (maxWritesPerRun 150), ArchiveDeletedPlaylists
    ├── DashboardService.cs              # Reads state/youtube/, builds dashboard model
    ├── YouTubeDuplicateMergePolicy.cs   # Policy: FindGroups (title-normalized), SelectWinner, GetTransferCandidates
    └── YouTubeDuplicateMerger.cs        # Exec merges: insert winners, delete losers, archive merge-manifests
```

## WHERE TO LOOK

| Task                      | File                                                    | Notes                                                        |
| ------------------------- | ------------------------------------------------------- | ------------------------------------------------------------ |
| Change sync flow          | `YouTubePlaylistOrchestrator.cs`                        | `ExecuteCoreAsync()` ThenAsync chain → Merge → Process → Sort |
| Change per-playlist logic | `YouTubePlaylistProcessor.cs`                           | `ProcessPlaylistAsync()` + `RefreshLocalStateAsync()`        |
| Add YouTube API call      | `YouTubePlaylistService.cs` / `YouTubeVideoService.cs` | Wrap response; handle quota via `IsQuotaOrRateLimit`        |
| Change translation        | `YouTubeTranslationService.cs`                          | Calls `TranslateService.TranslateBatchAsync()`               |
| Change state schema       | `YouTubeFetchState.cs`                                  | `PlaylistSnapshot`, `LoadAsync`/`SaveAsync`, sort fields     |
| Build dashboard data      | `DashboardService.cs`                                   | Reads `state/youtube/`, returns dashboard model              |

## CONVENTIONS

- **Orchestrator pattern:** `YouTubePlaylistOrchestrator` owns pipeline. Thin CLI delegates to `ExecuteAsync`/`ExecuteWithSortAsync`.
- **ErrorOr chain:** `LoadAsync().ThenAsync(Fetch+Detect).ThenAsync(Merge).ThenAsync(Process).Then(Finalize)` — breaks on first error.
- **State persistence:** `YouTubeFetchState.LoadAsync`/`SaveAsync` JSON to `state/youtube/manifest.json` (HmsTimeSpanConverter, ArchiveDeleted).
- **Change detection:** `YouTubeChangeDetector.DetectChanges(current, stored)` → New/Changed/Deleted; deleted archived to `state/youtube/deleted/`.
- **Cross-service:** `YouTubeTranslationService` → `Services.Azure.TranslateService` directly (no CLI indirection).
- **Quota handling:** `YouTubeSyncProcessor` caps `maxWritesPerRun=150` (50 units/write); `YouTubeSortService.IsQuotaOrRateLimit` checks 403/429 + "quota" string; budget passed per-pass, stops on exhaustion.

## ANTI-PATTERNS

- **NEVER** put sync logic in CLI. Orchestrator owns everything.
- **NEVER** bypass Orchestrator to call `YouTubePlaylistService` from CLI.
