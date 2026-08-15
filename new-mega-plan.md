# SACD Pipeline — Authoritative Plan

**Version:** 4.0 — SUPERSEDES AND REPLACES ALL PRIOR SACD DOCUMENTS
**Date:** 2026-08-15
**Status:** authoritative; this is the only SACD plan that should exist
**Changes from v3:** six defects found by self-audit are corrected — three false-negative acceptance gates, one byte-count error, one build break, one scope overclaim. One structural finding added (F-23, no loop-breaker).

---

## 0. Scope of this document

This file replaces every earlier SACD plan, spec, audit, and report. Once it is in place, **the following can be deleted**:

| Delete | Why it is safe to delete |
|---|---|
| `sacd-pipeline-rescue.md` | B1–B7 dispositions carried forward in §5; Phase 0 migration already executed |
| `2026-08-12-sacd-consolidated.md` | Status ledger contains two false rows (C-6); binding content restated here |
| `2026-08-09-sacd-death-loop-v2-design.md` | Hypotheses closed; §8/§12 process rules restated in §1 |
| `2026-08-14-audio-design-assessment.md` | A1–A18 dispositions carried forward in §5 and §10 |
| `2026-08-10-logging-audit.md` | Audio findings verified fixed in source (C-4); non-Audio out of scope |
| `2026-08-10-logging-audit-spec.md` | Method doc; its one durable rule restated in §1 |
| `2026-08-10-process-runner-streaming.md` | Targets a deleted directory and the wrong .NET version |
| `sacd=extractopn.md` | 8-line stub, header only, zero content |
| `sacdprobe-editorconfig.md` | Executed; two live constraints restated in §1 — but see C-7, one task falsely marked done |

**Keep:** `sacd-guide.md` (authority), `sacd-probe-journal.md` (append-only, needs the C-2 correction line), `toolbox-flatline.md` (repo hygiene, separate), the two YouTube plans (separate, but see T1).

### Evidence scope — read this before acting on any "dead code" finding

Every code claim in this document was verified against **22 source files**: `src/Services/Audio/*.cs` (19) and `src/CLI/Audio/*.cs` (3).

**`Program.cs`, `Core`, and the other CLI branches were not available.** Findings F-15, F-16, and F-17 assert "zero call sites" — that means *zero within those 22 files*. All three members are `public` on public classes, so `App` or another CLI branch could reference them. `SacdProbeService` in particular may be resolved from `Program.cs`; `sacdprobe-editorconfig.md` task 14 claims someone invoked `RunProbeAsync` through DI. **Each requires a repo-wide grep before deletion** — T8 step 1.

---

## 1. Binding rules

1. **Pipeline shape is fixed:** `sacd_extract` (single-file DSDIFF Edit Master + CUE) → strip ID3 → measure peak → Saracon DSD→PCM → split at CUE `INDEX 01` → tag FLAC.
2. **Saracon is the only DSD→PCM converter.** SoX cannot read DSD. ffmpeg must not measure gain (§3).
3. **No 0.0065 s click trim.** The guide prescribes it only for per-track extraction; single-file-then-split avoids the click by construction. Any future proposal to add it is wrong.
4. **Gain never clips.** Measure the actual peak, apply below it with 0.3–0.5 dB headroom, never exceed +6 dB.
5. **DFF must not carry ID3.** Guide rule 2.14.1.5.2.
6. **Every finding carries a confidence tag** (`HIGH`/`MEDIUM`/`LOW`) with its basis. Tags may not be dropped or upgraded when restated.
7. **Every acceptance criterion states its measurement, and must be capable of failing.** A criterion that cannot fail is decoration, not verification. A criterion that fails when the code is correct is worse than none.
8. **Structured logging only** — `Telemetry.Error("... {File}: {Error}", path, ex.Message)`, never string interpolation.
9. **Repo constraints:** one class per file; no inline comments; no test NuGet packages; no new dependencies; `ErrorOr` railway style; editorconfig violations are **build errors**.
10. **The probe harness stays in `src/Services/Audio`** — deliberate, oracle-documented user override. Not an open design question.

---

## 2. Decision register

| # | Decision | Rationale | Consequence |
|---|---|---|---|
| **D-1** | **16-bit only.** `Bit16` is the sole format supported by the SACD pipeline. | User decision. Library is for personal listening, not upload. | The pipeline's derived-output subsystem becomes unreachable and is **deleted, not fixed**. Removes three findings outright. **Does not extend to `dsd-convert`** — see T3. |
| **D-2** | **Saracon runs twice** (gain probe + master). | Self-consistent measurement (§3). The guide's one-pass route needs foobar2000, which cannot be automated headlessly. | ~50–70 min per disc. T15 is 12 h and cannot be compressed. |
| **D-3** | **Saracon measures gain**, not ffmpeg or sox. | §3. | Locked. |
| **D-4** | **One-pass optimisation is dead**, not deferred. | Requires a 24-bit intermediate to apply gain post-dither safely; D-1 forbids 24-bit. | Experiment E1-B removed. Only E1-A survives. |

### Deviation from the guide — DEV-1

- **Guide says:** 88.2 kHz / 24-bit, TPDF. The guide has no 44.1 kHz step at all.
- **We do:** 44,100 Hz / 16-bit in a single Saracon pass.
- **Technically:** both are single integer decimations of the DSD64 bit clock (2,822,400 ÷ 32 = 88,200; ÷ 64 = 44,100). Neither is a fractional resample.
- **Cost:** the gain decision is welded in at 16 bit with no headroom to correct later; 22.05–44.1 kHz content is discarded. Output is a listening copy, not an archival master.
- **Revoke DEV-1 before converting** if any rip is ever destined for a tracker that mandates the guide.

