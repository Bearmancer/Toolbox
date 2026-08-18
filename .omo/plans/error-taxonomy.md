# Error Taxonomy — Speculative vs Dead vs Logical-Error

**Source:** `toolbox-consolidated-spec.md` §2 (error producer→consumer map)
**Verified:** 2026-08-18 | Live: `src/Core/Errors.cs` (18 factories) + `src/Services/Google/YouTube/YouTubeSortService.cs` + `src/Services/Google/YouTube/YouTubeSyncProcessor.cs`

## Producer → Consumer Map

| Code | Consumer | Producer | Status |
|------|----------|----------|--------|
| `YT.RateLimit` | `SyncProcessor:79` | **0 producers** | **logical-error** — fix producer mappers |
| `YT.QuotaExceeded` | `SyncProcessor:79` not checked | `SortService:312` | **logical-error** — fix consumer check |
| `YT.PlaylistNotFound` | **0 consumers** | **0 producers** | **dead** — delete |
| `YT.VideoNotFound` | **0 consumers** | **0 producers** | **dead** — delete |
| `Azure.AuthFailed` | `SyncProcessor:88` | **0 producers** | **logical-error** — map at Translate boundary |
| `Azure.RateLimit` | `SyncProcessor:79` | **0 producers** | **logical-error** — map at Translate boundary |
| `Azure.ServiceUnavailable` | **0 consumers** | **0 producers** | **dead** — delete |

Live quota handling exists (`IsQuotaOrRateLimit` in `YouTubeSortService`, `maxWritesPerRun=150` budget in `YouTubeSyncProcessor`, `remainingBudget` param) — this table tracks *typed ErrorOr codes*, not exception-guard behavior. `YT.RateLimit` vs generic `ApiError` is a code-routing bug, not a missing guard.

## Fix Spec

- `YouTubePlaylistService.Delete/Insert/Fetch`: catch `GoogleApiException` → typed mapper (429→`RateLimit`, 403+quota→`QuotaExceeded`, 404→`PlaylistNotFound`, else→`ApiError`).
- `SyncProcessor:79`: add `"YT.QuotaExceeded"` to check.
- `TranslateService`: catch `HttpRequestException` 429→`Azure.RateLimit`, 401/403→`Azure.AuthFailed`.

## Related Dead Codes

`Errors.General.Unexpected/Internal`, `Errors.Validation.RequiredField`, `Text.Has/StartsWith`, `TranslateCommand --from`, `Serilog.Sinks.Console` PackageVersion — see `dead-code-catalog.md`.
