---
concern: Audio (SACD pipeline)
status: active
ref: github.com/Bearmancer/Toolbox @ fe6e322d (master)
source_docs: [audio-cli-spec.md, god-audit-spec.md, telemetry-spec.md, sacd-guide.md]
---

# Audio / SACD Pipeline — Plan

## 1. Scope & non-goals

`sacd_extract` → strip ID3 → measure peak → Saracon DSD→PCM → sox split at CUE `INDEX 01` → tag FLAC. Governing authority is `sacd-guide.md` (RED PS3 guide). Not in scope: SoX or ffmpeg as DSD→PCM converters (SoX cannot read DSD; ffmpeg's peak is filter-dependent and unsafe against the 0.5 dB headroom budget — settled in prior consolidation, not reopened here).

## 2. Current state — re-verified against `fe6e322d`, not inherited

21 files, 3,623 lines, `src/Services/Audio` + `src/CLI/Audio`. This reflects the completed M1–M4 mega-plan (`0451bf4`) plus one later fix (`5858a75`).

**Confirmed fixed, no longer findings:**
- `DffMetadataStripper` copies 16 bytes and rewrites `ckDataSize` — the ID3 strip actually strips.
- `ReprocessGuard.cs` exists (159 lines) with verdict-agnostic counting and `Complete`-clears-`Failed` semantics.
- `SacdProbeService`/`SacdProbeRunner`/`PathValidator.ValidateOutputDirectory` — all fully removed, zero references anywhere including their own definitions.
- `--format` help text and behavior now agree: `Bit16`, `Bit24`, and `Both` are all real, working options (`AudioModels.cs:62-64`), and the format description reads *"16 (default, CD-compatible) or 24 (hi-res)"* — matches.

**Reopened by this pass (D-1 supersession):** the earlier "16-bit only, locked" decision from a prior session in this consolidation is dropped. Source has moved past it — 24-bit is genuinely supported now, which is closer to the guide's own 88.2/24 target than a 16-bit-only policy was. This plan proceeds on the assumption both formats stay supported.

## 3. Findings

### F-1 — `onOutputLine` still wired to nothing `[MEDIUM] [HIGH]`

`SaraconService.cs:138` passes `onOutputLine: onOutputLine` into `processRunner.RunAsync`, but grep across every caller in `src/` shows the parameter is never supplied from above — `SaraconService`'s own public methods default it to `null`. Saracon and sox output is captured into a `StringBuilder` and discarded on success. No shell-level redirection can recover this; it has to be wired at the call site.

### F-2 — `inactivityTimeout` still wired to nothing `[MEDIUM] [HIGH]`

`ProcessRunner.cs` fully implements `inactivityTimeout` (the `CancelAfter` calls, the `inactivityTask` branch). Zero callers pass it. Only the one-hour wall-clock `timeout` guards a hung Saracon process today. A process that prints `100%` and stalls blocks for the full hour instead of minutes.

F-1 blocks F-2: you cannot pick a sane inactivity window without first knowing Saracon's real output cadence, and nothing currently records it.

### F-3 — Bit24/Both path has no dedicated verification `[LOW] [MEDIUM]`

The format is now accepted and presumably routes through `ForDsdRate` correctly, but nothing in the visible commit history shows a real-media run exercising `--format 24` specifically — every gate report in the corpus (`p5_p6_execution_plan.md`, the P5.1/P5.2 reports) ran the default (`Bit16`) path only. Not a defect, but an unverified claim.

## 4. CPM network

**Project duration: 9.0 h.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| A1 | Freeze baseline: tag, confirm build clean, media inventory | 0.5 | — | 0.0 | 0.5 | 0.0 | 0.5 | **0** |
| A2 | Wire `onOutputLine`: `Telemetry.Debug` per Saracon/sox line | 1.5 | A1 | 0.5 | 2.0 | 0.5 | 2.0 | **0** |
| A5 | Confirm Bit24/Both end-to-end: rate mapping, no dead branches | 1.0 | A1 | 0.5 | 1.5 | 4.5 | 5.5 | 4.0 |
| A6 | Re-verify gain-probe rate matches master rate under both formats | 1.0 | A1 | 0.5 | 1.5 | 4.5 | 5.5 | 4.0 |
| A3 | Measure real output cadence from one disc; choose inactivity window | 1.0 | A2 | 2.0 | 3.0 | 2.0 | 3.0 | **0** |
| A4 | Wire `inactivityTimeout` into `RunConversionAsync` | 1.0 | A3 | 3.0 | 4.0 | 3.0 | 4.0 | **0** |
| A7 | Standalone harness: termination reasons + inactivity kill + output capture | 1.5 | A4 | 4.0 | 5.5 | 4.0 | 5.5 | **0** |
| A8 | Build gate | 0.5 | A5,A6,A7 | 5.5 | 6.0 | 5.5 | 6.0 | **0** |
| A9 | Real-media gate: one disc, both `--format 16` and `--format 24`, compare | 2.0 | A8 | 6.0 | 8.0 | 6.0 | 8.0 | **0** |
| A10 | Journal + `AGENTS.md` update | 1.0 | A9 | 8.0 | 9.0 | 8.0 | 9.0 | **0** |

Critical path: `A1 → A2 → A3 → A4 → A7 → A8 → A9 → A10`. A5/A6 (the Bit24 re-verification) carry 4 h of float — real, but not schedule-binding.

## 5. Out of scope

Any change to the click-trim policy (correctly absent). Any Saracon replacement. Reopening the reprocess-guard or split-verification work — both closed and re-verified present in this source snapshot.
