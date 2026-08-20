# ErrorOr Migration Feasibility Assessment

Can the full Toolbox repo migrate to railway-style `ErrorOr` and ditch all exceptions? Three independent assessment teams analyzed the exception landscape, ErrorOr coverage, and library feasibility. Here is the consolidated verdict.

---

## Current State: The Numbers

### Exception Landscape

| Metric                                      | Count                                                                                    |
| :------------------------------------------ | :--------------------------------------------------------------------------------------- |
| Total explicit `throw` statements           | **35**                                                                                   |
| Total custom exception classes              | **2** (`LastFmApiException`, `ProcessRunnerCanceledException`)                           |
| Third-party libraries that force exceptions | **5** (Playwright, Azure SDK, Google APIs, System.Text.Json, System.Diagnostics.Process) |

### ErrorOr Coverage

| Metric                                                                   | Count   |
| :----------------------------------------------------------------------- | :------ |
| Methods returning `ErrorOr<T>` (sync)                                    | **6**   |
| Methods returning `Task<ErrorOr<T>>` (async)                             | **50+** |
| CLI boundary points unwrapping ErrorOr → exit codes                      | **12**  |
| Methods mixing `throw` + `ErrorOr` return (smell points)                 | **4**   |
| Methods using try/catch but returning plain types (migration candidates) | **~10** |

### Verdict: The repo is already ~80% railway.

ErrorOr is the dominant pattern across Audio, Azure, Google, LastFm, and Pristine services. Only ~10 methods in Pristine and Google still use plain return types with internal try/catch, and only 4 methods actively mix both patterns.

---

## The 35 `throw` Statements: Can They Be Eliminated?

### ❌ Cannot Eliminate (Truly Exceptional — 9 throws)

These represent programmer errors, corrupted data, or missing environment configuration. Exceptions are the correct tool here.

| Namespace                            | Exception                                           | Why It Must Stay                  |
| :----------------------------------- | :-------------------------------------------------- | :-------------------------------- |
| `Core.PathResolver`                  | `ArgumentOutOfRangeException`                       | Limit exceeded — programmer error |
| `Core.PathResolver`                  | `FileNotFoundException`                             | Missing critical data file        |
| `Core.ServiceName`                   | `ArgumentOutOfRangeException`                       | Invalid enum — programmer error   |
| `Services.Audio.DffHeaderReader`     | `InvalidDataException` (x2)                         | Corrupted binary file format      |
| `Services.Audio.DffHeaderReader`     | `EndOfStreamException`                              | Truncated binary file             |
| `Services.Audio.DffMetadataStripper` | `InvalidDataException` (x2), `EndOfStreamException` | Same as above                     |
| `Services.Azure.AzureCredentials`    | `InvalidOperationException`                         | Missing env var — startup failure |
| `Services.Google.GoogleSetup`        | `InvalidOperationException`                         | Missing env var — startup failure |

### ✅ Can Migrate to ErrorOr (Control-Flow — 4 throws)

These use exceptions for business logic signaling and should be converted to ErrorOr returns.

| Location                                  | Current                                              | Proposed                                                                      |
| :---------------------------------------- | :--------------------------------------------------- | :---------------------------------------------------------------------------- |
| `LastFmApiClient.ParseJsonResponse`       | Throws `LastFmApiException` for retryable API errors | Return `ErrorOr` with a `Retryable` error type; let caller check `error.Type` |
| `LastFmApiClient.ExecuteHttpRequestAsync` | Throws `LastFmApiException` on HTTP 429              | Same — return `ErrorOr` with metadata                                         |
| `Services.Audio.ProcessRunner`            | Throws `ProcessRunnerCanceledException`              | Return `ErrorOr` with `Error.Failure("Process.Cancelled", ...)`               |
| `Services.Azure.SpeechService`            | Throws `InvalidOperationException` on ffmpeg failure | Return `ErrorOr` with `Errors.Audio.ProcessFailed`                            |

---

## The 5 Third-Party Boundaries: Anti-Corruption Layers

These libraries will **always** throw exceptions. You cannot eliminate them. You must catch at the boundary and convert to ErrorOr.

| Library                        | Exceptions Thrown                         | Current Handling                               | Recommendation                                         |
| :----------------------------- | :---------------------------------------- | :--------------------------------------------- | :----------------------------------------------------- |
| **Playwright**                 | `TimeoutException`, `PlaywrightException` | Caught in `PristinePollService` god method     | Extract boundary wrapper methods that return `ErrorOr` |
| **Azure SDK**                  | `RequestFailedException`                  | Caught in Azure services, converted to ErrorOr | ✅ Already correct pattern                             |
| **Google APIs**                | `GoogleApiException`                      | Caught in YouTube services, mapped to ErrorOr  | ✅ Already correct pattern                             |
| **System.Text.Json**           | `JsonException`                           | Caught around state file reads                 | ✅ Already correct pattern                             |
| **System.Diagnostics.Process** | `InvalidOperationException`               | Caught in ProcessRunner                        | Migrate ProcessRunner to return ErrorOr                |

---

## The 4 Architectural Smell Points

These methods declare `ErrorOr<T>` return types but also throw exceptions, forcing callers to handle both patterns simultaneously:

