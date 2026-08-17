# P1.3 - Last-track completeness rule

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

The 30-second failure remains live. No format requirement imposes minimum track length.

1. Replace `< 30.0` failure with `<= 0`.
2. Downgrade short-track observation to `Warn` with measured duration; do not fail completeness.
3. Confirm `else if` still fires only for final track; it is the `else` of `Duration is { }`, final CUE track `Duration` is null by construction.
4. Confirm non-final ±2.0 s tolerance untouched.

Acceptance: 20-second final track assesses Complete; 0-byte final track assesses non-Complete; non-final track off by 3 s assesses non-Complete.

Reporting: per subtask command/diff, raw observed output, PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-8-report.md`.
