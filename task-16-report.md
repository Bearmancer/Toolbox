# Task 16 — P3.1 Harness Infrastructure

**Branch:** sacd-completion-v2 | **Baseline:** ef43b65 | **R1:** 9c677c9 | **R2:** 2d3481b | **R2-fix:** 0096387 | **R3:** b45b769 | **R3-fix:** 1cdc80b
**Date:** 2026-08-17

## Summary

Durable P3.1 harness infrastructure. Committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard boundary check (parent-directory comparison) and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `checks/Program.cs` | 221 | R3: StartsWith separator boundary, finally reap-before-dispose |
| `checks/GuardChecks.csproj` | 13 | Unchanged (references Audio, no test packages) |
| `task-16-report.md` | — | This report (repo root) |

## Subtask Results

### 1. Plain `.cs` entry point, no test packages, referencing production project

GuardChecks.csproj unchanged — references `Audio.csproj` (transitively references `Core`). No xUnit/NUnit/MSTest. Top-level statements with `Main()` implicit.

### 2. Assertion helpers with failure output naming the case

`Assert(string name, bool condition, string? error)` records pass/fail with case name into `results` list. Output: `PASS: {name}` or `FAIL: {name} — {error}`.

### 3. Temp-workspace creation and teardown, hard assertion under system temp

```csharp
string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
```

**Hard boundary check (R3):** Ensures `systemTemp` ends with `Path.DirectorySeparatorChar`, then checks `normalizedTempRoot.StartsWith(systemTempWithSep, OrdinalIgnoreCase)` or exact equality. This rejects sibling directories (e.g. `Temp2` when temp is `Temp`) by requiring a separator after the system temp prefix. On mismatch: prints `FAIL: TempRootUnderSystemTemp` then throws `InvalidOperationException`. Teardown in `finally`: `Directory.Delete(tempRoot, true)`.

### 4. Controllable child-process stub

Self-invocation mode: `--stub --exit <code> --output <lines> --delay <ms> --ignore-termination`.

| Stub arg | Behavior |
|----------|----------|
| `--exit N` | Exit with code N |
| `--output N` | Print N lines of stdout |
| `--delay N` | Sleep N ms before exit |
| `--ignore-termination` | Wait forever (until killed) |

### 5. Nonzero exit on failure; per-case summary; Telemetry Fatal

`Telemetry.Configure(LogEventLevel.Fatal)` at entry. Exit code 1 if any case fails or `--force-fail` present. Summary: `RESULTS: X passed, Y failed, Z total`.

### 6. Fix R1: ChildStubIgnoreTermination try/finally

Process kill/reap/dispose wrapped in try/finally. Bounded wait: 5-second `CancellationTokenSource` on `WaitForExitAsync`.

### 7. Fix R2: Finally fallback kill awaits bounded reaping

If `Kill()` or `WaitForExitAsync` throws in the try block, the finally block kills (if not exited), then awaits bounded `WaitForExitAsync` (3s timeout). If the bounded reap times out, reports `FAIL: ChildStubIgnoreTermination — fallback kill timed out, possible orphan` and records named failure. Process always disposed.

## Raw Commands

### Clean run

```bash
dotnet run --project checks/GuardChecks.csproj
```

### Forced-failure run

```bash
dotnet run --project checks/GuardChecks.csproj -- --force-fail
```

## Raw Outputs

### Clean run (exit 0)

```
  PASS: TempRootUnderSystemTemp
  PASS: ChildStubExitZero
  PASS: ChildStubExitNonzero
  PASS: ChildStubOutputVolume
  PASS: ChildStubDelay
  PASS: ChildStubIgnoreTermination

RESULTS: 6 passed, 0 failed, 6 total
EXIT: 0
```

### Forced-failure run (exit 1)

```
  PASS: TempRootUnderSystemTemp
  PASS: ChildStubExitZero
  PASS: ChildStubExitNonzero
  PASS: ChildStubOutputVolume
  PASS: ChildStubDelay
  PASS: ChildStubIgnoreTermination
  FAIL: ForcedFailure — forced failure mode active

RESULTS: 6 passed, 1 failed, 7 total
EXIT: 1
```

## Build Verification

```
dotnet build checks/GuardChecks.csproj
  Core -> artifacts\bin\Core\debug\Core.dll
  Audio -> artifacts\bin\Audio\debug\Audio.dll
  GuardChecks -> artifacts\bin\GuardChecks\debug\GuardChecks.dll
Build succeeded. 0 Warning(s) 0 Error(s)
```

## Case Summary

| # | Case | Result |
|---|------|--------|
| 1 | TempRootUnderSystemTemp | PASS |
| 2 | ChildStubExitZero | PASS |
| 3 | ChildStubExitNonzero | PASS |
| 4 | ChildStubOutputVolume | PASS |
| 5 | ChildStubDelay | PASS |
| 6 | ChildStubIgnoreTermination | PASS |
| 7 | ForcedFailure (--force-fail only) | FAIL (expected) |

**6/6 PASS clean, 1/1 FAIL forced-failure. Exit 0 clean, exit 1 forced.**

## Acceptance Criteria

- [x] Harness runs, prints per-case results
- [x] Exits 0 clean
- [x] Exits non-zero when forced to fail
- [x] Committed to the repo, not deleted

## Fix Round History

| Round | Commit | Change | Prior stale value |
|-------|--------|--------|-------------------|
| R1 | 9c677c9 | Hard temp assert (throw), try/finally on child reaping | Soft Assert, no finally |
| R2 | 2d3481b | Parent-dir boundary check, finally bounded kill+reap+dispose | `StartsWith` prefix match, finally kills without await |
| R2-fix | 0096387 | Report SHA correction | R2 SHA pointed to wrong commit |
| R3 | b45b769 | StartsWith with separator boundary, finally reap-before-dispose | Parent-dir compare, dispose-before-reap |
| R3-fix | 1cdc80b | Report line count 221, SHA correction | R3 SHA pointed to wrong commit |

## Concerns

1. **Telemetry side effects:** `Configure(LogEventLevel.Fatal)` creates `state/logs/` directory and per-service JSONL files. Acceptable for on-demand harness.
2. **Per-service JSONL sinks** still capture Debug+ despite Fatal console level — by design per Telemetry.cs configuration.
3. **Finally bounded reap** uses 3s timeout. If a child process survives `Kill(entireProcessTree: true)` + 3s wait, a named failure is reported. No orphan claim without evidence.
