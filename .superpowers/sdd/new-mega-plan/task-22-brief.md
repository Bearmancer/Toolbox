# P4.2 - Tool integration contracts

1. `sacd_extract -P` real ISO parse including multichannel.
2. `sox --i -D` duration on real FLAC.
3. `sox ... -n stats` peak regex including negative and `-0.00`.
4. `sox ... trim` offsets/final EOF.
5. `saracon` short real DFF normal exit, completion-marker, truncated output guard.
6. Record each tool version.

Acceptance: each contract asserted against captured real output, quoted. Missing tools/media -> BLOCKED with exact signature/owner; no inferred PASS.

Reporting: command/raw output/PASS/FAIL/BLOCKED per subtask. Write `task-22-report.md`.