---

## 3. Why Saracon measures the gain

**sox cannot read DSD.** The codebase proves it: `CalculateGainAsync` converts DFF→PCM with Saracon *first*, then runs `sox stats` on the PCM. If sox could open a DFF that conversion would be pointless.

**ffmpeg can decode DSD but must not be used here.** <cite index="13-1">It defaults to 16-bit output for DSD with no straightforward way to request 24–32 bit,</cite> and <cite index="17-1">DSD→PCM requires an explicitly chosen lowpass filter for the high-frequency noise, with the threshold left to the user.</cite>

The decisive point: **peak level is a property of the decimation filter, not of the DSD stream.** Different filters give different overshoot and ringing, so ffmpeg's peak is not the peak Saracon will produce. The gap is typically a few tenths of a dB — and the pipeline cuts exactly 0.5 dB (`TargetHeadroomDb = -0.5`). Using ffmpeg would force padding the margin to ~1.5 dB, discarding the level the measurement exists to preserve.

**Caveat, and it is live:** this argument only fully holds when probe and master use the *same* decimation ratio. Today they do not — F-2. T8 closes it.

---

## 4. Corrections to prior reporting

Everything below was claimed by an earlier plan, an agent, or by me, and is wrong or overstated. Verified against source.

| # | Prior claim | Source | Verified reality |
|---|---|---|---|
| **C-1** | Stripper "produces a malformed DSDIFF"; output "missing bytes 12–15" | commandcode `62fa8a33` | **Wrong.** Simulation: output is **byte-identical** to input. Nothing stripped, nothing malformed. Silent no-op. |
| **C-2** | Probe journal `stripped/` rows are meaningful evidence | `sacd-probe-journal.md` | **Void.** Given F-1 those runs were byte-identical to `raw/`. Both show four `Unknown chunk (ID3 )` warnings. Append a correction; do not delete rows. |
| **C-3** | "Change 12 to 16. No other changes needed." | commandcode `62fa8a33` | **Incomplete.** Leaves `FRM8 ckDataSize` over-declaring by the bytes removed (F-3). |
| **C-4** | 5 CRITICAL silent catches in Audio | logging-audit | **Stale.** `AudioMetadataService` ×3 (34, 80, 114), `DsdConvertService` (121), `DffMetadataStripper` (142), `PathValidator` (33) all log now. Zero remain. |
| **C-5** | UTF-8 / ACP 65001 is the root cause | v2 design §3 | **Closed-false.** Rejected by probe run #4; ACP is 1252. Never restate. |
| **C-6** | "ID3 strip fix landed, commit `0395a1e`"; "`tools/SacdProbe` stays as the regression gate" | consolidated ledger | **Both false.** `Seek(16)` landed in `HasId3Chunk` only. `tools/SacdProbe` was deleted by the editorconfig plan. |
| **C-7** | "Register `SacdProbeService` in DI" marked `[x]` | editorconfig task 10 | **Not done in `AudioSetup`** — it registers 12 services, not that one. Check `Program.cs` before concluding it is unregistered everywhere. |
| **C-8** | Last track's `Duration` is `TimeSpan.Zero` | subagent `bg-18` | **Wrong.** `CueParser.cs:83` — `TimeSpan.Zero` is `StartTime`; `Duration` is **null**. |
| **C-9** | Last-track 30 s check is "ineffective, nested under `if (track.Duration is {})`" | research agent, and my own v1–v2 | **Wrong — it is live.** `FlacCompletenessChecker.cs:85` is an `else if` on that condition, firing exactly when `Duration` is null. Hard completeness failure → F-11. |
| **C-10** | Path containment broken in three places, HIGH | assessment A8, and my own v1–v2 | **Overstated twice.** `LogPaths.Normalise` appends a trailing separator so `IsWithin` is correct; `PipelineOrchestrator:384` appends it explicitly and is correct. Only `PathValidator` uses a raw prefix, and it has no call sites in the audio subset. HIGH → LOW. |
| **C-11** | Inactivity cancellation leaves the child running, HIGH | reconciliation agent | **Latent.** No caller passes `inactivityTimeout` — `SaraconService:165-168` passes only `timeout`, `completionPattern`, `completionTimeout`. HIGH → LOW-latent. |
| **C-12** | B6 "partial split returns success" is FIXED | rescue plan, my v1–v2 | **Partial.** Aggregate check at `:228` catches missing tracks, but `:209-212` discards each error with a bare `continue` and no log. |
| **C-13** | *(v3 plan)* "`_clean.dff` measurably smaller than source" as a T14 gate | **my v3** | **Unverifiable.** `_clean.dff` is written into `dffDir` and `CleanupSuccesses` deletes `*.dff`. The file is gone before inspection. Gate would fail when the fix works. Corrected in T14. |
| **C-14** | *(v3 plan)* "zero `sacd_extract` process starts" as a T16 gate | **my v3**, inherited from rescue plan | **Impossible.** `ProcessIsoAsync:138` calls `ProbeAsync` unconditionally before assessment. Historical logs show `SacdExtract.ProbeStart` followed by `Skipping Disc 11`. Corrected in T16. |
| **C-15** | *(v3 plan)* clean file is "1,758 ± 8 B smaller" | **my v3** | **Off by 48 B.** 1,758 is the ID3 *payload* total; each chunk also has a 12-byte header. Chunk spans: 446 + 436 + 474 + 450 = **1,806 B**. My own simulation printed 1,806. Corrected in T4. |
| **C-16** | *(v3 plan)* T3 deletes `DsdConvertService.DeriveFlacAsync` | **my v3** | **Build break.** `DsdConvertCommand.cs:121` calls it and `:101` calls `ConvertFullDffAsync`. Both belong to the standalone `dsd-convert` command, not the SACD pipeline. Corrected in T3. |

