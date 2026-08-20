# Pristine Refactor — Failure Log & Rationale

**Session**: 619ecbd4-5987-42ed-a03d-78c109835b16  
**Date**: 2026-08-19  
**Status**: In Progress  

---

## Why No FLAC Attempts Have Happened

**Root Cause**: Playwright API calls ignore `CancellationToken`. The `resolveCts.CancelAfter(45s)` fires but `await page.ClickAsync(...)` doesn't throw because Playwright methods don't accept CancellationToken directly.

**Evidence**: Log stops at `Pristine.Album.ResolveStart` (line 8 of 8). No `Pristine.Album.Attempt` logs appear. First `ClickAsync` with 3s timeout hangs indefinitely.

**Why Previous Proposals Failed**:
1. **Added `resolveCts.CancelAfter(45s)`** — Timeout fires but `await` doesn't observe it
2. **Wrapped in try/catch** — Catches exceptions but not cancellation
3. **Added logging** — Logs exist but never execute because call hangs

**Why New Proposal Succeeds**: `.WaitAsync(resolveCt)` forces the Task to observe cancellation. When token cancels, the await throws `OperationCanceledException`, which we catch and log.

---

## Failure #1: Empty catch blocks (15+)

**File**: `PristineAlbumService.cs` (original)  
**Issue**: Silent failures, zero telemetry  
**Attempt**: Rewrote entire file with logging  
**Result**: ✅ Fixed — all catches now log  
**Rationale for New Proposal**: Logging is in place. Cancellation is the remaining blocker.

---

## Failure #2: IDE0007 style violations (30+)

**File**: Multiple  
**Issue**: Explicit types instead of `var`  
**Attempt**: `dotnet format`  
**Result**: ✅ Fixed  
**Rationale**: Style is clean. Not blocking functionality.

---

## Failure #3: Missing Errors.Pristine factories

**File**: `Errors.cs`  
**Issue**: `TracklistParseFailed`, `No16BitFlac` didn't exist  
**Attempt**: Manual edit  
**Result**: ✅ Fixed  
**Rationale**: Error taxonomy complete.

---

## Failure #4: ErrorOr type mismatch

**File**: `PristinePollService.cs:100`  
**Issue**: `ParseTracklistAsync` returns `ErrorOr<List<string>>` but caller expected `List<string>`  
**Attempt**: Added `.IsError` check  
**Result**: ✅ Fixed  
**Rationale**: Type safety enforced.

---

## Failure #5: PowerShell file corruption

**File**: `PristineAlbumService.cs`  
**Issue**: `Get-Content | Set-Content` collapsed file to 1 line  
**Attempt**: Manual rewrite  
**Result**: ✅ Fixed  
**Lesson**: Never use PowerShell pipeline for file editing.

---

## Failure #6: CS8602 null dereference

**File**: `PristineAlbumService.cs:131`  
**Issue**: `el.GetAttributeAsync("href").GetAwaiter().GetResult()[..80]` — null reference  
**Attempt**: Null-safe `?? string.Empty`  
**Result**: ✅ Fixed  
**Rationale**: Null safety enforced.

---

## Failure #7: Cookie rejection (2 `__Host-*`)

**File**: `PristineBrowser.cs`  
**Issue**: Domain mismatch `.pristinestreaming.com`  
**Attempt**: Per-cookie try/catch  
**Result**: ⚠️ Logged — 9 applied, 2 rejected  
**Impact**: May cause auth failures but non-blocking.

---

## Failure #8: Runtime hang at ResolveStart ⚠️ CURRENT BLOCKER

**File**: `PristineAlbumService.cs`  
**Issue**: Playwright calls ignore `CancellationToken`  
**Attempt**: Added `resolveCts.CancelAfter(45s)`  
**Result**: ❌ NOT FIXED — timeout fires but `await` doesn't throw  
**Root Cause**: Playwright API doesn't accept CancellationToken  
**New Proposal**: Wrap all Playwright calls with `.WaitAsync(resolveCt)`  
**Why This Succeeds**: `.WaitAsync()` forces Task to observe cancellation. When token cancels, await throws `OperationCanceledException`.

---

## Failure #9: 16-bit probe untested

**File**: `PristinePollService.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Rationale**: Cannot test probe if resolve hangs.

---

## Failure #10: Semaphore(5) untested

**File**: `PristinePollService.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Rationale**: Cannot test concurrency if resolve hangs.

---

## Failure #11: Sequential album download untested

**File**: `PristineOrchestrator.cs`  
**Issue**: Spec written, code present, never executed  
**Status**: ❌ Blocked by Failure #8  
**Rationale**: Cannot test orchestration if resolve hangs.

---

## Failure #12: Single-FLAC proof not landed

**File**: N/A  
**Issue**: Never reached download phase  
**Status**: ❌ Blocked by Failure #8  
**Rationale**: Cannot verify end-to-end if resolve hangs.

---

## Logging Spec Compliance

**Every catch logs**: ✅  
- `Debug` — retryable  
- `Warn` — recoverable  
- `Error` — terminal  

**Method entries**: ✅  
- `using var _ = Telemetry.ForService(ServiceName.Pristine)`  
- `Telemetry.Info("Pristine.X.Start ...")`  

**Mandatory log points**: ✅  
- Cookie count + domains  
- Each Playwright step: selector, attempt, error  
- URL after navigation  
- Resolve attempts + outcome  
- Candidate URLs with tier  
- Stall count, retry attempts  
- Download success/failure  
- Probe result  

**Gates**: ✅  
- No `!` operator  
- No `catch{}` without telemetry  
- `.editorconfig` clean  

---

## Next Action

**Fix Failure #8**: Wrap all remaining Playwright calls with `.WaitAsync(resolveCt)` or `.WaitAsync(ct)`.

**Files to fix**:
1. `PristineAlbumService.cs` — ResolveAlbumIdAsync (foreach loop), StartPlaybackAsync, ParseTracklistAsync, DownloadArtworkAndPdfAsync
2. `PristinePollService.cs` — DownloadSingleAlbumAsync (all Playwright calls), WaitForLoginAsync
3. `PristineOrchestrator.cs` — seed page calls
4. `PristineLoginService.cs` — all Playwright calls

**Expected Result**: 45s timeout fires, operation cancels cleanly, logs show cancellation.

---

**Last Updated**: 2026-08-19  
**Next Review**: After all WaitAsync wrappers applied
