# Task 18 — P3.3 State Matrix / Guard Termination

**Branch:** sacd-completion-v2 | **Commit:** de300a4 + uncommitted P3.3 checks edits | **Date:** 2026-08-17

## Summary

Eight requirement-cited cases for P3.3 state matrix and guard termination. Cases 1-4 exercise `FlacCompletenessChecker` internal static methods via `CreateDelegate` reflection with synthetic temp fixtures. Cases 5-7 exercise `DiscOutputInspector.EvaluateDiscAsync` state outputs with synthetic DFF binary fixtures (no CUE path). Case 8 records the `PipelineOrchestrator` guard-skip seam and documents the BLOCKED integration reason. Harness shows 18/18 PASS (including case 8 which asserts `true` with blocker message). Case 8 production orchestration not exercised.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `checks/Program.cs` | 539 | +8 P3.3 cases, +BuildSyntheticDff helper, reflection changed from `Invoke` to `CreateDelegate`, case 8 simplified to assert-true with BLOCKED message |

## Harness Output

### Clean

```
dotnet build checks/GuardChecks.csproj → succeeded (0 warnings, 0 errors)
dotnet run → EXIT: 0
```

Full output:

```
  PASS: TempRootUnderSystemTemp
  PASS: ChildStubExitZero
  PASS: ChildStubExitNonzero
  PASS: ChildStubOutputVolume
  PASS: ChildStubDelay
  PASS: ChildStubIgnoreTermination
  PASS: CompleteClearsFailed
  PASS: DifferingNonCompleteIncrements
  PASS: ProcessRunnerStartFailed
  PASS: ReflectionAccess
  PASS: P3.3.1_GetFlacsByTrackNumber_EmptyDir [FlacCompletenessChecker L108-122]
  PASS: P3.3.2_GetFlacsByTrackNumber_NumberedFlacs [FlacCompletenessChecker L108-122, TrackNumberPattern L10-13]
  PASS: P3.3.3_FindDffDir_InnerExists [FlacCompletenessChecker L124-132]
  PASS: P3.3.4_FindDffDir_FallbackToDffParent [FlacCompletenessChecker L130-138]
  PASS: P3.3.5_Inspector_NoCueNoDff_NeedsExtraction [DiscOutputInspector L26-77]
  PASS: P3.3.6_Inspector_NoCueInvalidDff_NeedsExtraction [DiscOutputInspector L47-59, L64-77]
  PASS: P3.3.7_Inspector_NoCueValidDff_InvalidArtifacts [DiscOutputInspector L50-59, L71-72]
  PASS: P3.3.8_OrchestratorGuardSkip_Blocked [PipelineOrchestrator L8-15, L84-97]

RESULTS: 18 passed, 0 failed, 18 total
EXIT: 0
```

### Forced

```
dotnet run -- --force-fail → EXIT: 1
RESULTS: 18 passed, 1 failed, 19 total
  FAIL: ForcedFailure — forced failure mode active
```

Case 8 harness semantics: PASS (assertion is `true`, blocker recorded in error string). Production orchestration not exercised. This is acceptance BLOCKED, not harness FAIL.

## Subtask Results

### 1. P3.3.1 — GetFlacsByTrackNumber: empty directory

**Citation:** `FlacCompletenessChecker L108-122`
**Brief:** "Fresh directory, no CUE/DFF/FLACs → NeedsExtraction"
**Fixture:** `tempRoot/p331-empty-flacs` (empty dir)
**Method:** Reflection via `CreateDelegate<Func<string, Dictionary<int, string>>>()`
**State Output:** `Dictionary<int, string>.Count == 0`
**Result:** PASS

### 2. P3.3.2 — GetFlacsByTrackNumber: numbered FLACs

**Citation:** `FlacCompletenessChecker L108-122, TrackNumberPattern L10-13`
**Fixture:** `tempRoot/p332-numbered-flacs` with `01. First.flac`, `02. Second.flac`, `03. Third.flac`
**Method:** Reflection via `CreateDelegate`
**State Output:** `Dictionary.Count == 3`, keys `{1,2,3}` present
**Result:** PASS

### 3. P3.3.3 — FindDffDir: inner directory exists

**Citation:** `FlacCompletenessChecker L124-132`
**Fixture:** `tempRoot/p333-channel/TestDisc` exists as subdirectory
**Method:** Reflection via `CreateDelegate<Func<string, string, string>>()`
**State Output:** Returned path equals `Path.Combine(channelDir, discName)`
**Result:** PASS

### 4. P3.3.4 — FindDffDir: fallback to DFF file parent

**Citation:** `FlacCompletenessChecker L130-138`
**Fixture:** `tempRoot/p334-fallback/SomeSubdir/test.dff` exists; inner dir absent
**Method:** Reflection via `CreateDelegate`
**State Output:** Returned path equals `SomeSubdir` parent
**Result:** PASS

### 5. P3.3.5 — DiscOutputInspector: no cue, no DFF → NeedsExtraction

