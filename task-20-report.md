# Task 20 — P3.5 ProcessRunner Termination Cases

**Branch:** sacd-completion-v2 | **Baseline:** 335468d | **Date:** 2026-08-17

## Summary

Six requirement-cited cases for P3.5 `ProcessRunner` termination reasons via real `ProcessRunner.RunAsync` against self-stub. Cases 1-2 exercise normal exit with stdout/stderr capture. Case 3 exercises caller cancellation via pre-cancelled `CancellationToken`. Case 4 exercises timeout termination. Case 5 exercises completion marker detection with grace kill. Case 6 exercises high-volume stdout drain. Stub extended with `--stderr N` and `--complete-after N` modes. Result: **29 PASS + 2 BLOCKED** (6 new P3.5 all PASS). Clean 0, forced nonzero. Telemetry Fatal. Temp teardown in finally. No new null literals, no nullable-forgiving operators, no production source edits. 3 legacy `Environment.ProcessPath!` sites replaced with `string?` + `is null` guards; switch case indent corrected.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `checks/Program.cs` | 1089 | +6 P3.5 cases, +2 stub modes (`--stderr`, `--complete-after`), +6 case method invocations, 3 legacy `Environment.ProcessPath!` → `string?` + `is null` guards, switch case indent fix |
| `task-20-report.md` | — | This report (repo root) |

## Harness Output

```
RESULTS: 29 passed, 0 failed, 2 blocked, 31 total
EXIT: 0
```

`--force-fail`: `RESULTS: 29 passed, 1 failed, 2 blocked, 32 total` → EXIT: 1 (forced nonzero verified).

## Subtask Results

### 1. P3.5.1 — Exit0 With Stdout Capture

**Citation:** `ProcessRunner.RunAsync, TerminationReason.Exited`
**Fixture:** Self-stub: `--stub --exit 0 --output 10`
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--output", "10"], CancellationToken.None)`
**Expected:** ExitCode=0, TerminationReason=Exited, Stdout contains 10 lines (`stub-output-0` through `stub-output-9`)
**Assertions:**
- `result.IsError` is false
- `result.Value.ExitCode == 0`
- `result.Value.TerminationReason == TerminationReason.Exited`
- Stdout line count == 10
**Orphan Check:** ProcessRunner completes normally → child reaped via `KillAndReapAsync` in `stopAndBuildAsync` (not needed for exit-0 path, `DrainOutputAsync` reaps)
**Result:** PASS

### 2. P3.5.2 — Exit3 With Stderr Capture

**Citation:** `ProcessRunner.RunAsync, TerminationReason.Exited, stderr`
**Fixture:** Self-stub: `--stub --exit 3 --stderr 5`
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "3", "--stderr", "5"], CancellationToken.None)`
**Expected:** ExitCode=3, TerminationReason=Exited, Stderr contains 5 lines (`stub-stderr-0` through `stub-stderr-4`)
**Assertions:**
- `result.IsError` is false
- `result.Value.ExitCode == 3`
- `result.Value.TerminationReason == TerminationReason.Exited`
- Stderr line count == 5
**Orphan Check:** Same as case 1 — normal exit path, drain reaps
**Result:** PASS

### 3. P3.5.3 — Caller Cancellation

**Citation:** `ProcessRunner.RunAsync, TerminationReason.CallerCanceled`
**Fixture:** Self-stub: `--stub --exit 0 --delay 10000` (long delay, never reached)
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--delay", "10000"], cts.Token)` with pre-cancelled token via `await cts.CancelAsync()`
**Expected:** `ProcessRunnerCanceledException` thrown, `ex.Result.TerminationReason == TerminationReason.CallerCanceled`
**Assertions:**
- Exception caught is `ProcessRunnerCanceledException`
- `ex.Result.TerminationReason == TerminationReason.CallerCanceled`
**Orphan Check:** `stopAndBuildAsync` kills and reaps before throwing; `KillAndReapAsync` calls `process.Kill(entireProcessTree: true)` then `DrainOutputAsync`
**Result:** PASS

### 4. P3.5.4 — Timeout

