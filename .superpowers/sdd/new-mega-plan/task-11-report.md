# P1.6 — ISO Deletion Gating — Report

**Branch:** sacd-completion-v2 | **Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
**Date:** 2026-08-16 | **Status:** PASS (source + standalone checks), runtime integration BLOCKED

## Summary

`CleanupSuccesses` previously gated ISO deletion on directory existence alone (`outputsValidated`), with DFF/XML cleanup running unconditionally before the ISO check. A zero-length FLAC or missing CUE would not prevent source destruction. Both are now replaced by `ValidateOutputsForDeletion` which enforces: CUE present, CUE parseable, FLAC count equals CUE track count, every FLAC non-zero length. Validation runs on ALL output directories before ANY file deletion. `--keepIso` short-circuits before validation. Standalone check suite (6 P1.6 cases + 5 pre-existing guard cases) passes 11/11. Full runtime integration through `RunAsync` is BLOCKED pending P3.3/P5 harness.

## Subtask 1 — Require FLAC count equal to CUE track count

**Command:** `dotnet build Toolbox.slnx --no-restore --no-incremental` → 0 warnings, 0 errors.

**Diff (PipelineOrchestrator.cs L524-530):**
```csharp
var cueTrackCount = cueResult.Value.Tracks.Count;
var flacFiles = Directory.GetFiles(outputDir, "*.flac");
if (flacFiles.Length != cueTrackCount)
    return Error.Validation(
        "Audio.DeletionValidationFailed",
        $"FLAC count {flacFiles.Length} != CUE track count {cueTrackCount}"
    );
```

**Check output:**
```
Test 8: P1.6 — FLAC count mismatch blocks deletion... PASS
```

**Result: PASS**

## Subtask 2 — Require every FLAC non-zero length

**Diff (PipelineOrchestrator.cs L532-539):**
```csharp
foreach (var flac in flacFiles)
{
    if (new FileInfo(flac).Length == 0)
        return Error.Validation(
            "Audio.DeletionValidationFailed",
            $"Zero-length FLAC: {flac}"
        );
}
```

**Check output:**
```
Test 9: P1.6 — Zero-length FLAC blocks deletion... PASS
```

**Result: PASS**

## Subtask 3 — Require the CUE present

**Diff (PipelineOrchestrator.cs L510-522):**
```csharp
var cueFiles = Directory.GetFiles(outputDir, "*.cue");
if (cueFiles.Length == 0)
    return Error.Validation(
        "Audio.DeletionValidationFailed",
        $"No CUE file in {outputDir}"
    );

ErrorOr<CueSheet> cueResult = cueParser.Parse(cueFiles[0]);
if (cueResult.IsError)
    return Error.Validation(
        "Audio.DeletionValidationFailed",
        $"CUE parse failed: {cueResult.Errors[0].Description}"
    );
```

**Check output:**
```
Test 6: P1.6 — Missing CUE blocks deletion... PASS
Test 7: P1.6 — Bad CUE blocks deletion... PASS
```

**Result: PASS**

## Subtask 4 — Log validation outcome at Info before deletion decision

**Diff (PipelineOrchestrator.cs L448-461):**
```csharp
if (failureReason is not null)
{
    Telemetry.Info(
        "Pipeline.DeletionValidationFailed iso={Iso} reason={Reason}",
        LogPaths.Format(disc.IsoPath),
        failureReason
    );
    continue;
}

Telemetry.Info(
    "Pipeline.DeletionValidationPassed iso={Iso}",
    LogPaths.Format(disc.IsoPath)
);
```

**Verification:** Both `Telemetry.Info` calls execute before any `File.Delete` or `File.Exists`+`Delete` in the method. The `keepIso` path also logs `Pipeline.KeepIsoRetained` at Info (L430-433).

**Result: PASS**

## Subtask 5 — Confirm `--keep-iso` short-circuits regardless

**Diff (PipelineOrchestrator.cs L428-435):**
```csharp
if (keepIso)
{
    Telemetry.Info(
        "Pipeline.KeepIsoRetained iso={Iso}",
        LogPaths.Format(disc.IsoPath)
    );
    continue;
}
```

This is the FIRST check in the `CleanupSuccesses` loop body — before validation, before DFF/XML cleanup, before ISO deletion. The `continue` skips all remaining logic.

