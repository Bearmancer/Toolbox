# Pristine Refactor — Running Log

**Session**: 619ecbd4-5987-42ed-a03d-78c109835b16  
**Started**: 2026-08-19  
**Status**: In Progress  

---

## Why No FLAC Attempts Have Happened

**Root Cause**: Playwright API calls don't observe `CancellationToken`. The `resolveCts.CancelAfter(45s)` fires but `await page.ClickAsync(...)` doesn't throw because Playwright's methods don't accept CancellationToken directly.

**Evidence**: 
- Log stops at `Pristine.Album.ResolveStart` (line 8 of 8 total)
- No `Pristine.Album.Attempt` logs appear
- First `ClickAsync` with 3s timeout hangs indefinitely
- 45s hard cap ignored because current `await` is blind to cancellation

**Fix Required**: Wrap each Playwright call with `.WaitAsync(resolveCt)` or use `Task.WhenAny` pattern.

---

## Failure Log

### Failure #1: Empty catch blocks (15+)
**File**: `PristineAlbumService.cs`  
**Issue**: Silent failures, zero telemetry  
**Status**: ✅ Fixed — all catches now log with `Telemetry.Debug/Warn/Error`  
**Attempt**: Rewrote entire file with logging  
**Result**: Build succeeded, but runtime hang discovered  

### Failure #2: IDE0007 style violations (30+)
**File**: `PristineAlbumService.cs`, `PristineBrowser.cs`, `PristinePollService.cs`  
**Issue**: Explicit types instead of `var`  
**Status**: ✅ Fixed — `dotnet format` auto-corrected  
**Attempt**: Ran `dotnet format --verbosity quiet`  
**Result**: Build clean  

### Failure #3: Missing Errors.Pristine factories
**File**: `Errors.cs`  
**Issue**: `TracklistParseFailed`, `No16BitFlac` didn't exist  
**Status**: ✅ Fixed — added both factories  
**Attempt**: Manual edit  
**Result**: Build clean  

### Failure #4: ErrorOr type mismatch
**File**: `PristinePollService.cs:100`  
**Issue**: `ParseTracklistAsync` returns `ErrorOr<List<string>>` but caller expected `List<string>`  
**Status**: ✅ Fixed — added `.IsError` check  
**Attempt**: Manual edit  
**Result**: Build clean  

### Failure #5: PowerShell file corruption
**File**: `PristineAlbumService.cs`  
**Issue**: `Get-Content | Set-Content` collapsed file to 1 line  
**Status**: ✅ Fixed — manual rewrite  
**Attempt**: Used `Set-Content` to fix regex  
**Result**: File destroyed, had to rewrite from scratch  
**Lesson**: Never use PowerShell pipeline for file editing  

### Failure #6: CS8602 null dereference
**File**: `PristineAlbumService.cs:131`  
**Issue**: `el.GetAttributeAsync("href").GetAwaiter().GetResult()[..80]` — null reference  
**Status**: ✅ Fixed — null-safe `?? string.Empty`  
**Attempt**: Manual edit  
**Result**: Build clean  

### Failure #7: Cookie rejection (2 `__Host-*`)
**File**: `PristineBrowser.cs`  
**Issue**: Domain mismatch `.pristinestreaming.com`  
**Status**: ⚠️ Logged — warn only, non-blocking  
**Attempt**: Added per-cookie try/catch  
**Result**: 9 cookies applied, 2 rejected  
**Impact**: May cause auth failures  

### Failure #8: Runtime hang at ResolveStart
**File**: `PristineAlbumService.cs`  
**Issue**: Playwright calls ignore `CancellationToken`  
**Status**: ❌ NOT FIXED — current blocker  
**Attempt**: Added `resolveCts.CancelAfter(45s)`  
**Result**: Timeout fires but `await` doesn't throw  
**Root Cause**: Playwright API doesn't accept CancellationToken  
**Fix Required**: Wrap calls with `.WaitAsync(resolveCt)`  

### Failure #9: 16-bit probe untested
**File**: `PristinePollService.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Attempt**: N/A  
**Result**: N/A  

### Failure #10: Semaphore(5) untested
**File**: `PristinePollService.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Attempt**: N/A  
**Result**: N/A  

