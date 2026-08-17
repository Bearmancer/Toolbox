# P1.6 - ISO deletion gating

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

`outputsValidated` checks only directory existence. Require durable output validation before deleting only recoverable source.

1. Require FLAC count equal CUE track count.
2. Require every FLAC non-zero length.
3. Require CUE present.
4. Log validation outcome per disc at Info before deletion decision.
5. Confirm `--keep-iso` short-circuits regardless.

Acceptance: zero-length FLAC retains ISO with reason logged; valid disc without `--keep-iso` deletes ISO; `--keep-iso` retains in both cases.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-11-report.md`.
