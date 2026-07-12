# Google Services

YouTube Data API v3 + orchestration. Depends on `Services.Azure.TranslateService`.

## STRUCTURE

```
Google/
├── GoogleSetup.cs                    # DI: extension AddGoogleServicesAsync(), OAuth2 flow
└── YouTube/
    ├── YouTubePlaylistOrchestrator.cs # Top-level sync: fetch → detect → process → sort
    ├── YouTubePlaylistProcessor.cs    # Per-playlist: fetch videos, translate, save
    ├── YouTubePlaylistService.cs      # YouTube API: list playlists, get items
    ├── YouTubeVideoService.cs         # YouTube API: video details
    ├── YouTubeSortService.cs          # Sort playlist items by title
    ├── YouTubeTranslationService.cs   # Translates titles via Azure TranslateService
    ├── YouTubeChangeDetector.cs       # Diff stored vs. current state
    ├── YouTubeFetchState.cs           # Manifest persistence (JSON)
    ── YouTubeSyncProcessor.cs        # Batch processing, sorting, archiving
```

## WHERE TO LOOK

| Task                      | File                                                    | Notes                                            |
|---------------------------|---------------------------------------------------------|--------------------------------------------------|
| Change sync flow          | `YouTubePlaylistOrchestrator.cs`                        | `ExecuteAsync()` → `ExecuteCoreAsync()` pipeline |
| Change per-playlist logic | `YouTubePlaylistProcessor.cs`                           | `ProcessPlaylistAsync()`                         |
| Add YouTube API call      | `YouTubePlaylistService.cs` or `YouTubeVideoService.cs` | Wrap API response in domain types                |
| Change translation        | `YouTubeTranslationService.cs`                          | Calls `TranslateService.TranslateBatchAsync()`   |
| Change state schema       | `YouTubeFetchState.cs`                                  | `PlaylistSnapshot`, `YouTubeFetchState` records  |
| Change state paths        | `YouTubePaths.cs`                                       | All paths relative to `state/youtube/`           |

## CONVENTIONS

- **Orchestrator pattern:** `YouTubePlaylistOrchestrator` owns the full sync pipeline. CLI command delegates to it.
- **ErrorOr chaining:** `.ThenAsync()` / `.Then()` pipeline. Breaks on error.
- **State persistence:** `YouTubeFetchState.SaveAsync()` / `LoadAsync()` — JSON to `state/youtube/manifest.json`.
- **Change detection:** `YouTubeChangeDetector.DetectChanges()` compares stored vs. current playlists.
- **Cross-service:** `YouTubeTranslationService` calls `Services.Azure.TranslateService` directly.

## ANTI-PATTERNS

- **NEVER** put sync logic in the CLI command. The orchestrator handles everything.
- **NEVER** bypass the orchestrator to call `YouTubePlaylistService` directly from CLI.
