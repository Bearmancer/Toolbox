# P1.5 - Split output verification

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

`SplitTrackAsync` returns on exit code alone; count check counts list entries, not files.

1. Confirm output file exists after exit-code check.
2. Confirm non-zero length.
3. Return descriptive `ConversionFailed` naming expected path when either fails.
4. Apply same check to `DeriveFlacAsync` and `ConvertDsdToFlacAsync`; record any other method returning unverified path.

Acceptance: stub exiting 0 writing nothing produces error; real Disc 3 split still succeeds.

Reporting: per subtask command/diff, raw output, PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-10-report.md`.