---

## 5. Duplicate marking

### 5a. TRUE duplicates — collapsed, all closed

| ID | Absorbed from | Verdict |
|---|---|---|
| Cancellation vs timeout | rescue B1, assessment A2 (part) | **FIXED** — `ProcessRunner.cs:124,137` |
| Dead completion-grace branch | rescue B2 | **FIXED** — independent `WhenAny` arm `:171-193` |
| Tree-A output layout | rescue B3 | **FIXED** — `PipelineOrchestrator.cs:146-150` |
| Ordinal ISO sort | rescue B7 | **FIXED** — `NaturalSortPad` `:40-48` |
| Partial split returns success | rescue B6 | **PARTIAL** — C-12, becomes F-9 |
| Failed-disc cleanup | rescue B5 | **MOSTLY FIXED** — residual is F-6/F-7 |
| Silent-catch logging (Audio) | logging-audit ×6 | **CLOSED-STALE** — C-4 |
| UTF-8 root cause | v2 §3 | **CLOSED-FALSE** — C-5 |
| Relocate probe harness | assessment A14 | **CLOSED-ADJUDICATED** — rule 10. Only C-7 survives. |

### 5b. FALSE duplicates — kept separate

| Apparent pair | Why distinct |
|---|---|
| "grace branch dead" vs **F-4** (grace kill → exit 0) | Same ten lines, opposite problems. Fixing the first *activated* the second. |
| **F-1** (stripper 12-vs-16) vs A11 (duplicated chunk walkers) | Correctness defect vs DRY observation. Doing A11 first propagates the bug into a shared reader. |
| A12 vs A5 vs B5 vs **F-6** (recovery deletes CUE) vs **F-7** (cleanup path mismatch) | Five artifacts, five lifetimes. Merging them is what let F-6 and F-7 survive every prior plan. |
| **F-2** (probe rate mismatch) vs **F-5** (stereo-DSD64 size estimate) | Both rate assumptions, different files, different fixes. |
| **F-11** (30 s rule) vs **F-23** (no loop-breaker) | F-11 is one trigger; F-23 is the absence of any guard that would stop *any* trigger. Fixing F-11 alone leaves the class open. |
| A1/A4 (god classes) vs every correctness finding | Decomposition is not a fix. §10 defers it. |
| rescue "Phase 0 migration" vs anything current | **Already executed** — 13 discs verified in Tree B. Dead section. |

---

## 6. Findings

### F-1 — `DffMetadataStripper` is inert `[CRITICAL] [HIGH]`

`DffMetadataStripper.cs:97` copies **12** bytes; `HasId3Chunk:37` seeks to **16**.

The DSDIFF 1.5 spec defines the Form DSD Chunk as `ckID`(4) + `ckDataSize`(8, big-endian 64-bit) + `formType`(4) = 16 bytes before the first local chunk, with <cite index="1-1">ckDataSize equal to total file size minus the length of ckID and ckDataSize, always an even number because all chunks cover an even number of bytes, and formType always 'DSD '</cite>.

Simulating the exact loop: the walk starts at 12, reads `"DSD "` as a chunk ID, reads bytes 16–23 as a size (`"FVER"` + zero padding = `0x4656455200000000` ≈ 5.07 × 10¹⁸), and since that ID is not `"ID3 "` it writes both back and calls `CopyBytes` with the absurd count, which terminates on EOF.

```
current code : 3504 B out, byte-identical = True,  ID3 chunks remaining = 4
copy-16 fix  : 1698 B out, 1806 B removed,         ID3 chunks remaining = 0
```

**The single defence the entire death-loop programme was built on has never executed.** The real Disc 3 DFF carries four top-level ID3 chunks spanning 1,806 bytes (payloads 434 + 424 + 462 + 438 = 1,758, plus 4 × 12-byte headers).

### F-23 — There is no loop-breaker anywhere in the pipeline `[HIGH] [HIGH]` — **NEW, structural**

`ProcessIsoAsync` maps every non-`IsComplete` assessment to reprocess, unconditionally. There is no attempt counter, no quarantine, no `Failed` terminal state. A disc that fails a completeness rule **deterministically** — same input, same output, same verdict — is re-extracted and re-converted on every run forever, while `succeededIsos.Add(isoPath)` still reports success.

Worse, the reprocess path runs `DeletePartialFlacs` then `DeleteExtractionArtifacts` *before* re-extracting, so each cycle opens a ~50-minute window in which a correct disc has been reduced to an ISO. Interrupt inside that window and working output is destroyed to satisfy a check that was never going to pass.

This is the class; F-11 is one instance. **Any completeness rule added without a guard is a potential loop** — including the "positive decoded duration" replacement proposed for F-11, which fails permanently if a CUE's final `INDEX 01` lies past the master's end (sox trims past EOF, emits an empty file, exits 0).

The loop only recurs while the ISO survives. With `--keep-iso` absent, run 1 deletes the ISO and run 2 skips the disc entirely — but every plan in the archive mandates `--keep-iso`, so in the intended configuration it repeats indefinitely.

