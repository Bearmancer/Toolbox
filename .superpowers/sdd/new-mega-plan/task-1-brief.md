# P0.1 - Snapshot and safety net

Baseline commit: `d4db355`.

1. Create annotated tag `backup/pre-completion-brief-v2` at `d4db355`; record tag SHA.
2. Run `git status --porcelain`; record every dirty file. Do not stash or discard.
3. Copy full output tree to a different physical volume; confirm byte totals match. If no accessible second physical volume exists, report `BLOCKED` with exact signature and owner; do not fake copy evidence.
4. Record SHA-256 for one FLAC per disc, 13 canaries, for Phase 5 tamper detection.
5. Confirm all 20 ISOs present with sizes. Record manifest and note nesting `Disc N\\Disc N.iso`.

Acceptance: tag exists; byte totals equal; 13 canaries recorded; 20 ISOs manifested.

Reporting: per subtask record command or diff, raw observed output, and `PASS`, `FAIL`, or `BLOCKED`. BLOCKED must quote blocking signature and name owner. Write full report to sibling `task-1-report.md`.
