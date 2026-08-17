# P1.2 - Reprocess guard semantics

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Three defects: success paths record pre-work `assessment.State`; non-Complete verdict changes reset count; transition blocks before the Nth attempt. Two are documented decisions requiring explicit reversal.

1. Record reversal rationale for stickiness in report, quoting `task-10.2-report.md`, before changing code.
2. Record re-scoping rationale for off-by-one, quoting `task-10.3-report.md` finding #2, and confirm reviewer requirement — a Failed disc starts no process — remains satisfied.
3. Success paths record cycle outcome, not `assessment.State`.
4. Count consecutive non-Complete outcomes regardless of verdict, so oscillation terminates.
5. N attempts execute before blocking: N=3 runs attempts 1-3; attempt 4 refused.
6. Genuine Complete clears Failed.
7. Add `--reset-guard` to `SacdConvertCommand`, logging each cleared entry.
8. Log every transition at Warn with ISO, previous verdict, new verdict, count.
9. Resolve T10.3 kept-minor #7 duplicate Failed lookup in `RunAsync` and `ProcessIsoAsync` — keep with documented reason or remove.
10. Guard writes atomic enough that interrupted write cannot produce unparseable JSON; `LoadAsync` must not silently erase lockouts on `JsonException`.
11. Preserve nine cancellation guards from T10.3 review-fix-2; no state write after cancellation request.

Acceptance: P3.2 suite passes with inverted assertions; three consecutive successes never accumulate; deterministic failure runs exactly three times then fourth refused; alternating verdicts terminate; `--reset-guard` restores Failed disc; interrupted write does not erase file.

Reporting: per subtask command/diff, raw observed output, PASS/FAIL/BLOCKED. BLOCKED quotes signature and names owner. Quote prior T10.2/T10.3 rationale verbatim where artifacts are absent by quoting the plan's preserved text; mark historical artifact unavailable. Write report to sibling `task-7-report.md`.
