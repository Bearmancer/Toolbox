# AGENTS.md — Services/LastFm

Last.fm API client + sync orchestrator. Persists scrobbles to `state/lastfm/scrobbles.json`.

## STRUCTURE

```
LastFm/
├── LastFmSetup.cs            # DI: reads LASTFM_API_KEY + LASTFM_USERNAME from env, registers singletons
├── LastFmApiClient.cs        # HTTP layer: BuildFetchUrl, request execution, JSON parsing, ClassifyError
├── LastFmService.cs          # Business logic: FetchRecentTracksAsync pagination, 3x retry, 200ms rate limit
├── LastFmSyncOrchestrator.cs # Sync flow: load state → filter → fetch → merge → save (SyncResult)
└── LastFmState.cs            # Persistence: LoadScrobblesAsync, SaveScrobblesAsync, MergeScrobbles (PlayedAt dedup)
```

## WHERE TO LOOK

| Task | File | Notes |
| ---- | ---- | ----- |
| Add API endpoint | `LastFmApiClient.cs` | Add to `BuildFetchUrl`, chain in `FetchPageCoreAsync` |
| Change fetch | `LastFmService.cs` | `FetchRecentTracksAsync` controls pagination + stop condition |
| Modify sync | `LastFmSyncOrchestrator.cs` | Load, filter, merge, save. Returns `SyncResult` record |
| Persistence | `LastFmState.cs` | `scrobbles.json` only. `JsonSerializerOptions { WriteIndented = true }` |
| Error codes | `LastFmApiClient.cs` | `ClassifyError` maps codes → `Retryable/Fatal/Permanent` |
| Env var | `LastFmSetup.cs` | Read in `AddLastFmServices()`, throw `InvalidOperationException` if missing |

## CONVENTIONS

- **Auth:** `LASTFM_API_KEY` + `LASTFM_USERNAME` via env. Never hardcode.
- **Error flow:** `LastFmApiException` (Retryable/Fatal) → `FetchPageAsync` → `ErrorOr<T>`.
- **Rate limit:** 200ms between requests. HTTP 429 → `Retry-After` header, fallback 5s.
- **Retry:** 3 attempts, exponential backoff. Only on `Retryable` + `HttpRequestException`.
- **Merge:** `MergeScrobbles` dedups by `PlayedAt` (GroupBy, take First), sorted descending.
- **JSON:** PascalCase properties. No `PropertyNamingPolicy`.
- **Display:** `LastFmScrobble.Date` returns IST (UTC+5:30) `yyyy-MM-dd HH:mm`.
- **Telemetry:** `Telemetry.ForService(ServiceName.LastFm)` per operation.

## ANTI-PATTERNS

- **NEVER** hardcode API keys. Env vars only.
- **NEVER** bypass `WaitForRateLimit` (200ms) before each request.
- **NEVER** write `state/lastfm/scrobbles.json` directly. Use `LastFmState.SaveScrobblesAsync`.
- **NEVER** swallow `LastFmApiException` without logging. `FetchPageAsync` handles it.
- **NEVER** assume single track shape. Response `track` can be array or single object.
