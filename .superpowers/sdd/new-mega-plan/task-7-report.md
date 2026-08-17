# P1.2 Task 7 Report — Reprocess Guard Semantics (Round 2)

## Subtask 1: Record reversal rationale for stickiness

**Rationale (plan-preserved historical evidence):**

> **Decision to reverse (T10.2):** `Failed` is sticky until manual JSON removal.

Source: `new-mega-plan.md` lines 35, 160. `task-10.2-report.md` artifact absent from worktree.

**Reversal:** `Failed` clearable by genuine `Complete` outcome (subtask 6). Manual JSON deletion replaced by `--reset-guard` (subtask 7).

**Status:** PASS

## Subtask 2: Record re-scoping rationale for off-by-one

**Rationale (plan-preserved historical evidence):**

> **Decision to re-scope (T10.3 finding #2):** transition fires before the Nth attempt, so N=3 yields two attempts.

Source: `new-mega-plan.md` line 162. `task-10.3-report.md` artifact absent.

> `task-10.3-report.md` review finding #2, severity Important: *"Transition must happen before processing"* → implemented as `c + 1 >= MaxConsecutiveCount` blocking before `ProbeAsync`. A reviewer asked for this.

Source: `new-mega-plan.md` line 36.

**Re-scoping (round 2 fix):** Guard now transitions to `Failed` when `newCount >= MaxConsecutiveCount` (i.e., after N records). For N=3: records 1–2 produce counts 1–2 (not Failed); record 3 produces count 3 ≥ 3 → `Failed`. The orchestrator's `Failed` early return (line 177–181) then refuses the 4th invocation before `ProbeAsync`. Reviewer requirement — *"a `Failed` disc starts no process"* — remains satisfied.

**Status:** PASS

## Subtask 3: Success paths record cycle outcome

**Files:** `PipelineOrchestrator.cs`

Success paths at lines 246 and 300 now record `DiscState.Complete` instead of `assessment.State`. The `assessment.State == Complete` path at line 208 already recorded `DiscState.Complete`.

**Status:** PASS

## Subtask 4: Count consecutive non-Complete regardless of verdict

**Files:** `ReprocessGuard.cs`

Count increments for every non-Complete record regardless of verdict. Oscillation terminates at N records.

**Status:** PASS

## Subtask 5: N attempts before blocking (round 2 fix)

**Files:** `ReprocessGuard.cs`, `PipelineOrchestrator.cs`

**Threshold fix:** Changed from `newCount > MaxConsecutiveCount` to `newCount >= MaxConsecutiveCount`.

For N=3: `RecordAsync` calls 1–2 produce counts 1–2 (not Failed). Call 3 produces count 3 ≥ 3 → `Failed`. Orchestrator's `Failed` check at line 177 refuses 4th invocation before `ProbeAsync`.

**Pipeline-level proof:** BLOCKED — requires P3.2 harness exercising orchestrator with real `ProcessIsoAsync` calls. Direct `RecordAsync` tests verify guard state only; they do not prove orchestrator refuses 4th process start. Owner: P3.2 harness.

**Status:** PASS (guard level), BLOCKED (pipeline level — P3.2 harness owner)

## Subtask 6: Complete clears Failed

**Files:** `ReprocessGuard.cs`

Sticky `Failed` early return removed. Complete removes entry regardless of prior state. Entry removal logged with prior verdict and count.

**Status:** PASS

## Subtask 7: Add --reset-guard to CLI

**Files:** `SacdConvertCommand.cs`

`--reset-guard` option added. Input changed from required `<input>` to optional `[input]`. Reset path calls `guard.ResetAllAsync(cancellationToken)` and returns 0.

**CLI help verification:** BLOCKED — `App.Main` requires `.env` (resolves to main repo via `PathResolver.RepoRoot`) and binaries on PATH (`sacd_extract`, `saracon`, `sox`). `AddAudioServices()` throws `InvalidOperationException` before CLI parser when binaries absent. Exit code 2, no output. Environment: worktree lacks `.env` and binaries. Owner: runtime environment setup.

**Status:** PASS (code), BLOCKED (CLI help output — environment)

## Subtask 8: Log every transition at Warn (round 2 fix)

**Files:** `ReprocessGuard.cs`

Every transition now logged with full metadata:

| Transition | Log template |
|---|---|
| Non-Complete ordinary | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) → {NewVerdict}({NewCount})` |
| Non-Complete → Failed | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) → Failed({NewCount})` |
| Complete (entry removed) | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) → Complete(0)` |
| Complete (no entry) | No log (no-op) |
| Single reset | `Guard reset: {ISO} {Verdict}({Count})` |
| Full reset | One `Guard reset: {ISO} {Verdict}({Count})` per entry, then clear |

Fields: ISO path, previous verdict, previous count, new verdict, new count. No aggregate-only logging.

**Status:** PASS

## Subtask 9: Resolve T10.3 kept-minor #7 duplicate Failed lookup

**Decision:** Kept with documented reason.

`Failed` check in `RunAsync` (line 88: quick skip) and `ProcessIsoAsync` (line 177–181: safety net). Defense-in-depth for future call paths.

**Status:** PASS

## Subtask 10: Atomic persistence, JsonException handling

**SaveAsync:** Write to `.tmp`, flush, close, `File.Move(overwrite: true)`. Interrupted write leaves original intact.

**LoadAsync:** `JsonException` propagates. Corrupt file not silently erased.

**Atomic interruption test:** BLOCKED — requires killing process mid-write and verifying file integrity. Cannot simulate in unit test. Owner: P3.2 integration harness.

**Status:** PASS (code), BLOCKED (interruption test — P3.2 harness)

## Subtask 11: Cancellation audit

**Round 2 fix:** Added `CancellationToken` parameter to `RecordAsync`, `ResetAsync`, `ResetAllAsync`, `SaveAsync`. All callers in `PipelineOrchestrator` now pass `ct`.

**Audit of `PipelineOrchestrator.ProcessIsoAsync`:** All 8 state-write paths follow:
```
ct.ThrowIfCancellationRequested();
await guard.RecordAsync(..., ct);
return ...;
```

No `RecordAsync`/`SaveAsync` after cancellation request. Verified by reading all return paths (lines 205–301).

**Cancellation race:** The guard's `SaveAsync` calls `File.Move` which is not atomic with respect to cancellation. If cancellation arrives between `FlushAsync` and `File.Move`, the `.tmp` file persists but the original is untouched. On next `LoadAsync`, the original file is read (still valid). The `.tmp` file is overwritten on next save. This is a benign race, not a data corruption path. Documented as acceptable.

**Status:** PASS

## Test outputs

### RED (before round 1 changes)
```
Test 1: Complete clears Failed... FAIL
Test 2: Differing non-Complete verdict increments... FAIL
Test 3: N=3 allows attempts 1-3, blocks 4... FAIL (premature Failed)
Test 4: Corrupt JSON does not reset to empty... FAIL (no exception)

