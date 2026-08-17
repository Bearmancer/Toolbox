# P1.4 - Split error capture

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Bare `continue` destroys sox stderr at production moment.

1. Capture failures into `Dictionary<int, string>` keyed by track number.
2. Log each failure at Warn with track number, output path, error text.
3. Include per-track reasons in aggregate error.
4. Confirm aggregate still names missing track numbers.
5. Confirm mid-loop failure does not prevent remaining tracks being attempted.

Acceptance: injected failure on track 7 of 19 produces Warn naming track 7 and stderr; aggregate error carries list and reasons.

Reporting: per subtask command/diff, raw output, PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-9-report.md`.
