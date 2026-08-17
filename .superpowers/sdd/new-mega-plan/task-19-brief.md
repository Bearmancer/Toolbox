# P3.4 - Stripper suite

Work only in `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`.

Extend committed `checks/` harness. New code: no null literals, nullable-forgiving `!`, or speculative production APIs.

1. Synthetic DSDIFF with four top-level ID3 chunks removed; `ckDataSize = filesize - 12`; even.
2. Odd-sized chunk requiring pad; padding preserved; output even.
3. ID3 nested under PROP removed; PROP size rewritten.
4. Truncated file descriptive error; no partial output.
5. Zero-size chunk mid-walk descriptive error; no partial output.
6. Input `ckDataSize` four bytes short: warns and repairs or fails that file only.
7. Real Disc 3 DFF streamed; never `File.ReadAllBytes`; exact figures required when media available.

Acceptance: all seven cases; real Disc3 case blocked with exact signature/owner if media unavailable. Every assertion citation required.

Reporting: per subtask command/diff/raw output/PASS/FAIL/BLOCKED. Write report to sibling `task-19-report.md`.