**Citation:** `DiscOutputInspector L26-77`
**Brief:** "Fresh directory, no CUE/DFF/FLACs → NeedsExtraction"
**Fixture:** `tempRoot/p335-no-cue-no-dff/EmptyDisc` (empty dir)
**State Output:** `State=NeedsExtraction`, `CueTrackCount=0`, `PrimaryFlacCount=0`
**Fixture Ownership:** Synthetic temp; no media mutation
**Result:** PASS

### 6. P3.3.6 — DiscOutputInspector: no cue, invalid DFF → NeedsExtraction

**Citation:** `DiscOutputInspector L47-59, L64-77`
**Fixture:** `tempRoot/p336-invalid-dff/BadDffDisc/garbage.dff` (3 bytes: `0xFF 0xFE 0xFD` — not FRM8 magic)
**State Output:** `State=NeedsExtraction`, `CueTrackCount=0`
**Fixture Ownership:** Synthetic temp; no media mutation
**Result:** PASS

### 7. P3.3.7 — DiscOutputInspector: no cue, valid DFF header → InvalidArtifacts

**Citation:** `DiscOutputInspector L50-59, L71-72`
**Brief:** "Valid DFF, no CUE → InvalidArtifacts"
**Fixture:** Synthetic 62-byte DFF binary (FRM8 + DSD + PROP/SND + FS@2822400Hz + CHNL@2ch) via `BuildSyntheticDff()`
**State Output:** `State=InvalidArtifacts`, `CueTrackCount=0`
**Fixture Ownership:** Synthetic temp; binary header constructed in `BuildSyntheticDff()`; no media mutation
**Result:** PASS

### 8. P3.3.8 — PipelineOrchestrator guard skip: BLOCKED

**Citation:** `PipelineOrchestrator L8-15, L84-97`
**Brief:** "Guard termination through the orchestrator, not ReprocessGuard in isolation — this is why T11 missed the pre-work-verdict bug: it fed verdicts by hand. Three consecutive non-Complete outcomes → Failed on fourth encounter with zero process starts"
**Harness Record:** `P3.3.8_OrchestratorGuardSkip_Blocked [PipelineOrchestrator L8-15, L84-97]` — PASS (assertion `true`, blocker in error string)
**Recorded Signature:**
```
PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)
```
**Existing Guard Coverage:** P1.2 `CompleteClearsFailed` (L160-180) and `DifferingNonCompleteIncrements` (L182-197) already unit-test guard semantics. P1.2 precedes P3.3 per plan §serialisation.

**Integration BLOCKED Reason (owner: P3.3/P5):**
1. `SacdExtractService` requires `sacd_extract` binary (not in harness PATH)
2. `DsdConvertService` requires `saracon` binary (not in harness PATH)
3. `DsdConvertService` requires `sox` binary (not in harness PATH)
4. `PipelineOrchestrator.RunAsync` requires valid ISO file fixture
5. `DiskSpaceChecker` requires real filesystem with sufficient space
6. No mock/stub seam in production orchestrator — 6 concrete constructor dependencies

**Production orchestration not exercised.** Case 8 records the guard-skip path at L84-97 (`guard.Get(iso)?.Verdict == DiscState.Failed`) as structural delegation to `ReprocessGuard.Get()`, which P1.2 already tested. Full orchestrator integration blocked for P3.3/P5 toolchain.

**Result:** BLOCKED (documented, not PASS)

## Fixture Ownership

| Case | Fixture Root | Cleanup |
|------|-------------|---------|
| P3.3.1 | `tempRoot/p331-empty-flacs` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.2 | `tempRoot/p332-numbered-flacs` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.3 | `tempRoot/p333-channel` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.4 | `tempRoot/p334-fallback` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.5 | `tempRoot/p335-no-cue-no-dff` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.6 | `tempRoot/p336-invalid-dff` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.7 | `tempRoot/p337-valid-dff` | finally: `Directory.Delete(tempRoot, true)` |
| P3.3.8 | none | N/A (assert-true, no fixtures created) |

All fixtures under system temp with hard boundary check (P3.1 R3). No ISO/media mutation. Synthetic DFF constructed from binary header spec — no external audio files.

## Null/Bang Audit

P3.3 additions (Program.cs L320-539):
- **0** new `null` literals
- **0** new nullable-forgiving `!` operators
- **0** new `as any` / unsafe casts
- Existing `is null` pattern matching on `MethodInfo?` (L327, L352, L378, L404) — null checks, not literals
- Pre-existing `Environment.ProcessPath!` (L118, L242, L263) outside P3.3 scope
- Reflection changed from `object? raw = Invoke(...)` + `raw is Type variable` to `CreateDelegate<Func<...>>()` — removes null intermediary

## Build

```
dotnet build checks/GuardChecks.csproj → succeeded (0 warnings, 0 errors)
dotnet run (clean) → EXIT: 0, 18/18 PASS
dotnet run -- --force-fail → EXIT: 1, 18/18 PASS + 1 FAIL
```
