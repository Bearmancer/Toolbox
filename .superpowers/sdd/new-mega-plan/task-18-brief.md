# P3.3 - State matrix and guard termination

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Extend committed `checks/` harness. No new `null` literals, nullable-forgiving `!`, or speculative production APIs.

1. Fresh directory, no CUE/DFF/FLACs -> `NeedsExtraction`, no throw (P1.1).
2. Valid DFF, no CUE -> `InvalidArtifacts`, stale DFF deleted, nothing else removed.
3. Valid DFF, CUE, zero FLACs -> `NeedsPrimaryConversion`.
4. Valid DFF, CUE, partial FLACs -> `NeedsPrimaryConversion`.
5. CUE, all FLACs, durations correct, no DFF -> `Complete`.
6. Final track 20 s -> `Complete` (P1.3).
7. Final track 0 bytes -> non-`Complete`.
8. Guard termination through orchestrator path, not `ReprocessGuard` in isolation: three consecutive non-Complete outcomes -> Failed on fourth encounter with zero process starts; three successes no accumulation; alternating verdicts terminate; reset restores processing.

Acceptance: all eight cases pass; case 8 drives production orchestration where environment permits, otherwise exact runtime blocker and owner recorded. Every assertion cites this brief or guide. Clean/forced harness exits preserved.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-18-report.md`.
