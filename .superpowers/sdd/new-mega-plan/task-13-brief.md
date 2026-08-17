# P2.1 - ProbeDsdAsync hardening

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

1. Replace every `ReadChars` with `ReadBytes` plus `Encoding.ASCII.GetString`.
2. Replace narrowing casts with `long`/`ulong` and `Stream.Seek` for skipping.
3. Bound seeks so corrupt size cannot pass EOF.
4. Confirm walk still breaks after `PROP` on real files.
5. Consider routing through `DffMetadataStripper` chunk reader; if not, record why.

Acceptance: real Disc 3 probe returns 2822400 Hz / 2 ch unchanged; corrupt oversized chunk returns error, not throw/over-allocation.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-13-report.md`.
