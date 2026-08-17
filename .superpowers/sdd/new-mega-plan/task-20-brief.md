# P3.5 - ProcessRunner termination suite

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Extend committed `checks/` harness. New code: no null literals, nullable-forgiving `!`, or speculative production APIs.

1. Exit 0 -> `Exited`, code 0, full stdout captured.
2. Exit 3 -> `Exited`, code 3 preserved, stderr captured.
3. Caller cancellation -> `CallerCanceled`, tree killed/reaped, no orphan.
4. Wall-clock timeout -> `Timeout`, killed/reaped, code not laundered to 0.
5. Completion marker then hang -> `KilledAfterCompletionMarker`, killed/reaped; caller still validates output.
6. High-volume stdout then immediate exit -> drain barrier holds; output complete.

Acceptance: all six; no killed process returns exit 0; no orphan remains. Runtime process checks must be observed, not inferred from source.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. Write report to sibling `task-20-report.md`.
