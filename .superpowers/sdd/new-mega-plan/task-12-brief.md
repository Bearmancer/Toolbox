# P1.7 - Stripper exception containment

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Strict input validation plus rethrowing `HasId3Chunk` with no catching caller means one odd DFF aborts batch.

1. Convert `HasId3Chunk` to `ErrorOr<bool>`, or wrap it so callers receive a value.
2. Wrap `PrepareDffAsync` so stripper failure degrades to per-disc error.
3. Keep validations but classify: input mismatched size warns and attempts repair; output remains hard failure.
4. Confirm `finally` partial-output cleanup on every failure path including new ones.
5. Confirm `OperationCanceledException` excluded from catch filter.

Acceptance: synthetic DFF `ckDataSize` four bytes short fails that disc and batch continues; well-formed DFF strips with same byte delta; cancellation during strip still cancels.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-12-report.md`.