### F-2 — Gain probe decimates at a different ratio than the master `[HIGH] [HIGH]`

`DsdConvertService.cs:15-16` fixes the probe at `ProbeSampleRate = 88200`, `ProbeBitDepth = 24`. Under D-1 the master converts at 44,100 / 16. Peak is measured through ÷32; gain is applied through ÷64. Different anti-alias filters, and the 88.2 kHz probe retains 22–44 kHz content the master discards.

Direction is *probably* conservative — DSD noise-shaping energy above 22 kHz inflates the probe peak, so gain undershoots and loses level rather than clipping. `MEDIUM` on direction, `HIGH` on the mismatch. **E1-A measures it.** The 13 already-converted discs went through the mismatched path.

### F-3 — The universal proposed fix for F-1 is incomplete `[HIGH] [HIGH]`

Every prior document proposes `12` → `16` and stops. That removes the ID3 chunks but copies the original `ckDataSize` verbatim, so `FRM8` over-declares by the bytes removed. The spec requires that field to equal filesize − 12 and be even. The SACD decoder project documents readers failing on a `FRM8` length four bytes off.

**Required:** after the loop, seek to offset 4 and rewrite `ckDataSize` = `output.Length − 12` big-endian; assert even and matching. The assertion matters more than the rewrite — it stops a future edit silently reintroducing the bug.

### F-4 — Grace-killed process reported as exit 0 `[HIGH] [HIGH]`

`ProcessRunner.cs:202-203`: `if (graceKillOccurred) exitCode = 0;`. `Process.Kill` is asynchronous and best-effort; observing `100%` is not evidence Saracon flushed and closed. Compounding: `WaitForExitAsync(CancellationToken.None)` at `:197` observes exit but does not wait for the async `DataReceived` handlers to drain, so captured output may be truncated. The inactivity and timeout `return`s at `:148` and `:165` kill without reaping at all.

### F-7 — Cleanup reconstructs a path that can be wrong `[HIGH] [HIGH]`

`CleanupSuccesses:375` builds `Path.Combine(outputRoot, discName, discName)`, where `outputRoot` was computed once at `:64-68` from the **run-level** `multichannel` flag. Each disc's actual directory is built at `:142-150` from **per-disc** `extractMch = multichannel ?? probe.Value.HasMultichannel`.

Omit `-m`, let any disc auto-detect as multichannel, and its output lives under `... (Multichannel)` while cleanup looks under `... (Stereo)`, finds nothing, and silently `continue`s — DFF and XML never cleaned, while the ISO is still deleted if `--keep-iso` is absent.

### F-6 — Invalid-artifact recovery deletes the CUE `[HIGH] [MEDIUM]`

`PipelineOrchestrator.cs:273-282` removes `*.dff`, `*.cue`, `*.xml`. Once the CUE is gone, completeness cannot be assessed without a full re-extract. Adopt keep-CUE.

### F-5 — PCM size guard hardcodes stereo DSD64 `[MEDIUM] [HIGH]`

`EstimateExpectedPcmBytes`: `dsdBytes / (2822400.0 / 8.0 * 2)`. Verified correct for stereo DSD64 — Disc 3's `DSD ` chunk of 3,332,708,736 B ÷ 705,600 = 4,723.2 s, consistent with its CUE (last track starts 3,204.093 s). But `ProbeDsdAsync` already parses actual rate and channels and that data is never passed in. Multichannel under-estimates, so truncated output passes.

### F-8 — Extraction can collide with an existing DFF `[MEDIUM] [HIGH]`

Case B requires `HasValidDff && HasCue` (`:206`); artifact deletion happens only when `!HasValidDff` (`:228`). **Valid DFF + no CUE** falls through to case-A extraction without removing the existing DFF; `sacd_extract` writes beside it and Windows applies collision suffixes.

### F-9 — Split errors are discarded silently `[MEDIUM] [HIGH]`

`DsdConvertService.cs:209-212`: `if (splitResult.IsError) { continue; }` — no log, no capture. The aggregate check at `:228` then infers missing tracks from filenames; sox's stderr is lost.

### F-10 — Split success is not verified against the filesystem `[MEDIUM] [HIGH]`

`SoxService.SplitTrackAsync` returns `outputFlac` after checking exit code only. A sox exit-0 producing nothing counts as a successful track.

### F-11 — Last-track 30-second rule is an instance of F-23 `[MEDIUM] [HIGH]`

`FlacCompletenessChecker.cs:85-104` returns `IsComplete = false` when the final track is under 30 s. Not a format requirement; a disc with a genuinely short closing track can never be marked complete. Replace with "positive decoded duration" — **and only under the F-23 guard**, since the replacement has its own permanent-failure mode.

### F-12 — `LogPaths` process-global, not exception-safe `[MEDIUM] [HIGH]`

`Setup` at `:69`, `Reset` only on the normal path at `:111`; cancellation throws at `:82`, skipping both cleanup and reset.

### F-13 — Temp cleanup can replace the primary error `[MEDIUM] [HIGH]`

`CalculateGainAsync:165-169` and `ConvertFullDffAsync:285-289` delete temp dirs in `finally` unguarded.

### F-14 — Strip failure leaves partial output `[MEDIUM] [HIGH]`

Output created at `:92` before validation at `:94`; the non-exceptional failure path returns without deleting it.

### F-22 — ISOs are deleted by default `[MEDIUM] [HIGH]`

`SacdConvertCommand.Settings.KeepIso` defaults to `false`. Every invocation must pass `--keep-iso`. Combined with F-7, a disc can lose its ISO while its DFF is left behind.

