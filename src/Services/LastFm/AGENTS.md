# AGENTS.md — Services/LastFm

Last.fm API client + sync orchestrator. Persists scrobbles to `state/lastfm/`.

## STRUCTURE

```
LastFm/
├── LastFmSetup.cs            # DI: reads LASTFM_API_KEY + LASTFM_USERNAME from env, registers singletons
├── LastFmApiClient.cs        # HTTP layer: URL building, request execution, JSON parsing, error classification
├── LastFmService.cs          # Business logic: paginated fetch, retry (3x, exponential), 200ms rate limit
├── LastFmSyncOrchestrator.cs # Sync flow: load state → filter → fetch → merge → save
└── LastFmState.cs            # Static: LoadScrobblesAsync, SaveScrobblesAsync, MergeScrobbles (dedup by PlayedAt)
```

## WHERE TO LOOK

| Task | File | Notes |
|------|------|-------|
| Add API endpoint | `LastFmApiClient.cs` | Add to `BuildFetchUrl`, chain in `FetchPageCoreAsync` |
| Change fetch behavior | `LastFmService.cs` | `FetchRecentTracksAsync` controls pagination + stop condition |
| Modify sync logic | `LastFmSyncOrchestrator.cs` | Load, filter, merge, save. Returns `SyncResult` record |
| Change persistence format | `LastFmState.cs` | `scrobbles.json` only. Uses `JsonSerializerOptions { WriteIndented = true }` |
| Add error codes | `LastFmApiClient.cs` | `ClassifyError` switch maps Last.fm codes to `Retryable/Fatal/Permanent` |
| Add env var | `LastFmSetup.cs` | Read in `AddLastFmServices()`, throw `InvalidOperationException` if missing |

## CONVENTIONS

- **Auth:** `LASTFM_API_KEY` + `LASTFM_USERNAME` via env. Never hardcode.
- **Error flow:** `LastFmApiException` (retryable/fatal) → caught by `FetchPageAsync` → returns `ErrorOr<T>`.
- **Rate limit:** 200ms between requests. HTTP 429 → respect `Retry-After` header, fallback 5s.
- **Retry:** 3 attempts, exponential backoff. Only on `Retryable` errors + `HttpRequestException`.
- **Merge:** `PlayedAt` is the dedup key. `MergeScrobbles` groups by `PlayedAt`, takes first.
- **JSON:** `LastFmScrobble` properties are PascalCase. No `PropertyNamingPolicy`.
- **Scrobble display:** `Date` property returns IST (UTC+5:30) formatted string.
- **Telemetry:** Every operation scoped with `Telemetry.ForService(ServiceName.LastFm)`.

## ANTI-PATTERNS

- **NEVER** hardcode API keys or usernames. Env vars only.
- **NEVER** bypass rate limiting. `WaitForRateLimit` is mandatory before each request.
- **NEVER** modify `state/lastfm/scrobbles.json` directly. Use `LastFmState.SaveScrobblesAsync`.
- **NEVER** catch and swallow `LastFmApiException` without logging. `FetchPageAsync` handles this.
- **NEVER** assume single track per API response. Response can return array or single object.
