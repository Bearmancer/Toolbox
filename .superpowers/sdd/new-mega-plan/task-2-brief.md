# P0.2 - Guard state audit

Guard semantics at this phase: `Failed` is sticky by historical design; recovery was manual JSON deletion. Audit before remediation.

1. Dump `state/audio/sacd-guard.json`; if absent, record absence. Note historical T10.2, T10.3, and T11 reports claiming driver cleanup.
2. For every entry record ISO path, `Verdict`, `ConsecutiveCount`, and `UpdatedAt`.
3. Classify every `Failed` entry against on-disk output as genuine-failure or false-lockout, quoting evidence paths and metadata.
4. Archive live state to `state/audio/sacd-guard.pre-brief.json`; delete live `state/audio/sacd-guard.json` after archive verification.

Acceptance: every entry classified with on-disk evidence quoted; live file removed; archive retained.

Reporting: each subtask gets command or diff, raw observed output, and `PASS`, `FAIL`, or `BLOCKED`. BLOCKED must quote exact signature and name owner. Do not alter source code or real media. Append all concerns and historical cleanup claims to report `task-2-report.md`.