**Check output:**
```
Test 11: P1.6 — keepIso bypasses validation (code path)... PASS (short-circuit is before ValidateOutputsForDeletion in CleanupSuccesses)
```

**Result: PASS**

## Standalone check suite output

```
Test 1: Complete clears Failed... PASS
Test 2: Differing non-Complete verdict increments... PASS
Test 3: N=3 refuses attempt 4... PASS
Test 4: Alternating verdicts terminate... PASS
Test 5: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)
Test 6: P1.6 — Missing CUE blocks deletion... PASS
Test 7: P1.6 — Bad CUE blocks deletion... PASS
Test 8: P1.6 — FLAC count mismatch blocks deletion... PASS
Test 9: P1.6 — Zero-length FLAC blocks deletion... PASS
Test 10: P1.6 — Valid outputs pass validation... PASS
Test 11: P1.6 — keepIso bypasses validation (code path)... PASS

ALL CHECKS PASSED
```

**Command:** `dotnet run --project checks/GuardChecks.csproj` → exit 0, 11/11 PASS.

## Build evidence

```
dotnet build Toolbox.slnx --no-restore --no-incremental
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## BLOCKED — Runtime integration through `CleanupSuccesses`

`CleanupSuccesses` is a `private` method called only from `RunAsync` (L146). `RunAsync` requires: real ISO files, `sacd_extract` / `saracon` / `sox` on PATH, the full extraction-conversion-cleanup pipeline. The validation building blocks (CueParser + filesystem checks) are verified standalone. The integration of validation into `CleanupSuccesses` branching is verified by source inspection: `keepIso` short-circuits first, validation runs on all output directories, DFF/XML cleanup + ISO deletion execute only after validation passes.

**Blocker signature:** `private void CleanupSuccesses(List<ProcessedDisc>, bool)` is not callable outside `PipelineOrchestrator`. Full pipeline requires real ISO + external tools.
**Owner:** P3.3 (state matrix and guard termination) and P5.x (real media gates) will exercise this path end-to-end.

## Committed files

| Commit | File | Lines | Nature |
|---|---|---|---|
| `25c644b` | `src/Services/Audio/PipelineOrchestrator.cs` | +89 / −20 | `CleanupSuccesses` restructured; `ValidateOutputsForDeletion` added |
| `7b720cc` | `.superpowers/sdd/new-mega-plan/task-11-report.md` | +178 | This report |

## External verification artifact (not committed)

`checks/Program.cs` contains 6 P1.6 standalone validation checks (tests 6–11) exercising `CueParser` + filesystem logic against temp directories. This file is **not** in the package or any commit — it is a temporary throwaway check that ran against the `GuardChecks.csproj` project. The 11/11 output reproduced below is evidence of standalone verification, not a committed test deliverable.

## Concerns

1. **P1.5 dependency:** P1.6 gates on FLAC existence and non-zero length. P1.5 (split output verification) prevents zero-length FLAC creation at the split stage. Without P1.5, a zero-length FLAC could be created by a faulty split, and P1.6 would correctly block ISO deletion — but the disc would be stuck requiring manual intervention. P1.5 is prevention; P1.6 is safety net.

2. **CUE file location:** Validation searches `outputDir` (the `ProcessedDisc.OutputDirectories` entries). These are DFF directories — the same location where CUE files are extracted by `sacd_extract`. This matches the pipeline's CUE location.

3. **Multiple output directories:** A single ISO can produce multiple output directories (stereo + multichannel). Validation checks ALL directories pass before allowing deletion. If any directory fails, the ISO is retained.

4. **DFF/XML cleanup gating:** DFF/XML cleanup is now gated by the same validation as ISO deletion. Previously DFF/XML cleanup ran unconditionally when the output directory existed. If validation fails, intermediates are retained — preventing the scenario where intermediates are cleaned but ISO cannot be deleted, leaving the disc in a state requiring re-extraction.

---

## Fix round 1 — Report/package mismatch

**Prior text (lines 165–168):**
```
| `checks/Program.cs` | +120 (net) | 6 P1.6 validation checks added (tests 6–11) |
```

**Replacement:** Table split into "Committed files" and "External verification artifact" sections. `checks/Program.cs` explicitly labeled as temporary/uncommitted.

**Command:** `git diff HEAD -- .superpowers/sdd/new-mega-plan/task-11-report.md` confirms only the Changed-files section and fix-round appended.

**Result: PASS**