4 CHECK(S) FAILED:
  FAIL: Complete clears Failed: entry still exists with verdict Failed
  FAIL: Differing verdict: count did not increment (1 -> 1)
  FAIL: N=3: transitioned to Failed before 3rd attempt
  FAIL: JsonException handling: should throw, not reset
```

### GREEN (after round 2 changes)
```
Test 1: Complete clears Failed... PASS
Test 2: Differing non-Complete verdict increments... PASS
Test 3: N=3 refuses attempt 4... PASS
Test 4: Alternating verdicts terminate... PASS
Test 5: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)

ALL CHECKS PASSED
```

### BLOCKED checks (require P3.2 harness or environment)
- **Pipeline-level attempt 4 refusal:** Guard `RecordAsync` verifies Failed after 3 records; orchestrator `Failed` check verifies refusal before `ProbeAsync`. But full integration (real `ProcessIsoAsync` → probe → extract → record cycle) requires P3.2 harness. Owner: P3.2.
- **CLI `--reset-guard --help` output:** `App.Main` requires `.env` and binaries on PATH. Exit code 2 without output in worktree environment. Owner: runtime env.
- **Atomic interruption file integrity:** Requires killing process mid-write. Owner: P3.2 integration.
- **Cancellation race under concurrent save:** Benign race documented; `.tmp` persists but original untouched. Cannot prove in unit test. Owner: P3.2.

## Guard state shape

```json
{
  "/path/to/disc.iso": {
    "Verdict": "NeedsExtraction",
    "ConsecutiveCount": 2,
    "UpdatedAt": "2026-08-16T18:00:00+00:00"
  }
}
```

Shape unchanged.

## Build

```
dotnet build Toolbox.slnx --no-restore --no-incremental

Build succeeded.
    0 Warning(s).
    0 Error(s).
```

## Changes (round 2)

| File | Change |
|---|---|
| `ReprocessGuard.cs` | `>` → `>=` threshold; Warn log on every transition (ISO/prev/new/count); per-entry reset logging; `CancellationToken` on RecordAsync/ResetAsync/ResetAllAsync/SaveAsync |
| `PipelineOrchestrator.cs` | Pass `ct` to all `RecordAsync` calls (CA2016) |
| `SacdConvertCommand.cs` | Pass `cancellationToken` to `ResetAllAsync` (CA2016) |
| `checks/Program.cs` | Test 3: verify Failed after 3 records (not after 4); Test 4: alternating verdicts terminate |

## Changes (round 3) — SaveAsync cancellation check

**Reviewer finding (Critical):** `File.Move` can execute after cancellation requested because no final `ThrowIfCancellationRequested` between stream close and atomic move.

**Fix:** Added `ct.ThrowIfCancellationRequested()` immediately after `stream.Close()` and before `File.Move(tempPath, StatePath, overwrite: true)`.

**Diff:**
```diff
 			await stream.FlushAsync(ct);
 			stream.Close();
+			ct.ThrowIfCancellationRequested();
 			File.Move(tempPath, StatePath, overwrite: true);
```

**Cancellation race after fix:** No deliberate `File.Move` after an observed cancellation. A cancellation arriving *during* `File.Move` (between OS kernel entry and completion) remains an OS atomic-operation boundary — the move either completes or doesn't, and the original file is untouched either way. This race is documented as BLOCKED/benign; it cannot be eliminated in userspace and does not corrupt state.

**Raw output:**
```
Test 1: Complete clears Failed... PASS
Test 2: Differing non-Complete verdict increments... PASS
Test 3: N=3 refuses attempt 4... PASS
Test 4: Alternating verdicts terminate... PASS
Test 5: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)

ALL CHECKS PASSED
```

**Status:** PASS

## Concerns

1. **Pipeline-level threshold proof BLOCKED:** Direct `RecordAsync` tests verify guard state; they do not prove orchestrator refuses 4th process start. P3.2 harness required.
2. **CLI help BLOCKED:** No `.env` or binaries in worktree environment. Startup throws before parser.
3. **Atomic interruption BLOCKED:** Cannot simulate in unit test.
4. **OS-level cancellation race (benign):** Cancellation during `File.Move` is an OS atomic-operation boundary. Move either completes or doesn't; original file untouched. Cannot be eliminated in userspace. Documented, not claimed impossible.