### F-15 — Conversion disk-space check appears unwired `[MEDIUM] [MEDIUM]`

`DiskSpaceChecker.CheckSpaceForConversion` (8× factor) has no call site in the audio subset; only `CheckSpaceForExtraction` (4×) runs, once, at `:52`. Conversion is the more space-hungry phase. **Confirm repo-wide before acting** (§0).

### F-20 — ID3 detection and strip run twice per disc `[LOW] [HIGH]`

`RunConversionAsync` calls `HasId3Chunk` and `StripId3TagsAsync` on every invocation, and the pipeline invokes it twice per disc (gain probe + master). **Once F-1 is fixed this becomes two full 3.3 GB rewrites per disc**, landing on T15's wall clock. Strip once, reuse.

### F-16 — Probe harness appears orphaned `[LOW] [MEDIUM]`

`SacdProbeService`/`SacdProbeRunner` are not registered in `AudioSetup` and are referenced by nothing in the audio subset. `RealDffFixture.Path` hardcodes `C:\Temp\t.dff`. **Confirm repo-wide before deletion** (§0, C-7).

### F-17 — `ValidateContainedPath` is unsafe and appears unreachable `[LOW] [MEDIUM]`

Raw prefix comparison; `Disc 1` admits `Disc 10`. No call site in the audio subset. Wire it in correctly or delete it — **confirm repo-wide first** (§0).

### F-18 — Latent inactivity deadlock `[LOW-latent] [HIGH]`

If `inactivityTimeout` were ever passed, `exitTask` (built on `linkedToken`) transitions to Canceled, `while (!exitTask.IsCompleted)` exits without entering the body, and `:197` blocks on a process nobody killed. No caller passes it today.

### F-19 — `ProbeDsdAsync` uses encoding-sensitive reads and narrowing casts `[LOW] [MEDIUM]`

`BinaryReader.ReadChars(4)` decodes via the stream encoding; on binary data it can consume a variable number of bytes and desync the walk. `(int)chunkSize` narrows a `ulong`; the DSD chunk (3.33 GB) overflows `int`. In practice the loop breaks after `PROP` — which the spec requires before the sound-data chunk — so it degrades to a probe failure rather than a crash.

### F-21 — `DsdConvertCommand` advertises DSF input it cannot process `[LOW] [HIGH]`

Description says "Input DSF or DFF file"; `ProbeDsdAsync` parses `FRM8` only.

---

## 7. CPM network

Engineering hours; T14–T17 are wall-clock including Saracon runtime. **Project duration 33.5 h** (v3: 33.0; +0.5 for the F-23 guard).

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| T1 | Logging preconditions | 0.5 | — | 0.0 | 0.5 | 0.0 | 0.5 | **0** |
| T2 | Freeze baseline | 0.5 | T1 | 0.5 | 1.0 | 0.5 | 1.0 | **0** |
| T3 | 16-bit-only: retire `Bit24`/`Both` from the pipeline | 1.0 | T2 | 1.0 | 2.0 | 3.0 | 4.0 | 2.0 |
| T4 | Stripper: copy 16 + rewrite `ckDataSize` + partial cleanup | 3.0 | T2 | 1.0 | 4.0 | 7.5 | 10.5 | 6.5 |
| T6 | ProcessRunner: `TerminationReason`, real exit code, drain, reap | 3.0 | T2 | 1.0 | 4.0 | 1.0 | 4.0 | **0** |
| T8 | Gain-probe alignment + dead-code resolution | 2.0 | T3 | 2.0 | 4.0 | 10.5 | 12.5 | 8.5 |
| T5 | Standalone DFF strip harness | 2.0 | T4 | 4.0 | 6.0 | 10.5 | 12.5 | 6.5 |
| T7 | Saracon: gated grace accept, probed rate/channels, strip-once | 2.0 | T6,T4 | 4.0 | 6.0 | 10.5 | 12.5 | 6.5 |
| T9 | Artifact ownership: CUE preserved, cleanup path fix, temp owner | 2.5 | T6,T3 | 4.0 | 6.5 | 4.0 | 6.5 | **0** |
| T10 | `DiscState` + **reprocess guard** + split-error capture + existence checks | 3.5 | T3,T9 | 6.5 | 10.0 | 6.5 | 10.0 | **0** |
| T11 | Standalone harness: state matrix, guard, containment, termination | 2.5 | T10 | 10.0 | 12.5 | 10.0 | 12.5 | **0** |
| T12 | Build + editorconfig-as-error gate | 0.5 | T5,T7,T8,T11 | 12.5 | 13.0 | 12.5 | 13.0 | **0** |
| T13 | Tool-integration checks | 3.0 | T12 | 13.0 | 16.0 | 13.0 | 16.0 | **0** |
| T14 | Gate A: Disc 3 case-B → 4 FLACs | 2.0 | T13 | 16.0 | 18.0 | 16.0 | 18.0 | **0** |
| T15 | Gate B: Discs 4–9 case-A | 12.0 | T14 | 18.0 | 30.0 | 18.0 | 30.0 | **0** |
| T17 | Gate D: Ctrl+C semantics | 0.5 | T14 | 18.0 | 18.5 | 32.0 | 32.5 | 14.0 |
| T16 | Gate C: 20/20 rerun | 0.5 | T15 | 30.0 | 30.5 | 30.0 | 30.5 | **0** |
| T18 | Doc reconciliation, delete superseded plans | 2.0 | T16 | 30.5 | 32.5 | 30.5 | 32.5 | **0** |
| T19 | Journal + commit + handoff | 1.0 | T16,T17,T18 | 32.5 | 33.5 | 32.5 | 33.5 | **0** |

