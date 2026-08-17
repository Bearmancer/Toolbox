# SACD Completion v2 - Final Stray Report

Date: 2026-08-17
Plan: `new-mega-plan.md`
Baseline: `d4db355`
Workspace: `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`

## Executive status

Plan defines 31 tasks and 173 subtasks. Phase 0-4 source work and durable harness work are complete at task level, with historical runtime blockers reconciled where real media was later available. Phase 5 has Gate A and Gate B complete. Gate C was explicitly skipped by user; Gate D and Gate E remain blocked by that dependency. Phase 6 documentation, journal reconciliation, and E1-A experiment are complete.

Remaining non-PASS residue is listed below. No item is silently treated as complete.

## Verified changes and runtime evidence

### P4/P5 runtime

- Disc 13 command: `dotnet run --project src\App -- audio sacd-convert "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 13\Disc 13.iso" --keep-iso`.
- Actual config: default `AudioOutputFormat.Bit16`; JSONL proves Saracon `-c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00/5.87 -T -V all -t ...`.
- Disc 13: 9 FLACs, 2 CUEs, ISO retained, normal Saracon exits, gain peak `-6.37 dB`, no `OutputTooSmall`.
- Disc 3 Gate A: 4 FLACs; durations `1223.000000`, `1158.373333`, `820.720000`, `1519.136190`; one ID3 detection; stripper `3332711216 -> 3332709410`; ISO/CUE retained.
- Disc 4 Gate B: fresh case-A extraction; 8 FLACs and 1 CUE; safe rerun after cleanup fix retained ISO and FLACs while removing DFF/XML/WAV; exit 0.
- Build after cleanup fix: `dotnet build Toolbox.slnx --no-restore --no-incremental` -> 0 warnings, 0 errors.

### Cleanup fix

Root cause was an early `keepIso` return in `PipelineOrchestrator.CleanupSuccesses`. Fix validates outputs and deletes DFF/XML first, then skips only ISO deletion when `keepIso` is true. RED observed `dff=2`, `xml=1`, `iso=True`; GREEN observed `dff=0`, `xml=0`, `wav=0`, `flac=8`, `iso=True`.

### E1-A

- Dummy: payload `42336000`, total `42336144`, FRM8 size `42336132`.
- SHA-256: `2cdd2fe5d5305980fbb99fd820c0cd56f9e9f8f33c80b0faf6b11c3e4ead6123`.
- 88200/24: exit 0, `22.519 s`, peak `-28.25 dB`.
- 44100/16: exit 0, `22.574 s`, peak `-28.27 dB`.
- Delta: `0.02 dB`; verdict: immaterial; no re-conversion indicated.

### Final durable harness

Command: `dotnet run --project checks\GuardChecks.csproj`

Observed: `30 passed, 0 failed, 1 blocked, 31 total`. The blocked case is P3.3.8 only: the harness records the production `PipelineOrchestrator` guard integration seam but does not invoke it. All P3.4.1-P3.4.7 and P3.5.1-P3.5.6 cases pass.

## Residue for AI review

| ID | Status | Exact evidence | Rationale / owner |
|---|---|---|---|
| R1 | BLOCKED | `Get-Volume` exposed only `C:` | P0.1 cannot prove byte-equal copy to another physical volume; owner: hardware |
| R2 | FAIL/BLOCKED | Deleted historical FLACs mean 13-canary baseline unavailable | P0.1 hashes cannot be fabricated; owner: user media state |
| R3 | BLOCKED | Historical T11 report absent; existing task-11 report is P1.6 | Audit artifact collision documented in `checks/collision-note.md`; owner: unavailable historical artifact |
| R4 | BLOCKED | P3.3.8 durable harness never drives `PipelineOrchestrator` guard path | Synthetic state matrix passes, production termination path unobserved; owner: checks/runtime harness |
| R5 | BLOCKED | P4.2.5 normal Saracon exits observed, but completion-marker hang and truncated-output branches not forced | Owner: runtime contract fixture |
| R6 | BLOCKED | P4.3.4 forced probe failure and cleanup-exception masking not exercised | Owner: runtime fault-injection harness |
| R7 | SKIPPED/BLOCKED | User instruction: `Skip gate C` | Disc 5 complete; Disc 6 partial; Discs 7-9 untouched; owner: user |
| R8 | BLOCKED | P5.4 requires all discs complete and P5.3 is skipped | No honest 20/20 skip claim; owner: Phase 5 dependency |
| R9 | BLOCKED | P5.5 depends on sequenced Phase 5 cancellation run | No Ctrl+C/cancellation evidence claimed; owner: Phase 5 dependency |

