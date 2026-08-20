# ErrorOr Railway Migration + Pristine Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all exception-based control-flow in application logic, migrate ~10 Pristine/Google methods to ErrorOr returns, and harden Pristine browser automation against failure scenarios.

**Architecture:** Three-layer approach: (1) fix the 4 active smell points where the same method both throws and returns ErrorOr, (2) migrate ~10 plain-return methods to ErrorOr in Pristine/Google/Audio, (3) execute the Pristine hardening plan (browser lifecycle, media gates, diagnostics, QA). ErrorOr is kept for business logic; boundary exceptions from Playwright/Azure/Google SDKs are always caught at the ACL boundary and converted to ErrorOr.

**Tech Stack:** .NET 11, ErrorOr, Playwright, Azure SDK, Google APIs, Serilog/SerilogTracing, Spectre.Console

## Global Constraints

- `dotnet build --nologo -v q` must pass with 0 warnings/0 errors after every task
- `lsp_diagnostics` must be clean on every changed C# file after every task
- Never use null-forgiving operators (`!`) anywhere
- Never add empty catch blocks
- One class per file — if moving a class creates a new file, do it
- `OperationCanceledException` propagates naturally — never wrap it in ErrorOr
- Infrastructure exceptions (out of memory, disk full) must NOT be caught and wrapped in ErrorOr
- Third-party SDK boundaries (Playwright, Azure SDK, Google APIs) catch at the ACL and convert to ErrorOr via `Error.Unexpected` or a domain error from `Core/Errors.cs`
- No hardcoded credentials, cookie values, signed media URLs, or auth tokens in logs or committed files
- `Errors.Pristine`, `Errors.Audio`, `Errors.LastFm` error factories must remain in `src/Core/Errors.cs`
- Existing Telemetry event names and JSONL property names must not change unless documented in the task

---

## Wave 1: Smell Point Fixes (independent, no dependencies)

### Task 1: Fix LastFmApiClient — replace throw-in-ErrorOr pattern with pure ErrorOr

**Files:**
- Modify: `src/Services/LastFm/LastFmApiClient.cs:64-110`
- Modify: `src/Services/LastFm/LastFmService.cs:120-175`
- Modify: `src/Core/Errors.cs` — add `Errors.LastFm.RateLimited(TimeSpan)` and `Errors.LastFm.Retryable(int, string)`

**Interfaces:**
- Consumes: existing `LastFmApiException` (still in file — deleted in Task 2)
- Produces: `ExecuteHttpRequestAsync` returns `Task<ErrorOr<string>>`; `ParseJsonResponse` returns `ErrorOr<JsonElement>` with no throws

**Context:** `ParseJsonResponse` (line 89) declares `ErrorOr<JsonElement>` but throws `LastFmApiException` on retryable errors (line 106). `ExecuteHttpRequestAsync` throws `LastFmApiException` on HTTP 429. Both force callers to handle `if (result.IsError)` AND `catch`. This task makes both pure ErrorOr.

**New error factories — add to `Errors.LastFm` in `src/Core/Errors.cs`:**
```csharp
public static Error RateLimited(TimeSpan retryAfter) =>
    Error.Custom(429, "LastFm.RateLimited", $"Rate limited. Retry-After: {retryAfter.TotalSeconds}s");

public static Error Retryable(int code, string message) =>
    Error.Custom(503, "LastFm.Retryable", $"[{code}] {message}");
```

**Fixed ParseJsonResponse:**
```csharp
private static ErrorOr<JsonElement> ParseJsonResponse(string json)
{
    using JsonDocument doc = JsonDocument.Parse(json);
    JsonElement root = doc.RootElement;

    if (root.TryGetProperty("error", out JsonElement errorElement))
    {
        var errorCode = errorElement.GetInt32();
        var errorMessage = root.TryGetProperty("message", out JsonElement msgEl)
            ? msgEl.GetString() ?? "Unknown" : "Unknown";
        LastFmErrorType errorType = ClassifyError(errorCode);

        return errorType switch
        {
            LastFmErrorType.Permanent => Errors.LastFm.ApiError(errorMessage),
            LastFmErrorType.Fatal => Errors.LastFm.ApiError(errorMessage),
            _ => Errors.LastFm.Retryable(errorCode, errorMessage),
        };
    }

    return root.Clone();
}
```

