# Historical T11 Report — Collision Note

**Date:** 2026-08-17
**Author:** P3.2 decontamination task

## Finding

Historical `task-11-report.md` (T11 harness execution) is absent from the repository.

The file `.superpowers/sdd/new-mega-plan/task-11-report.md` exists but is the **P1.6 ISO deletion gating report**, not the historical T11 regression harness report.

## Evidence

- P1.6 report (`.superpowers/sdd/new-mega-plan/task-11-report.md`): Records 6 P1.6 validation cases + 5 guard cases (11/11 pass). Contains "Complete clears Failed" and "Differing non-Complete verdict increments" assertions from P1.2 fix, not historical T11 blessed defects.
- Historical T11 report: Referenced in `new-mega-plan.md` §0.1 as recording "74 passing cases" with two blessed defects. File not found in repository.

## Blessed Assertions (from plan §0.2)

The historical T11 harness asserted two defects as correct behavior:

1. **"Complete can't remove Failed (sticky)"** — `Failed` entries persisted regardless of subsequent `Complete` verdicts.
2. **"different verdict resets count"** — A change in verdict (e.g., `NeedsExtraction` → `Complete`) reset `ConsecutiveCount` to 0.

These are the two guard defects the compliance audit raised. The harness encoded them as expected behavior and passed.

## Source

Quotes sourced from `new-mega-plan.md` §0.2 "The T11 harness asserted two of the defects as correct behaviour".
