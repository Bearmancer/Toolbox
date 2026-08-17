# P0.4 - Media risk inventory

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

1. Decode final-track duration for all 14 discs with CUEs using `sox --i -D`.
2. Flag any under 30 s; these trip the live rule today.
3. Record output-directory existence per ISO, separating fresh discs from re-processed ones.
4. Record CUE track count per disc as the Phase 5 expected-FLAC oracle.

Acceptance: per-disc table with all four columns, raw command output, and PASS/FAIL/BLOCKED per subtask. BLOCKED must quote signature and name owner. Do not mutate media. Write full report to `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\.superpowers\sdd\new-mega-plan\task-4-report.md`; return only status, commit, one-line test summary, concerns.