**Fixed ExecuteHttpRequestAsync (change return type to `Task<ErrorOr<string>>`):**
```csharp
private async Task<ErrorOr<string>> ExecuteHttpRequestAsync(string url, CancellationToken ct)
{
    using HttpResponseMessage response = await Client.GetAsync(url, ct);

    if (response.StatusCode == HttpStatusCode.TooManyRequests)
    {
        TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        return Errors.LastFm.RateLimited(retryAfter);
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(cancellationToken: ct);
}
```

**Updated LastFmService retry loop — replace `catch (LastFmApiException)` with ErrorOr check:**
```csharp
var result = await _client.FetchPageCoreAsync(fetchAfter, page, limit, ct);
if (result.IsError)
{
    var err = result.FirstError;
    bool isRetryable = err.NumericType is 429 or 503;
    if (isRetryable && attempt < MaxRetries)
    {
        TimeSpan waitTime = delay;
        Telemetry.Warn("Last.fm API attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
            attempt, err.Description, waitTime.TotalSeconds);
        await Task.Delay(waitTime, ct);
        delay *= 2;
        continue;
    }
    return err;
}
return (result.Value.Scrobbles, result.Value.TotalPages);
```

- [ ] **Step 1:** Add `RateLimited` and `Retryable` factories to `Errors.LastFm`. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 2:** Change `ExecuteHttpRequestAsync` to `Task<ErrorOr<string>>`. Return `Errors.LastFm.RateLimited(retryAfter)` instead of throwing. Update its callers inside `LastFmApiClient.cs`. Run build. Expect 0 errors.
- [ ] **Step 3:** Change `ParseJsonResponse` — replace the ternary throw with the switch expression above. Run build. Expect 0 errors.
- [ ] **Step 4:** Update `LastFmService.FetchPageAsync` retry loop — remove `catch (LastFmApiException)` blocks, replace with `result.IsError` + `err.NumericType` check per the pattern above. Run build. Expect 0 errors.
- [ ] **Step 5:** Verify: `rg "throw new LastFmApiException" src/ -t cs` → 0 results. `rg "catch.*LastFmApiException" src/ -t cs` → 0 results.
- [ ] **Step 6:** `lsp_diagnostics` on `LastFmApiClient.cs`, `LastFmService.cs`, `Errors.cs`. Expect no errors.
- [ ] **Step 7:** Commit: `fix(lastfm): replace throw-in-ErrorOr with pure ErrorOr returns`

---

### Task 2: Delete LastFmApiException class

**Files:**
- Modify: `src/Services/LastFm/LastFmApiClient.cs` — delete `LastFmApiException` class (bottom of file) and `LastFmErrorType` enum if no external callers remain

**Context:** After Task 1 eliminates all throw/catch sites, `LastFmApiException` and `LastFmErrorType` become unreferenced. Delete them. Note: `LastFmErrorType` is still used by `ClassifyError()` internal method to determine the ErrorOr type — keep the enum if `ClassifyError` remains. Delete the exception class either way.

- [ ] **Step 1:** Run `rg "LastFmApiException" src/ -t cs`. Confirm only the class definition remains.
- [ ] **Step 2:** Delete the `LastFmApiException` class. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 3:** Check if `LastFmErrorType` enum is still used by `ClassifyError`. If yes, keep the enum. If no, delete it too.
- [ ] **Step 4:** Run `lsp_diagnostics` on `LastFmApiClient.cs`. Expect no errors.
- [ ] **Step 5:** Commit: `refactor(lastfm): delete LastFmApiException — pure ErrorOr`

---

### Task 3: Fix ProcessRunner — eliminate ProcessRunnerCanceledException