### Critical path

```
T1 → T2 → T6 → T9 → T10 → T11 → T12 → T13 → T14 → T15 → T16 → T18 → T19
```

```
          ┌─ T3 ─→ T8 ────────────────┐   2.0 / 8.5 float
          │   ║                        │
T1 ═→ T2 ═┼─ T4 ─→ T5 ────────────────┤   6.5 float
          │   ↘                        │
          └─ T6 ═╬═→ T7 ──────────────┤   6.5 float
              ║  ║                     │
              T6 ╩═→ T9 ═→ T10 ═→ T11 ═→ T12 ═→ T13 ═→ T14 ═→ T15 ═→ T16 ═→ T18 ═→ T19
                                                          └→ T17   14.0 float

  ═══ critical (zero float)      ─── has float
```

### Reading the network

**The critical path runs through process semantics, cleanup ownership, and state modelling — not through the ID3 bug.** T4, the most severe defect, carries 6.5 h of float. Urgent for correctness, not schedule-binding, because the real-media gates cannot start until the state machine is trustworthy.

**T3 is the sleeper at 2.0 h float.** It feeds both T8 and T10.

**T15 is 36 % of the schedule and cannot be compressed.** Six discs × two full Saracon passes. Parallelising is not an option — Saracon is a 2010 wxWidgets application with process-global registry/OLE state and a documented self-restart interference history.

**Compression, in order of value:**
1. Do T4/T5 first despite the float — cheap, independently verifiable, and a bad master wastes 12 h of T15.
2. Run T3, T4, T6 concurrently right after T2 — three workers, no shared files.
3. Fix F-20 (strip-once) inside T7 — with F-1 live, every disc gains two 3.3 GB rewrites straight onto T15's wall clock.
4. Schedule T17 (14 h float) inside T15's dead time.
5. **Do not compress T13.** Skipping tool-integration checks is what made every previous real-media run fail late.

---

## 8. Task detail

Per rule 7, each criterion must be able to fail, and must not fail on correct code.

### T1 — Logging preconditions
`youtube-quota-logging.md` tasks 1–2 move the Serilog sink to `PathResolver.RepoRoot/logs` and pin the file sink to `Debug+` independently of `--verbose`. **Every gate below reads Debug-level events.** If those have not landed, land them here.
**Accept:** run any `audio` command from `C:\Users\Lance`; `<repo>\logs\audio.jsonl` gains Debug entries; no log file appears in CWD.

### T2 — Freeze baseline
`dotnet build Toolbox.slnx --no-restore --no-incremental` → 0/0. `git tag backup/pre-sacd-v4`. Record `where.exe saracon sacd_extract sox`, ACP, and a media inventory.
**Accept:** three binaries resolve; build exit 0. The inventory is **recorded, not asserted** — it is a baseline for later diffing, not a pass condition.

### T3 — 16-bit only for the SACD pipeline (D-1)
Reject `Bit24` and `Both` at `SacdConvertCommand` validation with a clear message. **Keep `ForDsdRate` intact** — a one-line revert restores them if DEV-1 is revoked.

Delete only what becomes unreachable in the pipeline: `DsdConvertService.DeriveDirectoryAsync`; the `DerivedOnly` branch (`PipelineOrchestrator:159-175`); the `IsComplete`+`Both` re-derive block (`:179-201`); derived handling in `DeletePartialFlacs`; derived fields in `DiscAssessment` and `DurationCheckResult`.

**Do NOT delete `DsdConvertService.DeriveFlacAsync` or `ConvertFullDffAsync`** — `DsdConvertCommand.cs:101` and `:121` call them (C-16). Decide separately whether `dsd-convert` also drops `Bit24`/`Both`; it is a different command with a different contract.
**Accept:** `audio sacd-convert --format 24|both` fails with a clear message; no `derivedDir` string is constructed in the pipeline; `audio dsd-convert --help` still works; build clean.

### T4 — Stripper (F-1, F-3, F-14)
Copy 16 not 12. After the loop, seek to 4 and write `output.Length − 12` big-endian; assert even and matching. Validate before creating output; delete incomplete output in `finally` on every failure path. Descend into `PROP` when scanning, or log `Id3ScanScope=TopLevelOnly`.
**Accept:** T5 passes. On the real Disc 3 DFF the clean file is **1,806 bytes smaller** (3,332,711,216 → 3,332,709,410), contains zero `ID3 ` chunks, and its `ckDataSize` equals **3,332,709,398**.

### T5 — Strip harness
Standalone `.cs`, no test packages. Cases: synthetic DSDIFF with 4 ID3 chunks; odd-sized chunk needing pad; ID3 nested under `PROP`; truncated file; zero-size chunk. Plus the real Disc 3 DFF **streamed** — never `File.ReadAllBytes`, which throws above 2 GB on a 3.33 GB file and already bit a prior session.
**Accept:** all cases pass; real-DFF case reports ID3 count 4 → 0 and the exact byte delta above.

### T6 — ProcessRunner (F-4, F-18)
`TerminationReason { Exited, CallerCanceled, Timeout, InactivityTimeout, KilledAfterCompletionMarker, StartFailed }`. Preserve the real exit code. Explicit output-drain barrier before reading stdout/stderr. Every abnormal path kills the tree **and reaps** — the current inactivity and timeout `return`s at `:148`/`:165` skip the reap. Handle `exitTask` completing as Canceled before the loop body (F-18). Dispose `inactivityCts`.
**Accept:** T11 drives a controllable child through each reason; no path returns 0 for a killed process; no path returns without reaping.