| Method                                    | Returns                         | Also Throws                                                    | Fix                                                              |
| :---------------------------------------- | :------------------------------ | :------------------------------------------------------------- | :--------------------------------------------------------------- |
| `ProcessRunner.RunAsync`                  | `Task<ErrorOr<ProcessResult>>`  | `ProcessRunnerCanceledException`, `OperationCanceledException` | Catch internally, return `Error.Failure`                         |
| `DsdConvertService.ProbeDsdAsync`         | `Task<ErrorOr<DsdProbeResult>>` | Rethrows `OperationCanceledException`                          | Let cancellation propagate naturally (it's not a business error) |
| `LastFmApiClient.ParseJsonResponse`       | `ErrorOr<JsonElement>`          | `LastFmApiException`                                           | Return `ErrorOr` with retryable error metadata                   |
| `LastFmApiClient.ExecuteHttpRequestAsync` | `Task<ErrorOr<string>>`         | `LastFmApiException`                                           | Same as above                                                    |

---

## The ~10 Migration Candidates

These methods currently return plain types with internal try/catch but should return `ErrorOr<T>`:

| Method                                            | Current Return              | Proposed                             |
| :------------------------------------------------ | :-------------------------- | :----------------------------------- |
| `PristinePollService.DownloadSingleAlbumAsync`    | `Task<PristineAlbumResult>` | `Task<ErrorOr<PristineAlbumResult>>` |
| `PristinePollService.WaitForLoginAsync`           | `Task<bool>`                | `Task<ErrorOr<Success>>`             |
| `PristineOrchestrator.WaitForLoginAsync`          | `Task<bool>`                | `Task<ErrorOr<Success>>`             |
| `PristineAlbumService.StartPlaybackAsync`         | `Task`                      | `Task<ErrorOr<Success>>`             |
| `PristineAlbumService.DownloadArtworkAndPdfAsync` | `Task`                      | `Task<ErrorOr<Success>>`             |
| `PristineBrowser.CreateAsync`                     | `Task<IBrowserContext>`     | `Task<ErrorOr<IBrowserContext>>`     |
| `PristineDownloader.DownloadAsync`                | `Task<bool>`                | `Task<ErrorOr<Success>>`             |
| `OciDashboardDeployer.DeployAsync`                | `Task`                      | `Task<ErrorOr<Success>>`             |
| `YouTubeSyncProcessor.SortPlaylistsAsync`         | `Task<SortStatistics>`      | `Task<ErrorOr<SortStatistics>>`      |

---

## Known Pain Points of Full Railway in C#

The ErrorOr Feasibility Analyst identified these fundamental C# language friction points:

| Pain Point                  | Impact                                                                        | Mitigation                                                                               |
| :-------------------------- | :---------------------------------------------------------------------------- | :--------------------------------------------------------------------------------------- |
| **`void` methods**          | ErrorOr doesn't support `void`. Must use `ErrorOr<Success>`.                  | Minor boilerplate — acceptable                                                           |
| **`using` / `IDisposable`** | `using` is a statement; ROP pipelines are expressions. Mixing them is clunky. | Don't chain `Then` across resource boundaries. Use imperative style with `using` blocks. |
| **`CancellationToken`**     | Clutters every lambda in `.ThenAsync()` chains                                | Let `OperationCanceledException` propagate naturally — it's not a business error         |
| **Lost stack traces**       | `Error` objects don't carry stack traces like exceptions do                   | Use exceptions for infrastructure crashes; ErrorOr only for expected business failures   |
| **Debugging fluent chains** | Can't step through `.ThenAsync()` lambdas easily                              | Prefer imperative `if (result.IsError) return result.Errors;` over long fluent chains    |

---

## Final Recommendation

### Can the repo go full ErrorOr? **No. Hybrid is correct.**

The repo is already ~80% there. The remaining work is small and surgical:

### Do This (Small, High-Value)

1. **Fix the 4 smell points** — eliminate the `throw` + `ErrorOr` mixing in LastFmApiClient and ProcessRunner
2. **Migrate the ~10 candidates** — convert Pristine and Google methods from try/catch to ErrorOr returns
3. **Delete 1 custom exception** — `LastFmApiException` becomes an `Error` with retryable metadata
4. **Keep `ProcessRunnerCanceledException`** — or let `OperationCanceledException` propagate naturally

### Do NOT Do This

1. ❌ Don't eliminate exceptions from binary parsers (DFF) — `InvalidDataException` is the correct signal for corrupt files
2. ❌ Don't eliminate startup validation throws — `InvalidOperationException` for missing env vars is correct fail-fast behavior
3. ❌ Don't wrap `OperationCanceledException` in ErrorOr — cancellation is not a business error, it's a runtime concern
4. ❌ Don't use long `.ThenAsync()` fluent chains — prefer imperative `if (result.IsError)` for debuggability
5. ❌ Don't add `Microsoft.Extensions.Http.Resilience` to replace LastFm retries — the API returns errors inside 200 OK bodies

### Effort Estimate

| Task                              | Effort     |
| :-------------------------------- | :--------- |
| Fix 4 smell points                | ~2 hours   |
| Migrate ~10 candidates            | ~4 hours   |
| Delete `LastFmApiException` class | ~1 hour    |
| **Total**                         | **~1 day** |
