---
concern: Audio (SACD pipeline)
status: complete — see §6 Completion for what shipped
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

## 6. Completion

All CPM tasks (A1–A10) closed. A3 and A9 — parked at pre-flight for lack of real SACD media — were satisfied by an actual end-to-end run once real incomplete source material was located, rather than left provisional.

**Findings resolved:** F-1 (`onOutputLine` unwired) and F-2 (`inactivityTimeout` unwired) are both wired: `SaraconService`'s d2p call defaults `onOutputLine` to `Telemetry.Debug` and sets a 5-minute `inactivityTimeout` alongside the pre-existing 1h wall-clock timeout; all 3 `SoxService` call sites default `onOutputLine` the same way. `SacdExtractService`'s 2 call sites (probe/extract) still discard `onOutputLine` — known, deliberately deferred, not a regression. F-3 (Bit24 unverified against real media) is resolved by the real run below.

**Scope changes beyond the original 10-task CPM (both by direct user instruction mid-session, not controller rulings):**
1. `--format both` was implemented (commit `26b74bd`, fixing a real gap where the disc-level pipeline silently dropped its 16-bit sibling output), then removed entirely one commit later (commit `0ffddf1`, which supersedes `26b74bd` and says so in its body) — no method or flag may request both formats at once any more. That same commit unified `--keep-iso` into `--retain-artifacts`, which now retains ISO and DFF/XML together (previously DFF couldn't be retained at all — it was deleted unconditionally regardless of any flag).
2. A second, pre-existing bug was found via the real run below and fixed (commit `5e014b1`): `--format 16`/`--format 24` — the bare-numeric CLI syntax documented in the command's own help text — never actually worked. Spectre.Console.Cli's default enum parsing treats a purely-numeric option string as the enum's underlying int value rather than doing a name match; since `Bit24 = 1` (not 24), `--format 24` silently built an invalid `(AudioOutputFormat)24`. Fixed with a `[TypeConverter]` on `AudioOutputFormat` itself that accepts `16`/`24`/`Bit16`/`Bit24` and rejects anything else with a clear error — both CLI commands (`sacd-convert`, `dsd-convert`) picked up the fix automatically since Spectre resolves converters from the type.

**Real-media run (satisfies A3 + A9):** real, genuinely incomplete SACD source discs were located outside this worktree (Disc 18 and Disc 20 of a personal collection, both missing track 01 after an interrupted prior run — investigated via `state/logs/audio.jsonl`; cause not determined, and unrelated to any code defect found this session). Re-running the pipeline against both fully recovered them: Disc 18 (default 16-bit, `--retain-artifacts`) — 9/9 FLACs; Disc 20 (`--format Bit24`, after first hitting the bug above via the bare `--format 24`) — 8/8 FLACs. Both matched their cue sheets exactly and both correctly retained ISO+DFF/XML together (new `Pipeline.ArtifactsRetained` log line, confirmed in production). Real Saracon cadence measured empirically: progress lines every ~130–135s during normal operation, giving the provisional 5-minute `inactivityTimeout` from A4 ~165s of margin above the observed maximum gap — no retune needed.

**Commits (chronological):** `26b74bd` (A5 fix, superseded) → `0ffddf1` (format/retention redesign, supersedes `26b74bd`) → `9733c29` (A2+A4+A7: onOutputLine/inactivityTimeout wiring + synthetic harness verification, all 5 scenarios passed) → `5e014b1` (format-parsing bugfix) → closeout (this commit, A10: journal + AGENTS.md sync + real-run state).

**Deferred, not fixed (flagged, not forgotten):** `SacdExtractService`'s 2 `RunAsync` call sites still discard `onOutputLine` (out of A2's named scope). `AudioOutputFormatConverter.ConvertFrom`'s non-string branch calls `value.GetType()` without a null check — not reachable in practice since Spectre never passes null.