**Files:**
- Modify: `src/Services/Audio/ProcessRunner.cs:139, 169, 185` — replace throws with returns
- Modify: `src/Services/Audio/ProcessRunner.cs:243-246` — delete `catch (ProcessRunnerCanceledException) { throw; }`
- Modify: `src/Services/Audio/ProcessRunner.cs:326-332` — delete `ProcessRunnerCanceledException` class

**Context:** `ProcessRunnerCanceledException` extends `OperationCanceledException` to carry a `ProcessResult`. But `RunAsync` already returns `Task<ErrorOr<ProcessResult>>`, and `CallerCanceled` is already a valid `TerminationReason` on `ProcessResult`. The three throw sites can simply `return await stopAndBuildAsync(TerminationReason.CallerCanceled)` — same data, no exception.

**At each of the 3 throw sites, replace:**
```csharp
// Before:
throw new ProcessRunnerCanceledException(await stopAndBuildAsync(terminationReason), ct);

// After:
return await stopAndBuildAsync(TerminationReason.CallerCanceled);
```

**Callers of `RunAsync`** in `DsdConvertService`, `SaraconService`, `SoxService` already handle `ErrorOr<ProcessResult>` — no caller changes needed.

- [ ] **Step 1:** Replace the 3 throw sites with `return await stopAndBuildAsync(TerminationReason.CallerCanceled)`. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 2:** Delete `catch (ProcessRunnerCanceledException) { throw; }` block (lines 243-246). Run build. Expect 0 errors.
- [ ] **Step 3:** Delete `ProcessRunnerCanceledException` class (lines 326-332). Run build. Expect 0 errors.
- [ ] **Step 4:** Verify: `rg "ProcessRunnerCanceledException" src/ -t cs` → 0 results.
- [ ] **Step 5:** `lsp_diagnostics` on `ProcessRunner.cs`. Expect no errors.
- [ ] **Step 6:** Commit: `fix(audio): eliminate ProcessRunnerCanceledException`

---

## Wave 2: Pristine Migration Candidates (sequential, depend on Wave 1 green)

### Task 4: PristineBrowser.CreateAsync → Task<ErrorOr<IBrowserContext>>

**Files:**
- Modify: `src/Services/Pristine/PristineBrowser.cs:9`
- Modify callers: `src/Services/Pristine/PristineOrchestrator.cs`, `src/Services/Pristine/PristineLoginService.cs`

**Pattern:**
```csharp
public async Task<ErrorOr<IBrowserContext>> CreateAsync(bool headless, CancellationToken ct = default)
{
    try
    {
        // existing implementation unchanged
        return context; // implicit conversion
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex) { return Error.Unexpected("Browser.CreateFailed", ex.Message); }
}
```

**Callers:**
```csharp
var ctxResult = await _browser.CreateAsync(headless, ct);
if (ctxResult.IsError) return ctxResult.FirstError;
var context = ctxResult.Value;
```

- [ ] **Step 1:** Change signature and wrap body. Run build. Fix callers. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 2:** `lsp_diagnostics`. Commit: `fix(pristine): CreateAsync returns ErrorOr`

---

### Task 5: WaitForLoginAsync → Task<ErrorOr<Success>> (both copies)

**Files:**
- Modify: `src/Services/Pristine/PristineOrchestrator.cs:183`
- Modify: `src/Services/Pristine/PristineLoginService.cs:112`
- Modify callers in both files

**Pattern (applies to both private methods):**
```csharp
private static async Task<ErrorOr<Success>> WaitForLoginAsync(IPage page, CancellationToken ct, int timeoutS = 180)
{
    try
    {
        // existing wait logic
        if (timedOut) return Errors.Pristine.LoginTimeout;
        return Result.Success;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex) { return Error.Unexpected("Pristine.LoginWaitFailed", ex.Message); }
}
```

**Callers change from:**
```csharp
if (!await WaitForLoginAsync(page, ct)) return Errors.Pristine.LoginTimeout;
```
**To:**
```csharp
var loginResult = await WaitForLoginAsync(page, ct);
if (loginResult.IsError) return loginResult.FirstError;
```