### T7 — Saracon (F-4 acceptance, F-5, F-20)
Accept `KilledAfterCompletionMarker` only after output exists, is structurally valid, and passes the size guard. Thread probed rate and channel count into `EstimateExpectedPcmBytes`. **Strip ID3 once per disc and reuse the clean DFF** across gain probe and master.
**Accept:** Disc 3 estimate returns 4,723 s ± 1 s; a synthetic multichannel header no longer under-estimates; `Saracon.Id3Detected` appears **once** per disc in `audio.jsonl`, not twice.

### T8 — Gain-probe alignment and dead-code resolution (F-2, F-15, F-16, F-17)
**Step 1, before anything else:** repo-wide grep for `ValidateContainedPath`, `CheckSpaceForConversion`, `SacdProbeService`, `RunProbeAsync` across `src/` including `Program.cs` and all CLI branches (§0). Record the result. Only then decide delete-versus-wire for each.

Then: pass the resolved `DsdConversionSettings` into `CalculateGainAsync`; probe at the master's own rate and depth; delete `ProbeSampleRate`/`ProbeBitDepth`.
**Accept:** `DsdConvert.GainCalcComplete` and the master's `Saracon.ConvertStart` show the same rate and bit depth; the grep result is recorded with a disposition per member.

### T9 — Artifact ownership (F-6, F-7, F-13, F-22)
Publish this table into `AGENTS.md` and implement it. **Cleanup operates on exact paths returned by the per-disc result, never reconstructed** (F-7) — this requires `ProcessIsoAsync` to return the output directory alongside success, not just add the ISO path to a list.

| Artifact | Success | Failure / cancellation |
|---|---|---|
| ISO | delete only if `--keep-iso` absent **and** all outputs validate | retain |
| CUE | retain | **retain — never deleted** |
| DFF / `_clean.dff` | delete after full output validation | retain or quarantine |
| FLAC | retain | delete only for a deliberate re-split, logged |
| Master PCM | best-effort delete in `finally` | never masks the primary error |
| Temp files | run-owned unique path, publish on success | remove run-owned only |

**Accept:** forced probe failure leaves the CUE on disk; a disc forced to auto-detect as multichannel is cleaned in its own `(Multichannel)` directory; forced cleanup exception still surfaces the original conversion error.

### T10 — `DiscState` and the reprocess guard (F-23, F-8 – F-12)
Replace the boolean bag with `Complete | NeedsPrimaryConversion | NeedsExtraction | InvalidArtifacts | Failed`.

**Add the loop-breaker first** (F-23): persist a per-disc reprocess-attempt count; after N consecutive cycles that end in the same non-`Complete` verdict, transition to `Failed`, log the reason, retain all artifacts, and **stop reprocessing that disc**. `Failed` is terminal for the run and is reported in `PipelineResult`, not silently counted as success.

Then: handle **valid DFF + no CUE** explicitly, deleting the stale DFF before re-extracting (F-8). Capture per-track split errors keyed by track number instead of `continue` (F-9). Verify each split output exists and is non-empty (F-10). Replace the 30-second last-track failure with "positive decoded duration", warning only (F-11). Put `LogPaths.Reset` in `finally` or use a scope (F-12).
**Accept:** T11 matrix passes; a disc with a 20-second final track is marked `Complete`; a disc rigged to fail completeness deterministically reaches `Failed` after N attempts and starts zero processes on the next run; a forced sox failure names the failing track and its stderr.

### T11 — Standalone harness
State matrix, reprocess guard, containment, termination reasons, cleanup ownership.
**Accept:** all cases pass, exit 0; the guard case proves termination, not just detection.

### T12 — Build gate
**Accept:** 0 errors, 0 warnings; editorconfig violations are build errors.

### T13 — Tool integration
`sacd_extract -P` parse contract against real output; `sox` split/stats/duration; `saracon` on a short real DFF covering normal exit, completion marker, truncated output.
**Accept:** each contract asserted against actual output, not assumed.

### T14 — Gate A (Disc 3, case B)
Disc 3 has DFF + CUE and 0 FLACs. Expect 4 FLACs with CUE durations 1223.000 / 1158.373 / 820.720 / remainder.
**Accept:**
- 4 FLACs present; non-final durations within 0.01 s of the CUE-derived values
- `logs/audio.jsonl` contains one `DffMetadataStripper.Complete` line whose `size` is smaller than the source DFF — **verify from the log, not the filesystem**, because `CleanupSuccesses` deletes `_clean.dff` before the run ends (C-13)
- `Saracon.Id3Detected` appears exactly once
- master WAV passed the size guard (no `Saracon.OutputTooSmall`)
- ISO still present at its original size

### T15 — Gate B (Discs 4–9)
Full case-A path. Run detached; do not treat elapsed time alone as failure before the 1 h Saracon timeout.
**HALT rule:** on a `RegistryOleInit` signature (`Can't open registry key` / `Cannot initialize OLE` / `wxIdleWakeUpModule`) the agent session is blocked by design — stop, journal the signature, hand the command to the interactive terminal, resume at validation.
**Accept:** each disc's FLAC count equals its CUE track count; no leftover WAV/DFF for succeeded discs; all 6 ISOs retained; zero discs reach `Failed`.

