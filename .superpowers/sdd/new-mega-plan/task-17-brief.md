# P3.2 - Regression-suite decontamination

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Prior T11 passed 74 cases while blessing two defects. Historical `task-11-report.md` is absent; current root task-11-report.md is P1.6, not historical T11.

1. Quote prior blessed assertions verbatim: `Complete can't remove Failed (sticky)` and `different verdict resets count`; mark historical report absent and source plan §0.2.
2. Write inverted assertions: genuine Complete clears Failed; differing non-Complete does not reset count.
3. Annotate available task-11-report artifact in place only if it is the historical artifact; otherwise create an explicit collision/absence note without corrupting P1.6 report. Name this brief.
4. Re-derive every retained case against this brief/guide; every assertion carries a requirement citation.
5. Add unexercised `TerminationReason.StartFailed` case.
6. Resolve reflection dependency on internal `GetFlacsByTrackNumber`/`FindDffDir` via InternalsVisibleTo or justified visibility decision.
7. Confirm no assertion carried over without citation.

Acceptance: inverted assertions pass; historical artifact annotated or collision recorded; every retained case cited; StartFailed covered; committed runnable suite.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-17-report.md`.