- [ ] **Step 1:** Change both signatures + bodies. Fix callers. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 2:** `lsp_diagnostics`. Commit: `fix(pristine): WaitForLoginAsync returns ErrorOr`

---

### Task 6: Migrate DownloadAsync + void Tasks to ErrorOr

**Files:**
- Modify: `src/Services/Pristine/PristineDownloader.cs:10`
- Modify: `src/Services/Pristine/PristineAlbumService.cs` — `StartPlaybackAsync`, `DownloadArtworkAndPdfAsync`
- Modify all callers

**Pattern:**
```csharp
public async Task<ErrorOr<Success>> DownloadAsync(...)
{
    try { /* existing */ return Result.Success; }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex) { return Error.Unexpected("Pristine.DownloadFailed", ex.Message); }
}
```

- [ ] **Step 1:** Migrate `DownloadAsync`. Fix callers. Run build.
- [ ] **Step 2:** Migrate `StartPlaybackAsync` and `DownloadArtworkAndPdfAsync`. Fix callers. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 3:** `lsp_diagnostics`. Commit: `fix(pristine): migration candidates return ErrorOr`

---

### Task 7: DownloadSingleAlbumAsync → Task<ErrorOr<PristineAlbumResult>>

**Files:**
- Modify: `src/Services/Pristine/PristinePollService.cs:14`
- Modify callers: `src/Services/Pristine/PristineOrchestrator.cs`

**Context:** The 550-line god method currently returns `Task<PristineAlbumResult>`. Change to `Task<ErrorOr<PristineAlbumResult>>`. Each failure path that currently returns a default/empty result should now return the appropriate `Errors.Pristine.*` error. Success paths return the `PristineAlbumResult` directly (implicit ErrorOr conversion).

- [ ] **Step 1:** Change signature. Scan every `return` statement — failure paths get `Errors.Pristine.*`, success paths return result directly. Fix callers. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 2:** `lsp_diagnostics`. Commit: `fix(pristine): DownloadSingleAlbumAsync returns ErrorOr`

---

## Wave 3: Google/OCI Migration (independent of Wave 2, depends on Wave 1)

### Task 8: OciDashboardDeployer.DeployAsync → Task<ErrorOr<Success>> + fix SyncYoutubeCommand SoC

**Files:**
- Modify: `src/Services/Google/Dashboard/OciDashboardDeployer.cs` — `DeployAsync` returns `Task<ErrorOr<Success>>`
- Modify: `src/Services/Google/Dashboard/DashboardOrchestrator.cs:35` — handle ErrorOr from DeployAsync
- Modify: `src/CLI/Sync/YouTube/SyncYoutubeCommand.cs` — delete `RegenerateDashboardAsync` private method, route through `_orchestrator.GenerateAndDeployAsync`

**Context:** The verified audit confirmed that `SyncYoutubeCommand.RegenerateDashboardAsync` manually reimplements what `DashboardOrchestrator.GenerateAndDeployAsync` already does, bypassing the orchestrator entirely. This task fixes both issues simultaneously.

**Fix DeployAsync:**
```csharp
public static async Task<ErrorOr<Success>> DeployAsync(string dir, CancellationToken ct)
{
    try
    {
        // existing SSH/SFTP implementation
        return Result.Success;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex) { return Error.Unexpected("OCI.DeployFailed", ex.Message); }
}
```

**Fix SyncYoutubeCommand — remove `RegenerateDashboardAsync`, use orchestrator:**
```csharp
// Replace the private method call with:
var deployResult = await _orchestrator.GenerateAndDeployAsync(ct);
if (deployResult.IsError)
    Telemetry.Warn("Dashboard deploy failed: {Error}", deployResult.FirstError.Description);
```

