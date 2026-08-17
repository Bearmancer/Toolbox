# P4.1 - Build and style gate

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

1. `dotnet build Toolbox.slnx --no-restore --no-incremental` -> 0 errors, 0 warnings.
2. Confirm editorconfig violations are build errors.
3. Close deferred formatting nit in `SacdConvertCommand`.
4. Confirm no test package/new dependency entered project files Phases 1-3.

Acceptance: clean build; deliberate style violation fails; project files otherwise unchanged.

Reporting: command/raw output/PASS/FAIL/BLOCKED each subtask. Write `task-21-report.md`.
