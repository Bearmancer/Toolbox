# P1.1 - Fresh-disc crash

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

`DeleteFlacsInDir` enumerates without an existence check. On a fresh disc `FindDffDir` returns a non-existent path, state is `NeedsExtraction`, and the deleter runs before extraction. `Directory.GetFiles` throws; the inner catch covers only `File.Delete`; nothing up to `RunAsync` catches it. The whole batch aborts.

1. Add an existence guard as the first statement of `DeleteFlacsInDir`.
2. Audit every directory enumeration in `src/Services/Audio`; record each with a disposition. Every sibling already guards — this is the sole exception.
3. Add a per-disc exception boundary in `RunAsync` so an unexpected throw fails one disc and the batch continues.
4. Confirm the boundary does not swallow `OperationCanceledException`.

Acceptance: a fresh temp tree reaches the extraction call without throwing; an injected `IOException` fails one disc and the loop continues; Ctrl+C still stops the run.

Reporting: per subtask record command or diff, raw observed output, and `PASS`, `FAIL`, or `BLOCKED`. BLOCKED must quote blocking signature and name owner. Write full report to sibling `task-6-report.md`.