- [ ] **Step 1:** Change `DeployAsync` to `Task<ErrorOr<Success>>`. Wrap SSH logic. Fix `DashboardOrchestrator` caller. Run build.
- [ ] **Step 2:** Delete `RegenerateDashboardAsync` from `SyncYoutubeCommand`. Add `_orchestrator` field injection. Route call through `GenerateAndDeployAsync`. Run `dotnet build --nologo -v q`. Expect 0 errors.
- [ ] **Step 3:** Verify: `rg "RegenerateDashboardAsync" src/ -t cs` → 0 results.
- [ ] **Step 4:** `lsp_diagnostics`. Commit: `fix(google): DeployAsync ErrorOr; sync uses orchestrator`

---

## Wave 4: Pristine Hardening (depends on Waves 1-3 complete)

**These tasks follow the full plan at `C:\Users\Lance\Dev\Toolbox\.omo\plans\pristine-hardening.md`.** Read that file first — all acceptance criteria, QA scenarios, references, and evidence paths are defined there. Tasks below reference the todo numbers.

| Plan Task | Pristine Hardening Todo | Commit |
| :--- | :--- | :--- |
| Task 9 | Todo 1 — Harden compiler patterns + resolver failures | `fix(pristine): harden historical error paths` |
| Task 10 | Todo 2 — Config + secret-state hygiene | `chore(pristine): protect auth and diagnostic state` |
| Task 11 | Todo 3 — Browser cookies + async resource cleanup | `fix(pristine): harden browser auth lifecycle` |
| Task 12 | Todo 4 — Structured Playwright diagnostics | `feat(pristine): add structured browser diagnostics` |
| Task 13 | Todo 5 — Transient album retries on fresh pages | `fix(pristine): isolate transient album retries` |
| Task 14 | Todo 6 — Album resolution state-driven | `fix(pristine): make album resolution state-driven` |
| Task 15 | Todo 7 — 16-bit candidate selection + post-download gate | `fix(pristine): enforce 16-bit media gate` |
| Task 16 | Todo 8 — Downloader + verifier state semantics | `fix(pristine): make media verification authoritative` |
| Task 17 | Todo 9 — Failure result + CLI exit semantics | `fix(pristine): report album failures truthfully` |
| Task 18 | Todo 10 — Azure + runtime preflight (verification-only) | N/A |
| Task 19 | Todo 11 — Single-FLAC + ffprobe QA (verification-only) | N/A |
| Task 20 | Todo 12 — Full-album concurrency + sequential multi-PASC (verification-only) | N/A |

---

## Final Verification Wave (after all 20 tasks complete — run in parallel)

- [ ] **F1: Railway compliance** — `rg "throw new LastFmApiException|throw new ProcessRunnerCanceledException" src/ -t cs` → 0 results. All migration candidates return ErrorOr. No method both throws and returns ErrorOr.
- [ ] **F2: Code quality** — No `!`, no empty catches, no suppression pragmas, one-class-per-file on changed files. Build clean.
- [ ] **F3: Manual QA** — `dotnet build --nologo -v q`. Pristine single-FLAC run. ffprobe gate. Multi-PASC sequential evidence.
- [ ] **F4: Scope fidelity** — Only planned files changed. No auth deletion. No unrelated service changes.

---

## Dependency Matrix

| Task | Depends on | Parallelizable With |
| :--- | :--- | :--- |
| Task 1 (LastFm ErrorOr) | none | Task 3 |
| Task 2 (Delete exception) | Task 1 | — |
| Task 3 (ProcessRunner) | none | Task 1 |
| Task 4 (PristineBrowser) | none | Tasks 1, 3 |
| Task 5 (WaitForLoginAsync) | Task 4 | — |
| Task 6 (DownloadAsync) | Task 5 | — |
| Task 7 (DownloadSingleAlbum) | Task 6 | — |
| Task 8 (OciDeployer) | Tasks 1-3 | Tasks 4-7 |
| Tasks 9-20 (Hardening) | Tasks 1-8 | Tasks 9-10-11 (Wave 1 of hardening) |
| F1-F4 | Tasks 1-20 | Each other |