### T16 — Gate C (rerun)
Immediate 20-disc re-run.
**Accept:** 20/20 logged as skipped at INFO. In `logs/audio.jsonl`: **20 `sacd_extract` probe invocations are expected and correct** — `ProcessIsoAsync:138` probes unconditionally before assessment (C-14). What must be zero is `sacd_extract` **extraction** invocations (args containing `-e`) and `saracon` process starts.

### T17 — Gate D (cancellation)
Ctrl+C during a Saracon conversion.
**Accept:** reported as cancellation; no `ProcessRunner.Timeout` entry for that run; no orphaned `saracon.exe` in the process list; exit within seconds; next run resumes rather than restarting from scratch.

### T18 — Doc reconciliation
Update `src/Services/Audio/AGENTS.md` — it omits `DiscOutputInspector`, `FlacCompletenessChecker`, `LogPaths`, `SacdProbeService`, `SacdProbeRunner`, `RealDffFixture`, and misdescribes the state model. Record DEV-1. Delete every file listed in §0.
**Accept:** one SACD plan in `docs/`; `AGENTS.md` file list matches disk exactly.

### T19 — Journal and handoff
Append findings with confidence tags, **including the C-2 correction that historical `stripped/` rows were never stripped.** Do not delete historical rows.
**Accept:** every `## Findings` entry carries a tag; no UTF-8 claim restated as settled.

---

## 9. Experiment E1-A — deferred, ~30 minutes

**One question:** how much level did the probe/master rate mismatch (F-2) cost, and were the 13 already-converted discs gained wrongly?

**E1-B (one-pass optimisation) is cancelled**, not deferred. It requires a 24-bit intermediate; D-1 forbids 24-bit. Boosting a 16-bit file by +5 dB lifts its dither floor from ≈ −96 to ≈ −91 dBFS. Do not revive without revoking D-1.

### Build the dummy file

Truncate the real Disc 3 DFF to ~60 s of DSD — real content, ~1 min per Saracon pass. 60 s stereo DSD64 = 60 × (2,822,400 ÷ 8) × 2 = **42,336,000 bytes**.

1. Copy bytes 0–15 (`FRM8` + size + `DSD `).
2. Copy `FVER` (16 B total) and `PROP` (100 B total) verbatim.
3. Write a `DSD ` chunk header with size 42,336,000, then the first 42,336,000 payload bytes.
4. Stop — omit `DIIN`, `COMT`, and all `ID3 ` chunks.
5. Rewrite `ckDataSize` at offset 4 = filesize − 12 = **42,336,132**. Assert even.

Expected total size **42,336,144 bytes**. Save as `C:\Temp\e1-dummy.dff`, record SHA-256. This reuses T4's header-rewrite code, which is the point.

### Measure

```
saracon -c d2p -r 88200 -f wav -n 24bit -d tpdf -g 0.00 -T -V all -t <out> e1-dummy.dff
saracon -c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00 -T -V all -t <out> e1-dummy.dff
sox <88k.wav> -n stats
sox <44k.wav> -n stats
```

`delta = peak88 − peak44`.

| Result | Meaning |
|---|---|
| `delta > 0` | Current gain is **conservative** — `delta` dB of level lost on all 13 finished discs. Safe but wasteful. Confirms F-2's reasoned direction. |
| `delta < 0` | Current gain is **optimistic and can clip.** F-2 becomes urgent, T8 moves onto the critical path, and the 13 finished discs need checking for clipping. |
| `\|delta\| < 0.05` | Immaterial for this material. T8 becomes hygiene. |

### Report template

````
=== SACD E1-A REPORT ===
date:
saracon version:            (first line of any saracon run)
sox version:                (sox --version)
ACP:                        (reg query HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage /v ACP)

--- dummy file ---
source dff:                 C:\Users\Lance\Desktop\Music\...\Disc 3.dff
dummy path:                 C:\Temp\e1-dummy.dff
dummy size bytes:           (expect 42336144)
dummy sha256:
dummy DSD chunk bytes:      (expect 42336000)
dummy ckDataSize field:     (expect 42336132)
ckDataSize even:            yes / no
ID3 chunks in dummy:        (expect 0)

--- probe rate mismatch ---
peak @ 88200/24 0dB:                     dB     (sox Pk lev dB)
peak @ 44100/16 0dB:                     dB
delta (peak88 - peak44):                 dB
gain that WOULD be applied (88.2 probe): (-0.5 - peak88, cap +6)  =        dB
gain that SHOULD be applied (44.1 probe):(-0.5 - peak44, cap +6)  =        dB
level left on table (or clipped by):     dB

--- timings ---
88.2/24 pass wall clock:                 s
44.1/16 pass wall clock:                 s

--- anomalies ---
(saracon warnings, non-zero exits, size-guard trips, unexpected output filenames)
=== END ===
````

---

## 10. Explicitly out of scope

Design findings A1, A4, A9, A10, A13, A15–A18 (god classes, CLI layering, metadata SRP, harness decomposition) are **maintainability**, not correctness. Attempting them before T16 risks the working 13-disc library for zero functional gain. Revisit after a clean 20/20 run.

A14 (relocate the probe) is closed — rule 10.

Also excluded: any SoX-based DSD→PCM replacement (SoX cannot read DSD); any Saracon replacement; any ffmpeg gain measurement (§3); any retry inside `SaraconService`; any repeat of the Tree A → Tree B migration (already executed); **any 0.0065 s click-trim step** (rule 3); the one-pass gain optimisation (§9); 24-bit output from the SACD pipeline (D-1); new NuGet packages or test frameworks; YouTube/Azure/dashboard work beyond T1.