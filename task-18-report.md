# Task 18 — P3.3 State Matrix / Guard Termination

**Branch:** sacd-completion-v2 | **Baseline:** c559b62 | **Date:** 2026-08-17

## Summary

Eight requirement-cited cases for P3.3 state matrix and guard termination. Cases 1-4 exercise `FlacCompletenessChecker` internal static methods via reflection with synthetic temp fixtures. Cases 5-7 exercise `DiscOutputInspector` state outputs with synthetic DFF binary fixtures (no CUE path). Case 8 records the `PipelineOrchestrator` guard-skip seam and documents the BLOCKED integration reason (six concrete dependencies: `sacd_extract`, `saracon`, `sox` binaries absent; no ISO fixtures). Result: **17 PASS + 1 BLOCKED**. Clean 0, forced nonzero. Telemetry Fatal. Temp teardown in finally. No new null literals, no nullable-forgiving operators, no production source edits.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `checks/Program.cs` | 475 | +8 P3.3 cases, +BuildSyntheticDff helper, +blocked list, +usings |
| `task-18-report.md` | — | This report (repo root) |

## Harness Output

```
RESULTS: 17 passed, 0 failed, 1 blocked, 18 total
EXIT: 0
```

`--force-fail`: `RESULTS: 17 passed, 1 failed, 1 blocked, 19 total` → EXIT: 1 (forced nonzero verified).

## Subtask Results

### 1. P3.3.1 — GetFlacsByTrackNumber: empty directory

**Citation:** `FlacCompletenessChecker L108-122`
**Fixture:** Empty temp directory under `tempRoot/p331-empty-flacs`
**State Output:** `Dictionary<int, string>.Count == 0`
**Method:** Reflection (`BindingFlags.Static | BindingFlags.NonPublic`)
**Result:** PASS

### 2. P3.3.2 — GetFlacsByTrackNumber: numbered FLACs

**Citation:** `FlacCompletenessChecker L108-122, TrackNumberPattern L10-13`
**Fixture:** Temp dir with `01. First.flac`, `02. Second.flac`, `03. Third.flac`
**State Output:** `Dictionary.Count == 3`, keys `{1,2,3}` present
**Method:** Reflection
**Result:** PASS

### 3. P3.3.3 — FindDffDir: inner directory exists

**Citation:** `FlacCompletenessChecker L124-132`
**Fixture:** `channelDir/discName` exists as subdirectory
**State Output:** Returned path equals `Path.Combine(channelDir, discName)`
**Method:** Reflection
**Result:** PASS

### 4. P3.3.4 — FindDffDir: fallback to DFF file parent

**Citation:** `FlacCompletenessChecker L130-138`
**Fixture:** `channelDir/SomeSubdir/test.dff` exists; inner dir absent
**State Output:** Returned path equals `SomeSubdir` parent
**Method:** Reflection
**Result:** PASS

### 5. P3.3.5 — DiscOutputInspector: no cue, no DFF → NeedsExtraction

**Citation:** `DiscOutputInspector L26-77`
**Fixture:** Empty `channelDir/discName` directory
**State Output:** `State=NeedsExtraction`, `CueTrackCount=0`, `PrimaryFlacCount=0`
**Fixture Ownership:** Synthetic temp; no media mutation
**Result:** PASS

### 6. P3.3.6 — DiscOutputInspector: no cue, invalid DFF → NeedsExtraction

**Citation:** `DiscOutputInspector L47-59, L64-77`
**Fixture:** `garbage.dff` (3 bytes: `0xFF 0xFE 0xFD`) — not FRM8 magic
**State Output:** `State=NeedsExtraction`, `CueTrackCount=0`
**Fixture Ownership:** Synthetic temp; no media mutation
**Result:** PASS

### 7. P3.3.7 — DiscOutputInspector: no cue, valid DFF header → InvalidArtifacts

**Citation:** `DiscOutputInspector L50-59, L71-72`
**Fixture:** Synthetic 62-byte DFF binary (FRM8 + DSD + PROP/SND + FS@2822400Hz + CHNL@2ch)
**State Output:** `State=InvalidArtifacts`, `CueTrackCount=0`
**Fixture Ownership:** Synthetic temp; binary header constructed in `BuildSyntheticDff()`; no media mutation
**Result:** PASS

### 8. P3.3.8 — PipelineOrchestrator guard skip: BLOCKED

**Citation:** `PipelineOrchestrator L8-15, L84-97`
**Recorded Signature:**
```
PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)
```
**Guard Verification:** Structural record only — no `ReprocessGuard` invocation in harness. Guard semantics (transitions, consecutive count, Failed sticky, Complete clears) already unit-tested by P1.2 `CompleteClearsFailed` and `DifferingNonCompleteIncrements`.
**Integration BLOCKED Reason:**
1. `SacdExtractService` requires `sacd_extract` binary (not in harness PATH)
2. `DsdConvertService` requires `saracon` binary (not in harness PATH)
3. `DsdConvertService` requires `sox` binary (not in harness PATH)
4. `PipelineOrchestrator.RunAsync` requires valid ISO file fixture
5. `DiskSpaceChecker` requires real filesystem with sufficient space
6. No mock/stub seam in production orchestrator — 6 concrete constructor dependencies

**Gap:** Integration test of guard-skip-through-orchestrator requires full SACD toolchain. P1.2 semantics (guard transitions, consecutive count, Failed sticky, Complete clears) already covered by `CompleteClearsFailed` and `DifferingNonCompleteIncrements`. Pipeline-level guard skip at L84-97 is structural delegation to `ReprocessGuard.Get()` — already tested.
**Result:** BLOCKED (documented)

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
| P3.3.8 | none (BLOCKED — no fixture) | n/a |

All fixtures under system temp with hard boundary check (P3.1 R3). No ISO/media mutation. Synthetic DFF constructed from binary header spec — no external audio files.

## Null/Bang Audit

- **0** new `null` literals introduced
- **0** new nullable-forgiving `!` operators
- **0** new `as any` / unsafe casts
- Existing legacy null/bang in production code unaltered
- Reflection results handled via `raw is Type variable` pattern matching

## Build

```
dotnet build checks/GuardChecks.csproj → succeeded (0 warnings, 0 errors)
dotnet run (clean) → RESULTS: 17 passed, 0 failed, 1 blocked, 18 total → EXIT: 0
dotnet run -- --force-fail → RESULTS: 17 passed, 1 failed, 1 blocked, 19 total → EXIT: 1
```
