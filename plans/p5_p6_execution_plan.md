# P5/P6 Execution Plan

## Current State

| Discs                  | Status                     | Evidence                                                  |
| ---------------------- | -------------------------- | --------------------------------------------------------- |
| 1, 2, 10-20 (13 discs) | ✅ FLACs exist             | 122 FLACs total across these discs                        |
| **Disc 3**             | 🟡 DFF extracted, no FLACs | `Disc 3.dff` (3.1 GB) + `Disc 3.cue` present — **case B** |
| Discs 4-9              | ❌ No output               | Empty directories — **case A** (full extraction needed)   |

> [!IMPORTANT]
> Guard file is empty (`{}`). The 13 prior discs were processed from the main repo, not this worktree. The pipeline will probe all ISOs but should skip the 13 completed discs (output validation → `Complete`).

## Execution Order

### Step 1: P5.1 — Disc 3, case B (~10-20 min)

- **Fastest gate** — DFF already extracted, just needs DSD→FLAC conversion
- Run: `dotnet run --project src\App -- audio sacd-convert "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 3\Disc 3.iso" --keep-iso`
- Verify: 4 FLACs, stripper log once, Saracon.Id3Detected once, no OutputTooSmall, ISO+CUE retained

### Step 2: P5.2 — Disc 4, case A (~1 hour)

- Fresh-disc path — full extraction + conversion
- Run: `dotnet run --project src\App -- audio sacd-convert "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 4\Disc 4.iso" --keep-iso`
- Verify: FLAC count = CUE track count, no leftover WAV/DFF, ISO+CUE retained, guard Complete

### Step 3: P5.3 — Discs 5-9 (~8-10 hours, background)

- Run: pass the parent directory but only Discs 5-9 need processing
- Verify per disc: FLAC count = CUE tracks, zero Failed, no leftover WAV/DFF, ISOs+CUEs retained
- Re-verify 13 canary hashes from prior output

### Step 4: P5.4 — Full 20-disc rerun (~minutes)

- Re-run all 20 — all should skip as `Complete`
- Verify: 20/20 logged as skipped, 20 probe invocations, zero extraction invocations, zero saracon starts

### Step 5: P5.5 — Cancellation test

- Ctrl+C during Saracon on a re-run (or use a test disc)
- Verify: reported as cancellation, no orphaned saracon.exe, exit within seconds

### Parallel: P6.1-P6.3 — Documentation & experiment

- Can start P6.1 (AGENTS.md update, plan cleanup) while P5.3 runs in background

## Wall-Clock Estimate

| Gate      | Time                          |
| --------- | ----------------------------- |
| P5.1      | ~15 min                       |
| P5.2      | ~1 hour                       |
| P5.3      | ~8-10 hours                   |
| P5.4      | ~5 min                        |
| P5.5      | ~5 min                        |
| P6.1-P6.3 | ~2 hours (parallel with P5.3) |

**Total: ~10-12 hours**, dominated by P5.3 Saracon runtime.
