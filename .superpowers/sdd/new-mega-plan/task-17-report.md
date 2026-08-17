# P3.2 — Regression-Suite Decontamination — Report

**Branch:** sacd-completion-v2 | **Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
**Date:** 2026-08-17 | **Status:** PASS

## Summary

Decontaminated the regression suite by:
1. Documenting absence of historical T11 report (collision note created)
2. Quoting blessed assertions verbatim from plan §0.2
3. Writing inverted assertions that validate P1.2 requirements
4. Adding `ProcessRunner.StartFailed` coverage
5. Resolving internal member access via reflection

## Historical Artifact Status

- **Historical T11 report:** ABSENT from repository
- **Root task-11-report.md:** P1.6 ISO deletion gating report (not historical T11)
- **Collision note:** Created at `checks/collision-note.md`

## Blessed Assertions (from plan §0.2)

The historical T11 harness asserted two defects as correct behavior:

1. **"Complete can't remove Failed (sticky)"** — `Failed` entries persisted regardless of subsequent `Complete` verdicts.
2. **"different verdict resets count"** — A change in verdict reset `ConsecutiveCount` to 0.

These are the two guard defects the compliance audit raised. The harness encoded them as expected behavior and passed.

## Inverted Assertions

### CompleteClearsFailed

**Requirement:** P1.2 — "Make `Failed` clearable by a genuine `Complete` outcome."
**Test:** Record three non-Complete verdicts (→ Failed), then record Complete. Assert entry is removed.
**Result:** PASS

### DifferingNonCompleteIncrements

**Requirement:** P1.2 — "Count consecutive non-`Complete` outcomes regardless of verdict, so oscillation terminates."
**Test:** Record NeedsExtraction (count=1), then NeedsPrimaryConversion (count=2). Assert count=2.
**Result:** PASS

## StartFailed Coverage

**Requirement:** T11 report noted `TerminationReason.StartFailed` as "never exercised."
**Test:** Call `ProcessRunner.RunAsync` with nonexistent binary. Assert `StartFailed` termination reason.
**Result:** PASS

## Internal Member Access

**Methods:** `FlacCompletenessChecker.GetFlacsByTrackNumber`, `FlacCompletenessChecker.FindDffDir`
**Visibility:** `internal static`
**Decision:** Reflection (not `InternalsVisibleTo`)
**Rationale:** Temp harness should not couple to production assembly via `InternalsVisibleTo`. Reflection provides access without modifying production code or assembly attributes.

## Case Citation Inventory

| Case | Citation | Status |
|------|----------|--------|
| TempRootUnderSystemTemp | P3.1 requirement: "hard assertion the path is under the system temp root" | PASS |
| ChildStubExitZero | P3.1 requirement: "Controllable child-process stub: configurable exit code" | PASS |
| ChildStubExitNonzero | P3.1 requirement: "Controllable child-process stub: configurable exit code" | PASS |
| ChildStubOutputVolume | P3.1 requirement: "Controllable child-process stub: output volume" | PASS |
| ChildStubDelay | P3.1 requirement: "Controllable child-process stub: delay" | PASS |
| ChildStubIgnoreTermination | P3.1 requirement: "mode ignoring termination" | PASS |
| ForcedFailure | P3.1 requirement: "Non-zero exit on any failure" | PASS |
| CompleteClearsFailed | P1.2 requirement: "Make `Failed` clearable by a genuine `Complete` outcome" | PASS |
| DifferingNonCompleteIncrements | P1.2 requirement: "Count consecutive non-`Complete` outcomes regardless of verdict" | PASS |
| ProcessRunnerStartFailed | T11 report: "`TerminationReason.StartFailed` as never exercised" | PASS |
| ReflectionAccess | P3.2 requirement: "Resolve the reflection dependency on `internal` members" | PASS |

## Build Evidence

```
dotnet build checks/GuardChecks.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Harness Execution Evidence

```
dotnet run --project checks/GuardChecks.csproj
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

RESULTS: 10 passed, 0 failed, 10 total
```

## Forced Failure Evidence

```
dotnet run --project checks/GuardChecks.csproj -- --force-fail
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
  FAIL: ForcedFailure — forced failure mode active

RESULTS: 10 passed, 1 failed, 11 total
```

## Committed Files

| Commit | File | Nature |
|--------|------|--------|
| `73d2859` | `checks/collision-note.md` | Historical T11 artifact collision note |
| `a7dbe97` | `checks/Program.cs` | P3.2 inverted assertions, StartFailed, reflection |
| (not committed) | `.superpowers/sdd/new-mega-plan/task-17-report.md` | This report (in .gitignore) |