**Citation:** `ProcessRunner.RunAsync, TerminationReason.Timeout`
**Fixture:** Self-stub: `--stub --exit 0 --delay 10000` (10s delay, exceeds 200ms timeout)
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--delay", "10000"], CancellationToken.None, timeout: TimeSpan.FromMilliseconds(200))`
**Expected:** `result.Value.TerminationReason == TerminationReason.Timeout`
**Assertions:**
- `result.IsError` is false
- `result.Value.TerminationReason == TerminationReason.Timeout`
**Orphan Check:** `stopAndBuildAsync` kills and reaps; `KillAndReapAsync` ensures process exited before return
**Result:** PASS

### 5. P3.5.5 — Completion Marker Hang

**Citation:** `ProcessRunner.RunAsync, TerminationReason.KilledAfterCompletionMarker`
**Fixture:** Self-stub: `--stub --complete-after 100` (outputs "DONE" after 100ms, then hangs forever)
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--complete-after", "100"], CancellationToken.None, completionPattern: "DONE", completionTimeout: TimeSpan.FromMilliseconds(200))`
**Expected:** ProcessRunner detects "DONE" in stdout, starts 200ms grace timer, kills after grace → `TerminationReason.KilledAfterCompletionMarker`
**Assertions:**
- `result.IsError` is false
- `result.Value.TerminationReason == TerminationReason.KilledAfterCompletionMarker`
**Orphan Check:** Grace expiry triggers `stopAndBuildAsync(KilledAfterCompletionMarker)`, `KillAndReapAsync` ensures clean termination
**Result:** PASS

### 6. P3.5.6 — High-Volume Stdout Drain

**Citation:** `ProcessRunner.RunAsync, output drain`
**Fixture:** Self-stub: `--stub --exit 0 --output 1000` (1000 lines to stdout)
**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--output", "1000"], CancellationToken.None)`
**Expected:** ExitCode=0, TerminationReason=Exited, Stdout contains all 1000 lines
**Assertions:**
- `result.IsError` is false
- `result.Value.ExitCode == 0`
- `result.Value.TerminationReason == TerminationReason.Exited`
- Stdout line count == 1000
**Orphan Check:** Normal exit path, `DrainOutputAsync` waits for `stdoutDrainTcs` and `stderrDrainTcs` before return
**Result:** PASS

## Stub Extensions

Two new modes added to `RunStubAsync`:

| Mode | Effect |
|------|--------|
| `--stderr N` | Write N lines to `Console.Error` (stderr) before delay logic |
| `--complete-after N` | Wait N ms, write "DONE" to stdout, then hang forever (priority over `--delay` and `--ignore-termination`) |

Existing modes (`--exit`, `--output`, `--delay`, `--ignore-termination`) unaltered. All prior P3.1-P3.4 cases pass unchanged.

## ProcessRunner API Coverage

| TerminationReason | Case | Verified |
|---|---|---|
| `Exited` | P3.5.1, P3.5.2, P3.5.6 | ExitCode + stdout/stderr capture |
| `CallerCanceled` | P3.5.3 | Pre-cancelled token → exception |
| `Timeout` | P3.5.4 | 200ms timeout on 10s delay |
| `KilledAfterCompletionMarker` | P3.5.5 | Pattern detected → grace kill |
| `InactivityTimeout` | — | No caller passes `inactivityTimeout`; latent per C-11 |
| `StartFailed` | ProcessRunnerStartFailed (P3.1) | Pre-existing case |

`InactivityTimeout` not exercised — no production caller passes the parameter; testing it would require ProcessRunner API modification which is out of scope.

## Null/Bang Audit

- **0** new `null` literals introduced
- **0** new nullable-forgiving `!` operators
- **0** new `null!` assignments
- `string? exePath = Environment.ProcessPath` used with `is null` guard in all nine methods (6 P3.5 + 3 legacy spawn sites)
- 3 legacy `Environment.ProcessPath!` sites (`ChildStubIgnoreTerminationAsync`, `SpawnStubAsync`, `SpawnStubWithOutputAsync`) replaced with `string?` + `is null` guards; **0** `Environment.ProcessPath!` remain
- Boolean negation uses prefix `!` on `bool` values (not nullable-forgiving): `!result.IsError`
- Pattern matching: `exePath is null`, `result.IsError`

## Build

```
dotnet build checks/GuardChecks.csproj → succeeded (0 warnings, 0 errors)
dotnet run (clean) → RESULTS: 29 passed, 0 failed, 2 blocked, 31 total → EXIT: 0
dotnet run -- --force-fail → RESULTS: 29 passed, 1 failed, 2 blocked, 32 total → EXIT: 1
```
