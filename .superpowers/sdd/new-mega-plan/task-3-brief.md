# P0.3 - Falsified-completion audit

Re-derive T1-T11 claims against current source and runtime evidence. Produce table with claim, source location, and exactly one of `CONFIRMED`, `FALSE`, `PARTIAL`, `STATIC-ONLY`. Every `FALSE`, `PARTIAL`, or `STATIC-ONLY` row maps to a later task ID in `new-mega-plan.md`.

Audit groups:

1. T1: sink at `state/logs`; file sub-logger explicitly Verbose and not shadowed by root `LevelSwitch`; run one command from `C:\Users\Lance`; record mangled temp-root label and Seq-sink level deferral.
2. T3: rejection of `24`/`both`; `ForDsdRate` intact; `dsd-convert` builds and runs; mark never-run media conversion `STATIC-ONLY`.
3. T4: copy-16, `ckDataSize` rewrite, read-back verify, `finally` cleanup, `PROP` descent; enumerate every reachable `throw` and whether callers catch it.
4. T6/T7: six `TerminationReason` values; no killed process returns 0; abnormal paths reap; `inactivityCts` disposed; estimator receives probed rate/channels; mark unexecuted real conversion `STATIC-ONLY`.
5. T8/T9: gain probe uses resolved settings; `ProbeSampleRate`/`ProbeBitDepth` gone; `CheckSpaceForConversion` wired at both sites and ordered before `DeletePartialFlacs`; mark runtime log equality `STATIC-ONLY`.
6. T10/T11: record F-9, F-10, F-11 as `FALSE` with line evidence; quote T11 assertions `Complete can't remove Failed (sticky)` and `different verdict resets count` as defective blessed behavior.

Use current source, available reports, git history, and commands where observation is required. Referenced 44-artifact set may be absent; record exact gap, do not invent report contents. No source edits or media mutation.

Reporting: each subtask includes command/diff, raw observed output, status `PASS`, `FAIL`, or `BLOCKED`; BLOCKED quotes signature and names owner. Write full report to `task-3-report.md` and commit report.