### Failure #11: Sequential album download untested
**File**: `PristineOrchestrator.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Attempt**: N/A  
**Result**: N/A  

### Failure #12: Single-FLAC proof not landed
**File**: N/A  
**Issue**: Never reached download phase  
**Status**: ❌ Blocked by Failure #8  
**Attempt**: N/A  
**Result**: N/A  

---

## Logging Spec (from design §10)

### Every catch logs:
- `Debug` — retryable (selector miss, nav timeout, eval miss)
- `Warn` — recoverable (resolve fail, empty tracklist, stall)
- `Error` — terminal (goto browse failed, playback failed, all downloads failed)

### Method entries:
- `using var _ = Telemetry.ForService(ServiceName.Pristine)` OR
- `Telemetry.Info("Pristine.X.Start ...")`

### Mandatory log points:
- Cookie count + domains (no values)
- Each Playwright step: selector, attempt, error
- URL after each navigation
- Resolve attempts (3 max) + outcome
- Candidate URLs with tier (0=16-bit, 1=flac, 2=mp3)
- Stall count `n/60`, retry attempts
- Download success/failure + file path
- Probe result: codec, bits, rate, channels

### Gates:
- No `!` operator in `src/Services/Pristine/**`
- No `catch{}` without telemetry
- `.editorconfig` clean (var for non-builtins, `new()`, `[]`, `is not null`)

---

## Code Changes Log

### Change #1: PristineAlbumService.cs rewrite
**Date**: 2026-08-19  
**Rationale**: Eliminate silent failures, add telemetry  
**Expected**: All catches log, build clean  
**Actual**: Build clean, runtime hang  
**Reasoning**: Added `Telemetry.ForService` scope, logged every step  

### Change #2: Errors.cs additions
**Date**: 2026-08-19  
**Rationale**: Support new error types  
**Expected**: Build clean  
**Actual**: Build clean  
**Reasoning**: Added `TracklistParseFailed`, `No16BitFlac`  

### Change #3: PristinePollService.cs ErrorOr handling
**Date**: 2026-08-19  
**Rationale**: Fix type mismatch  
**Expected**: Build clean  
**Actual**: Build clean  
**Reasoning**: Added `.IsError` check before accessing `.Value`  

### Change #4: PristineBrowser.cs per-cookie try/catch
**Date**: 2026-08-19  
**Rationale**: Prevent single bad cookie from killing batch  
**Expected**: All good cookies applied  
**Actual**: 9 applied, 2 rejected  
**Reasoning**: Added per-cookie error handling  

### Change #5: PristineAlbumService.cs cancellation token
**Date**: 2026-08-19  
**Rationale**: Prevent infinite hang  
**Expected**: 45s timeout fires, operation cancels  
**Actual**: Timeout fires but `await` doesn't throw  
**Reasoning**: Added `resolveCts.CancelAfter(45s)` but Playwright ignores it  

---

## Attempt Log

### Attempt #1: Single FLAC download (PASC552)
**Command**: `dotnet run --project src/App -- pristine download PASC552 --single --headless`  
**Expected**: One FLAC file at `Desktop\Pristine\<Album>\01. <Title>.flac`  
**Actual**: Command timed out after 90s, log shows only 8 lines stopping at `ResolveStart`  
**Rationale**: First Playwright call hangs, cancellation token ignored  

### Attempt #2: Single FLAC download (PASC552) with env var
**Command**: `$env:PRISTINE_BASE_OUT_DIR='C:\Users\Lance\Desktop\Pristine'; dotnet run --project src/App -- pristine download PASC552 --single --headless`  
**Expected**: One FLAC file  
**Actual**: Command timed out after 30s  
**Rationale**: Same hang, env var doesn't affect root cause  

### Attempt #3: Verbose logging
**Command**: `dotnet run --project src/App -- pristine download PASC552 --single --headless --verbose`  
**Expected**: Detailed logs showing where hang occurs  
**Actual**: Command timed out after 30s, no additional logs  
**Rationale**: Debug logs not flushing, or hang occurs before logging  

---

## Todo List

### Priority: High (Blockers)

1. **Fix Playwright cancellation in `ResolveAlbumIdAsync`**
   - **File**: `src/Services/Pristine/PristineAlbumService.cs`
   - **Issue**: Playwright calls ignore `CancellationToken`
   - **Fix**: Wrap each call with `.WaitAsync(resolveCt)` or use `Task.WhenAny`
   - **Expected**: 45s timeout fires, operation cancels cleanly
   - **Status**: ❌ Pending

2. **Verify single-FLAC proof lands (PASC552 `--single -H`)**
   - **File**: N/A (integration test)
   - **Issue**: Never reached download phase
   - **Expected**: One FLAC at `Desktop\Pristine\<Album>\01. <Title>.flac`
   - **Status**: ❌ Blocked by #1

3. **Verify 16-bit probe selects tier-0 URL**
   - **File**: `src/Services/Pristine/PristinePollService.cs`
   - **Issue**: Spec written, code present, never executed
   - **Expected**: Log shows `Pristine.Poll.QualitySelected` with 16-bit
   - **Status**: ❌ Blocked by #2

### Priority: Medium (Post-Blocker)

4. **Verify semaphore(5) concurrency**
   - **File**: `src/Services/Pristine/PristinePollService.cs`
   - **Issue**: Spec written, code present, never executed
   - **Expected**: Max 5 concurrent `.part` files observable
   - **Status**: ❌ Blocked by #2

5. **Verify sequential album download (PASC552 + PASC553)**
   - **File**: `src/Services/Pristine/PristineOrchestrator.cs`
   - **Issue**: Spec written, code present, never executed
   - **Expected**: Album 2 starts only after album 1 finishes
   - **Status**: ❌ Blocked by #2

6. **Verify `ffprobe` confirms 16-bit 44100Hz**
   - **File**: N/A (integration test)
   - **Issue**: Never reached verification phase
   - **Expected**: `ffprobe -show_streams` reports `bits_per_sample=16`, `sample_rate=44100`
   - **Status**: ❌ Blocked by #2

### Priority: Low (Future)

7. **Fix cookie rejection for `__Host-*` cookies**
   - **File**: `src/Services/Pristine/PristineBrowser.cs`
   - **Issue**: 2 cookies rejected due to domain mismatch
   - **Expected**: All valid cookies applied
   - **Status**: ⚠️ Logged, non-blocking

8. **Add Firefox cookie extraction as fallback**
   - **File**: `src/Services/Pristine/PristineBrowser.cs`
   - **Issue**: `auth.json` may not have all session cookies
   - **Expected**: Extract from `cookies.sqlite` if `auth.json` insufficient
   - **Status**: ❌ Not started

---

## Next Action

**Fix #1**: Wrap Playwright calls with `.WaitAsync(resolveCt)` to respect cancellation token.

**Implementation**:
```csharp
// Before:
await page.ClickAsync(searchSelector, new PageClickOptions { Timeout = 3000 });

// After:
await page.ClickAsync(searchSelector, new PageClickOptions { Timeout = 3000 }).WaitAsync(resolveCt);
```

Apply to all Playwright calls in `ResolveAlbumIdAsync`:
- `ClickAsync`
- `EvaluateAsync`
- `FillAsync`
- `WaitForLoadStateAsync`
- `GotoAsync`
- `QuerySelectorAsync`

Then rebuild, rerun, verify log shows `Pristine.Album.Attempt` entries.

---

## Log Format

Each entry follows:
```
### [Failure|Change|Attempt] #N: [Title]
**File**: [path]
**Issue**: [what went wrong]
**Status**: [✅ Fixed | ❌ Pending | ⚠️ Partial]
**Attempt**: [what was tried]
**Expected**: [what should happen]
**Actual**: [what actually happened]
**Rationale**: [why this change/attempt]
**Result**: [outcome]
**Lesson**: [what we learned] (optional)
```

---

**Last Updated**: 2026-08-19  
**Next Review**: After Fix #1 implemented
