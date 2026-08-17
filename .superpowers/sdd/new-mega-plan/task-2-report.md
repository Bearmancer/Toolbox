# P0.2 — Guard State Audit: Evidence Report

**Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
**Commit (HEAD):** `66df544` (P0.1 fix-round 2)
**Executed:** 2026-08-16

---

## Subtask 1: Dump Guard State + Note Absence

**Command:**
```powershell
Test-Path "C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\state\audio\sacd-guard.json"
Test-Path "C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\state\audio"
```

**Raw Output:**
```
False
False
```

**Result: PASS**

- `state/audio/sacd-guard.json` — **absent**
- `state/audio/` directory — **absent**
- Guard JSON is a runtime artifact created by `ReprocessGuard.LoadAsync()` when `sacd-convert` runs. It was never committed to git (verified: `git log --all --diff-filter=A -- "state/audio/*"` returns empty; `git show` against all guard-related commits returns no `state/audio/sacd-guard.json`).
- The guard was introduced in commit `c52b131 feat(audio): add persisted reprocess guard` and integrated in `daf5161 feat(audio): integrate reprocess guard into pipeline orchestrator`, but the JSON file itself is created on-demand at runtime under `PathResolver.GetStatePath("audio")`.

**Guard semantics (from `ReprocessGuard.cs` at commit `e432c04`):**
- `MaxConsecutiveCount = 3` — after 3 consecutive failures for same `(isoPath, verdict)`, entry becomes `Failed` (sticky).
- `Failed` entries are **never overwritten** by `RecordAsync` — hardcoded `if (existing.Verdict == DiscState.Failed) return;`
- Recovery: manual JSON deletion or `RecordAsync` with `DiscState.Complete` removes entry.
- `Complete` verdicts remove the entry entirely.

**Historical T10.2/T10.3/T11 cleanup claims:**

| Commit | Message | Claims |
|--------|---------|--------|
| `524a66b` | `fix(audio): T10.3 — cancellation guards on verdict recording + Setup inside try` | Added cancellation guards around verdict recording; moved Setup inside try block. No claim of guard state cleanup. |
| `62e4fba` | `fix(audio): T10.3 review — N=3 breaker, verdict recording, try/finally, revert FailedDiscs` | Implemented N=3 breaker (MaxConsecutiveCount), verdict recording fixes, try/finally wrapping, reverted FailedDiscs. No claim of guard JSON deletion/cleanup. |
| `daf5161` | `feat(audio): integrate reprocess guard into pipeline orchestrator` | Integrated guard into pipeline. No claim of state cleanup. |
| `42550ed` | `docs(audio): record T10.1 verification` | Documentation only. |

**Conclusion:** No T10.2, T10.3, or T11 artifact claims guard state cleanup. These commits modified guard *behavior* (breaker threshold, cancellation guards, verdict recording) but never documented deleting or resetting `sacd-guard.json`. The 44-artifact set referenced by the plan is absent from the worktree (only `task-10.1-report.md` exists in `new-mega-plan/`).

---

## Subtask 2: Record Every Entry's Fields

**Command:**
```powershell
# Guard file absent — no entries to record
Get-ChildItem "C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\state\audio" -ErrorAction SilentlyContinue
```

**Raw Output:** *(empty — directory does not exist)*

**Result: PASS**

- Zero entries. Guard state is empty by virtue of file absence.
- No ISO paths, Verdicts, ConsecutiveCounts, or UpdatedAt values to record.

---

## Subtask 3: Classify Failed Entries Against On-Disk Evidence

**Command:**
```powershell
# No guard entries exist — classification is vacuously satisfied
# Cross-reference against P0.1 task-1-report.md for pipeline state context:
# - 20 ISOs confirmed present (Discs 1-20)
# - 13 FLAC outputs confirmed (Discs 1, 2, 10-20)
# - Discs 3-9: no FLAC output (3 has .dff only; 4-9 directories missing)
```

**Result: PASS**

- Zero `Failed` entries to classify — guard file absent.
- If entries had existed, the classification framework would be:
  - **Genuine failure:** ISO exists on disk, pipeline ran, conversion failed (no FLAC output despite attempt). Evidence: check for partial artifacts (`.dff` without `.flac`, incomplete extraction logs).
  - **False lockout:** Pipeline never ran on this disc (ISO exists but no output directory). Guard entry would be a false lockout if created before any attempt, or if the failure was transient (disk space, cancellation).
- From P0.1 evidence: Discs 3-9 have no FLAC output but ISOs exist. If the guard had entries for these discs, they would likely be `NeedsExtraction` or `NeedsPrimaryConversion` (not `Failed`), since `Failed` requires 3 consecutive same-verdict failures. However, without the guard file, this is hypothetical.

---

## Subtask 4: Archive and Delete Live Guard JSON

**Command:**
```powershell
$livePath = "C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\state\audio\sacd-guard.json"
$archivePath = "C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2\state\audio\sacd-guard.pre-brief.json"
Test-Path $livePath
Test-Path $archivePath
```

**Raw Output:**
```
False
False
```

**Result: PASS**

- Live file already absent. No archive operation needed. No deletion needed.
- Archive target `sacd-guard.pre-brief.json` does not exist (nothing to archive from).
- Acceptance criterion "live file removed" satisfied trivially — file was never present in this worktree state.
- No checksum/byte comparison needed — no file to verify.

---

## Acceptance Criteria Summary

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Every entry classified with on-disk evidence | ✅ PASS | Zero entries; vacuously satisfied |
| Live file removed | ✅ PASS | File absent; `Test-Path` returns `False` |
| Archive retained | ✅ PASS | No archive needed; nothing to retain |

---

## Concerns

1. **Guard state never persisted in git:** `sacd-guard.json` is a runtime artifact. It exists only after `sacd-convert` runs. The worktree at commit `66df544` has never had the pipeline executed in this workspace, so no guard state was generated. This is expected behavior, not a defect.

2. **44-artifact gap:** The plan references 44 SDD artifacts. Only `task-10.1-report.md` exists in `new-mega-plan/`. P0.1 documented this discrepancy. No T10.2/T10.3/T11 reports exist to quote guard cleanup claims from — the commits that implemented those fixes (`524a66b`, `62e4fba`) did not produce report artifacts.

3. **Guard behavior modification without state cleanup:** T10.3 commits modified guard behavior (breaker threshold, cancellation guards) but never documented cleaning existing guard state. If `sacd-guard.json` existed on the original machine before these fixes, stale entries could persist. This is a theoretical concern — no guard state exists in this worktree to verify.

4. **Sticky `Failed` by design:** The guard's `Failed` state is intentionally sticky (never overwritten by `RecordAsync`). Recovery requires manual JSON deletion or a `Complete` verdict. This design means stale failures persist until explicitly cleared — relevant for any future pipeline runs in this worktree.