## Historical blockers now superseded

These older reports say `.env`, ISO, DFF, or toolchain were missing. They are superseded only where later runtime evidence exists: P3.4.7 real Disc 3 stripping, P4.2 real ISO/FLAC/trim evidence, P4.3 T8/T3/T7, P5.1, and P5.2. They remain historical records and were not rewritten to erase prior state.

## Files changed for closure

- `src/Services/Audio/PipelineOrchestrator.cs` - keep-ISO cleanup correction.
- `src/Services/Audio/AGENTS.md`, root `AGENTS.md` - current file list, state model, exact Saracon CLI, ownership rules.
- `new-mega-plan.md`, `progress.md` - runtime addenda and statuses.
- `task-22-report.md`, `task-23-report.md`, `p51-gate-a-report.md`, `p52-gate-b-report.md` - evidence addenda.
- `sacd-probe-journal.md` - confidence-tagged reconciliation and residue register.
- `p63-e1a-report.md` - E1-A measurements.
- Superseded plan/audit files named by P6.1 were removed. `.superpowers/sdd/new-mega-plan/` was retained.

## AI review instructions

1. Treat every row in `Residue for AI review` as unresolved unless its cited evidence directly proves otherwise.
2. Do not convert `SKIPPED/BLOCKED` into PASS because sibling discs passed.
3. Check cleanup behavior against the post-fix C# and Disc 4 GREEN filesystem evidence.
4. Check exact Saracon args in `state/logs/audio.jsonl`, not console path rendering.
5. Preserve historical journal claims as historical; use the 2026-08-17 reconciliation for current root-cause confidence.

## Deliberately skipped checks

Per user instruction, no additional test execution was performed during this review turn. These are documented skips, not PASS claims:

- P0.1 second-volume copy and historical canary comparison: no second volume and original FLAC baselines unavailable.
- P3.3.8 production orchestrator guard termination: durable harness seam remains uninvoked.
- P4.2.5 Saracon completion-marker-kill and truncated-output branches: normal real Saracon runs retained as evidence; forced branches skipped.
- P4.3.4 forced probe failure and cleanup-exception masking: fault-injection run skipped.
- P5.3 Gate C: explicitly skipped by user.
- P5.4 Gate D and P5.5 Gate E: skipped because Phase 5 dependency chain is incomplete.

The skipped-check list is intentionally preserved for the next AI reviewer; absence of new test output must not be interpreted as regression evidence.

## Whole-repository stale-artifact scan

Scan scope: entire worktree, tracked and untracked files. `main...HEAD` comparison was unavailable because this worktree has no local `main` ref; the scan therefore used working-tree status, tracked files, ignored paths, filename patterns, and content searches. Report-only mode: no stale-artifact deletion was performed in this scan.

### Keep

- `.superpowers/sdd/new-mega-plan/`: required audit trail; plan explicitly says retain it.
- `.omo/docs/superpowers/audits/sacd-probe-journal.md`, `checks/collision-note.md`, task reports/review packages: historical evidence and rationale.
- `state/logs/*.jsonl`: expected application telemetry; ignored by `.gitignore`.
- `artifacts/`: expected .NET build output; ignored by `.gitignore`.
- `state/youtube/raw/*.json`: application state, not stale build output.

### Cleanup candidates, deliberately not removed

- `.superpowers/sdd/new-mega-plan/e1a/`: generated 60-second DFF/WAV experiment outputs. Keep while AI review needs raw evidence; remove after review sign-off.
- `.superpowers/sdd/new-mega-plan/p53-*.log` and `p53-run.ps1`: interrupted Gate C runner evidence. Keep for audit; remove only after P5.3 disposition is accepted.
- `state/audio/sacd-guard.json`: runtime guard state. Keep if future runs need it; do not commit as source evidence unless explicitly required.
- Root `task-21-review-package.md`, `task-22-review-package.md`, `task-23-review-package.md`: duplicate convenience copies; canonical reports remain in plan artifacts. Safe deletion candidate after review sign-off.

### Removed as superseded

P6.1 removed the eight plan/audit files explicitly listed by the mega-plan. No source, ISO, CUE, FLAC, or user media file was removed by this scan.

### Slop scan

`remove-ai-slops` scan found no obvious source debug prints, TODO/FIXME leftovers, or unreferenced temporary code in `src`. Markdown hits are historical blocker language, not executable slop. No cleanup edits were applied; regression tests were deliberately skipped per the section above.
