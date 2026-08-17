# P2.3 - Probe harness disposition

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

`SacdProbeService` is DI-registered but `RunProbeAsync` has no caller; `RealDffFixture` hardcodes `C:\Temp\t.dff` in shipped assembly.

Decision: remove probe harness. Delete `SacdProbeService.cs`, `SacdProbeRunner.cs`, `RealDffFixture.cs`, and registration together. Confirm clean build and no unreferenced public member remains.

Acceptance: three files and registration gone; clean build; no unreferenced public member remains.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. BLOCKED quotes signature and owner. Write report to sibling `task-15-report.md`.
