# SACD Pipeline — Completion Brief v2

**Version:** 2.0 — corrects v1, which was built on `progress.md` alone
**Date:** 2026-08-15
**Baseline:** working tree at `d4db355`
**Evidence base:** 24 audio source files, `new-mega-plan.md`, **and all 44 artifacts in `.superpowers/sdd/new-mega-plan/`** (briefs, reports, review packages) — the last of which v1 did not read

**Total: 31 tasks, 173 subtasks, across 7 phases.**

---

## 0. What v1 got wrong

v1 was written from `progress.md`. That ledger is incomplete and, in one place, misleading. Reading the task reports and review packages changes three conclusions:

### 0.1 T11 was executed. `progress.md` has no line for it.

`task-11-report.md` records **74 passing cases** across twelve categories, a clean build, and exit code 0. v1 stated "T11–T19 not started" and scheduled T11 as new work. That was wrong.

### 0.2 The T11 harness asserted two of the defects as correct behaviour

This is the material finding. Two of its passing guard cases were:

- *"Complete can't remove Failed (sticky)"*
- *"different verdict resets count"*

Those are precisely the two guard defects the compliance audit raised — the unrecoverable lockout and the oscillation escape. The harness did not miss them. It **encoded them as expected behaviour and passed.**

v1 warned that the harness "must not be written before P1.2 lands, or it will encode the defective recording semantics into the regression suite." That has already happened. The remediation therefore is not "write a harness" but **"decontaminate an existing suite, then rebuild it durably"** — the harness was deleted after passing (`task-11-report.md`, "Artifacts deleted: T11Driver/"), so the assertions survive only as specification in the report, which is where they will be copied from if someone rebuilds naively.

### 0.3 Two defects are deliberate, documented decisions — not oversights

The compliance audit treated guard stickiness and the attempt-count off-by-one as bugs. The artifacts show both were chosen:

- `task-10.2-report.md`: *"`Failed` remains sticky until JSON removal."* Deliberate, and manual JSON deletion is the intended recovery path.
- `task-10.3-report.md` review finding #2, severity Important: *"Transition must happen before processing"* → implemented as `c + 1 >= MaxConsecutiveCount` blocking before `ProbeAsync`. A reviewer asked for this.

**This does not make them correct.** Stickiness plus the pre-work verdict recording (which nobody noticed, and which is a genuine bug) produces permanent lockout of healthy discs. The reviewer's "block before processing" request was satisfied in a way that also reduced three attempts to two. But these now require **decision reversal with recorded rationale**, not silent bug-fixing — someone deliberately chose them and the reasoning must be addressed rather than overwritten.

### 0.4 Several acceptance criteria were satisfied statically, never observed

A theme across the reports, and a direct violation of the governing rule that criteria must be measurable:

- `task-3-report.md`: *"SACD `--format 16` media conversion was not run."*
- `task-7-report.md`: *"Real Saracon full conversion remains unexecuted by design."*
- `task-8-report.md`: *"Runtime gain/master log equality was verified **statically** through shared resolved settings and clean build."* — T8's acceptance was that `GainCalcComplete` and `Saracon.ConvertStart` show the same rate and depth **in the log**. That was never observed.
- `task-9-report.md`: *"Runtime media gates remain outside T9 and were not run."*

Phase 4 now includes a task that observes these at runtime rather than inferring them.

### 0.5 Corroborations worth carrying forward

- `task-2-report.md` flags the ISO layout as `Disc N\Disc N.iso` (double-nested) with **two sibling trees** — ISOs under `Karajan 1970-79 Berlin\`, output under `Karajan 1970-79 Berlin (Stereo)\`. This independently corroborates the fresh-disc crash: `FindDffDir` resolves to `…(Stereo)\Disc N\Disc N`, which does not exist before extraction.
- `task-1-report.md` flags that `OutputDir` renders as a mangled temp-root label in `Saracon.ConvertStart`. Every Phase 5 gate verifies from the log, so this must be fixed or accounted for.
- `task-11-report.md` flags `TerminationReason.StartFailed` as never exercised, and `internal` members tested through reflection.

---

## 1. Governing rules

Unchanged from v1 and binding on every task.

1. **Pipeline shape is fixed:** `sacd_extract` (single-file Edit Master + CUE) → strip ID3 → measure peak → Saracon DSD→PCM → split at CUE `INDEX 01` → tag FLAC.
2. **16-bit / 44.1 kHz only** from the SACD pipeline. `dsd-convert` is a separate contract.
3. **Saracon is the only DSD→PCM converter.** SoX cannot read DSD; ffmpeg must never measure gain.
4. **Saracon runs twice per disc.** Not an inefficiency to remove.
5. **No 0.0065 s click trim.**
6. **Gain never clips:** peak measured, applied below it with 0.5 dB headroom, capped +6 dB.
7. **No new packages, no test frameworks.** Harnesses are plain `.cs` with a `Main`.
8. **Repo constraints:** one class per file; no inline comments; `ErrorOr` railway style; structured logging only; editorconfig violations are build errors.
9. **Every acceptance criterion must be capable of failing, must not fail on correct code, and must be *observed* — not inferred from a clean build or shared settings.**
10. **Partial completion is failure.** No deferred-minor state.
11. **New:** a harness assertion that encodes current behaviour is not evidence. Every assertion must trace to a requirement in this brief or the guide, not to what the code happens to do.

### Reporting contract

Per subtask: command or diff applied, raw observed output, `PASS` / `FAIL` / `BLOCKED`. `BLOCKED` requires the blocking signature quoted and an owner named. **Reports must also record any assertion deliberately inverted from a prior harness, with the prior text quoted**, so the reversal is auditable.

---

# PHASE 0 — Ground truth (5 tasks, 25 subtasks)

## P0.1 — Snapshot and safety net (5 subtasks)

1. `git tag backup/pre-completion-brief-v2` at `d4db355`; record the SHA.
2. `git status --porcelain` — record every dirty file; do not stash or discard.
3. Copy the full output tree to a **different physical volume**; confirm byte totals match.
4. Record SHA-256 for one FLAC per disc (13 canaries) for Phase 5 tamper detection.
5. Confirm all 20 ISOs present with sizes; record the manifest, noting the `Disc N\Disc N.iso` nesting.

**Accept:** tag exists; byte totals equal; 13 canaries recorded; 20 ISOs manifested.

## P0.2 — Guard state audit (4 subtasks)

`Failed` is sticky by design and recovery is manual JSON deletion. Prior driver runs may have left entries.

1. Dump `state/audio/sacd-guard.json`; if absent, record that. Note that T10.2, T10.3 and T11 reports each claim cleanup after their driver runs.
2. Per entry record ISO path, `Verdict`, `ConsecutiveCount`, `UpdatedAt`.
3. Classify each `Failed` entry against on-disk output as genuine-failure or false-lockout.
4. Archive to `state/audio/sacd-guard.pre-brief.json`; delete the live file.

**Accept:** every entry classified with on-disk evidence quoted; live file removed; archive retained.

## P0.3 — Falsified-completion audit (6 subtasks)

Re-derive each T1–T11 claim against source. Table: claim, source location, `CONFIRMED` / `FALSE` / `PARTIAL` / `STATIC-ONLY`.

1. **T1** — sink at `state/logs`; file sub-logger explicitly Verbose and not shadowed by the root `LevelSwitch`. Run one command from `C:\Users\Lance`. Also record the mangled temp-root label defect and the Seq-sink level deferral.
2. **T3** — rejection of `24`/`both`; `ForDsdRate` intact; `dsd-convert` builds and runs. Mark the never-run media conversion as `STATIC-ONLY`.
3. **T4** — copy-16, `ckDataSize` rewrite, read-back verify, `finally` cleanup, `PROP` descent. Separately enumerate every reachable `throw` and whether any caller catches it.
4. **T6/T7** — six `TerminationReason` values; no path returns 0 for a killed process; every abnormal path reaps; `inactivityCts` disposed; estimator receives probed rate and channels. Mark the unexecuted real conversion `STATIC-ONLY`.
5. **T8/T9** — gain probe uses resolved settings; `ProbeSampleRate`/`ProbeBitDepth` gone; `CheckSpaceForConversion` wired at both sites and ordered before `DeletePartialFlacs` in case B. Mark runtime log equality `STATIC-ONLY`.
6. **T10/T11** — record F-9, F-10, F-11 as `FALSE` with line evidence; record the two T11 assertions that blessed defective guard behaviour, quoting them.

**Accept:** table covering T1–T11; every `FALSE`/`PARTIAL`/`STATIC-ONLY` row maps to a task ID in this brief.

## P0.4 — Media risk inventory (4 subtasks)

1. Decode final-track duration for all 14 discs with CUEs (`sox --i -D`).
2. Flag any under 30 s — these trip the live rule today.
3. Record output-directory existence per ISO, separating fresh discs from re-processed ones.
4. Record CUE track count per disc as the Phase 5 expected-FLAC oracle.

**Accept:** per-disc table with all four columns.

## P0.5 — SDD artifact reconciliation (6 subtasks) — **NEW in v2**

The ledger and the reports disagree. Future readers must not repeat v1's error.

1. Add the missing T11 line to `progress.md` with its commit or a note that the harness was deleted without one.
2. Cross-check every task's `progress.md` line against its report's `Status:` field; record discrepancies.
3. Extract every "Concerns" item from all eleven reports into one open-items register.
4. Map each open item to a task in this brief, or mark it formally closed with rationale.
5. Extract every review finding marked `Minor` and kept (e.g. T10.3 finding #7, duplicate `Failed` lookup) and confirm each is still an acceptable decision.
6. Record which reports claim driver cleanup of `state/audio/sacd-guard.json`, and reconcile against P0.2's actual finding.

**Accept:** one register containing every concern and kept-minor from all reports, each mapped to a task or an explicit closure.

---

# PHASE 1 — Remediation (7 tasks, 38 subtasks)

## P1.1 — Fresh-disc crash (4 subtasks)

`DeleteFlacsInDir` enumerates without an existence check. On a fresh disc `FindDffDir` returns a non-existent path, state is `NeedsExtraction`, and the deleter runs *before* extraction. `Directory.GetFiles` throws; the inner `try/catch` covers only `File.Delete`; nothing up to `RunAsync` catches it. **The whole batch aborts.** Corroborated by `task-2-report.md`'s two-sibling-tree observation.

1. Add an existence guard as the first statement of `DeleteFlacsInDir`.
2. Audit every directory enumeration in `src/Services/Audio`; record each with a disposition. Every sibling already guards — this is the sole exception.
3. Add a per-disc exception boundary in `RunAsync` so an unexpected throw fails one disc and the batch continues.
4. Confirm the boundary does not swallow `OperationCanceledException`.

**Accept:** a fresh temp tree reaches the extraction call without throwing; an injected `IOException` fails one disc and the loop continues; Ctrl+C still stops the run.

## P1.2 — Reprocess guard semantics (11 subtasks) — **expanded in v2**

Three defects, two of which are documented decisions requiring explicit reversal.

**Genuine bug:** both success paths record `assessment.State` — the pre-work verdict — so success and failure are indistinguishable to the counter.

**Decision to reverse (T10.2):** `Failed` is sticky until manual JSON removal.

**Decision to re-scope (T10.3 finding #2):** transition fires before the Nth attempt, so N=3 yields two attempts.

1. Record the reversal rationale for stickiness in the task report, quoting `task-10.2-report.md`, before changing code.
2. Record the re-scoping rationale for the off-by-one, quoting `task-10.3-report.md` finding #2, and confirm the reviewer's actual requirement — *a `Failed` disc starts no process* — remains satisfied.
3. Change both success paths to record the **cycle outcome**, not `assessment.State`.
4. Count **consecutive non-`Complete` outcomes regardless of verdict**, so oscillation terminates.
5. Move the transition so N attempts execute before blocking: with N=3, attempts 1–3 run and attempt 4 is refused.
6. Make `Failed` clearable by a genuine `Complete` outcome.
7. Add `--reset-guard` to `SacdConvertCommand`, logging each cleared entry.
8. Log every transition at `Warn` with ISO, previous verdict, new verdict, count.
9. Resolve T10.3 kept-minor #7 (duplicate `Failed` lookup in `RunAsync` and `ProcessIsoAsync`) — keep with documented reason or remove.
10. Confirm guard writes are atomic enough that an interrupted write cannot produce unparseable JSON — `LoadAsync` treats `JsonException` as "reset to empty", which would silently erase every lockout.
11. Preserve the nine cancellation guards added by T10.3 review-fix-2; confirm no state write occurs after a cancellation request.

**Accept:** P3.2 suite passes with **inverted** assertions; three consecutive successes never accumulate; a deterministic failure runs exactly three times then is refused; alternating verdicts still terminate; `--reset-guard` restores a `Failed` disc; an interrupted write does not erase the file.

## P1.3 — Last-track completeness rule (4 subtasks)

The 30-second failure is still live. No format requirement imposes a minimum track length.

1. Replace `< 30.0` failure with `<= 0`.
2. Downgrade the short-track observation to `Warn` with the measured duration; do not fail completeness.
3. Confirm the `else if` branch still fires only for the final track — it is the `else` of the `Duration is { }` test, and the final CUE track's `Duration` is `null` by construction.
4. Confirm the non-final ±2.0 s tolerance is untouched.

**Accept:** a 20-second final track assesses `Complete`; a 0-byte final track assesses non-`Complete`; a non-final track off by 3 s still assesses non-`Complete`.

## P1.4 — Split error capture (5 subtasks)

The bare `continue` destroys sox's stderr at the moment it is produced.

1. Capture into a `Dictionary<int, string>` keyed by track number.
2. Log each failure at `Warn` with track number, output path, error text.
3. Include per-track reasons in the aggregate error.
4. Confirm the aggregate still names missing track numbers.
5. Confirm a mid-loop failure does not prevent remaining tracks being attempted.

**Accept:** an injected failure on track 7 of 19 produces a `Warn` naming track 7 and its stderr, and the aggregate error carries both list and reasons.

## P1.5 — Split output verification (4 subtasks)

`SplitTrackAsync` returns on exit code alone; the count check counts list entries, not files.

1. Confirm the output file exists after the exit-code check.
2. Confirm non-zero length.
3. Return a descriptive `ConversionFailed` naming the expected path when either fails.
4. Apply the same check to `DeriveFlacAsync` and `ConvertDsdToFlacAsync`; record any other method returning an unverified path.

**Accept:** a stub exiting 0 writing nothing produces an error; the real Disc 3 split still succeeds.

## P1.6 — ISO deletion gating (5 subtasks)

`outputsValidated` checks only directory existence. With P1.5 unfixed this is the path that destroys the only recoverable source.

1. Require FLAC count equal to CUE track count.
2. Require every FLAC non-zero length.
3. Require the CUE present.
4. Log the validation outcome per disc at `Info` before any deletion decision.
5. Confirm `--keep-iso` short-circuits regardless.

**Accept:** a disc with one zero-length FLAC retains its ISO with the reason logged; a valid disc without `--keep-iso` deletes its ISO; `--keep-iso` retains in both cases.

## P1.7 — Stripper exception containment (5 subtasks)

Strict input validation plus a rethrowing `HasId3Chunk` with no catching caller means one odd DFF aborts the batch — the exact input class the `ckDataSize` fix exists to repair.

1. Convert `HasId3Chunk` to `ErrorOr<bool>`, or wrap it so callers receive a value.
2. Wrap `PrepareDffAsync` so stripper failure degrades to a per-disc error.
3. Keep the validations but classify: mismatched size on **input** warns and attempts repair; on **output** remains a hard failure.
4. Confirm `finally` partial-output cleanup fires on every failure path including the new ones.
5. Confirm `OperationCanceledException` stays excluded from the catch filter.

**Accept:** a synthetic DFF with `ckDataSize` four bytes short fails that disc and the batch continues; a well-formed DFF strips with the same byte delta; cancellation during a strip still cancels.

---

# PHASE 2 — Defects no plan scheduled (3 tasks, 13 subtasks)

## P2.1 — `ProbeDsdAsync` hardening (5 subtasks)

1. Replace every `ReadChars` with `ReadBytes` plus `Encoding.ASCII.GetString`.
2. Replace `(int)` narrowing casts with `long`/`ulong` and `Stream.Seek` for skipping.
3. Bound seeks so a corrupt size cannot pass end-of-file.
4. Confirm the walk still breaks after `PROP` on real files.
5. Consider routing through the `DffMetadataStripper` chunk reader rather than maintaining a second walker; if not, record why.

**Accept:** real Disc 3 probe returns 2822400 Hz / 2 ch unchanged; a corrupt oversized chunk size returns an error rather than throwing or over-allocating.

## P2.2 — CLI contract truthfulness (4 subtasks)

`task-10.4-report.md` reproduces `--help` output showing `Output format: 16 (default), 24, both` — shipped after review, while the command rejects everything but 16.

1. Correct the `sacd-convert` format description to 16-bit only.
2. Correct the `dsd-convert` input description to DFF only, or add DSF parsing — decide and record.
3. Confirm the rejection message names the supported value.
4. Confirm `--keep-iso` help states the destructive default clearly.

**Accept:** `--help` for both commands matches actual behaviour on every option.

## P2.3 — Probe harness disposition (4 subtasks)

`SacdProbeService` is DI-registered but `RunProbeAsync` has no caller; `RealDffFixture` hardcodes `C:\Temp\t.dff` in the shipped assembly.

1. Decide: expose behind a CLI command, or remove. Record the decision.
2. If retained, replace the hardcoded path with configuration defaulting to absent.
3. If retained, guard the entry point so a missing fixture reports a precondition failure.
4. If removed, delete the three files and the registration together; confirm a clean build.

**Accept:** either a working CLI path, or the three files and registration gone; no unreferenced public member remains.

---

# PHASE 3 — Durable verification (5 tasks, 33 subtasks)

## P3.1 — Harness infrastructure (5 subtasks)

Prior harnesses (T5, T10.2, T11) were built, passed, then deleted. This one is **committed and runnable on demand**.

1. Plain `.cs` entry point, no test packages, referencing the production project.
2. Assertion helpers with failure output naming the case.
3. Temp-workspace creation and teardown, with a hard assertion the path is under the system temp root — no real media mutation.
4. Controllable child-process stub: configurable exit code, output volume, delay, and a mode ignoring termination.
5. Non-zero exit on any failure; per-case summary. Configure `Telemetry` at `Fatal` to suppress output while avoiding the null-logger crash noted in `task-11-report.md`.

**Accept:** harness runs, prints per-case results, exits 0 clean and non-zero when forced to fail. **Committed to the repo, not deleted.**

## P3.2 — Regression-suite decontamination (7 subtasks) — **NEW in v2**

The T11 suite passed 74 cases while asserting two defects as correct. Its report is the specification anyone rebuilding will copy from.

1. Quote both blessed assertions verbatim in the task report: *"Complete can't remove Failed (sticky)"* and *"different verdict resets count"*.
2. Write the **inverted** assertions: a genuine `Complete` outcome clears `Failed`; a differing non-`Complete` verdict does **not** reset the count.
3. Annotate `task-11-report.md` in place, marking those two rows superseded and naming this brief — do not delete the file.
4. Re-derive every other T11 case against a requirement in this brief or the guide, and discard any that merely restates current behaviour.
5. Add the case T11 recorded as unexercised: `TerminationReason.StartFailed`.
6. Resolve the reflection dependency on `internal` members (`GetFlacsByTrackNumber`, `FindDffDir`) — `InternalsVisibleTo` or widened visibility, decided and recorded.
7. Confirm no assertion in the new suite was carried over without a requirement citation.

**Accept:** both inverted assertions pass; `task-11-report.md` annotated; every retained case carries a requirement citation.

## P3.3 — State matrix and guard termination (8 subtasks)

1. Fresh directory, no CUE/DFF/FLACs → `NeedsExtraction`, and no throw (P1.1).
2. Valid DFF, no CUE → `InvalidArtifacts`, stale DFF deleted, nothing else removed.
3. Valid DFF, CUE, zero FLACs → `NeedsPrimaryConversion`.
4. Valid DFF, CUE, partial FLACs → `NeedsPrimaryConversion`.
5. CUE, all FLACs, durations correct, no DFF → `Complete`.
6. Final track 20 s → `Complete` (P1.3).
7. Final track 0 bytes → non-`Complete`.
8. Guard termination **through the orchestrator, not `ReprocessGuard` in isolation** — this is why T11 missed the pre-work-verdict bug: it fed verdicts by hand. Three consecutive non-`Complete` outcomes → `Failed` on the fourth encounter with zero process starts; three consecutive successes → no accumulation; alternating verdicts → still terminates; `--reset-guard` restores processing.

**Accept:** all eight pass; case 8 drives `ProcessIsoAsync`, not `RecordAsync` directly, and proves termination.

## P3.4 — Stripper suite (7 subtasks)

1. Synthetic DSDIFF with four top-level ID3 chunks → removed; `ckDataSize` = filesize − 12; even.
2. Odd-sized chunk requiring a pad → padding preserved, output even.
3. ID3 nested under `PROP` → removed and the `PROP` size field rewritten.
4. Truncated file → descriptive error, no partial output.
5. Zero-size chunk mid-walk → descriptive error, no partial output.
6. Input `ckDataSize` four bytes short → per P1.7, warns and repairs or fails that file only.
7. Real Disc 3 DFF **streamed** — never `File.ReadAllBytes` (throws above 2 GB on a 3.33 GB file). Assert ID3 4 → 0, output exactly 1,806 bytes smaller (3,332,711,216 → 3,332,709,410), `ckDataSize` = 3,332,709,398.

**Accept:** all seven; case 7 runs against real media with exact figures.

## P3.5 — ProcessRunner termination suite (6 subtasks)

1. Exit 0 → `Exited`, code 0, full stdout captured.
2. Exit 3 → `Exited`, code **3 preserved**, stderr captured.
3. Caller cancellation → `CallerCanceled`, tree killed and reaped, no orphan.
4. Wall-clock timeout → `Timeout`, killed and reaped, code not laundered to 0.
5. Completion marker then hang → `KilledAfterCompletionMarker`, killed and reaped; caller-side acceptance still requires output validation.
6. High-volume stdout then immediate exit → drain barrier holds; output complete.

**Accept:** all six; no case returns exit 0 for a killed process; no orphan remains.

---

# PHASE 4 — Build, contracts, and observation (3 tasks, 17 subtasks)

## P4.1 — Build and style gate (4 subtasks)

1. `dotnet build Toolbox.slnx --no-restore --no-incremental` → 0 errors, 0 warnings.
2. Confirm editorconfig violations are build errors.
3. Close the deferred formatting nit in `SacdConvertCommand`.
4. Confirm no test package or new dependency entered project files during Phases 1–3.

**Accept:** clean build; a deliberate style violation fails it; project files otherwise unchanged.

## P4.2 — Tool integration contracts (6 subtasks)

1. `sacd_extract -P` on a real ISO → confirm the parse contract including multichannel detection.
2. `sox --i -D` → duration parsing on a real FLAC.
3. `sox ... -n stats` → peak regex against real output, including negative and `-0.00` cases.
4. `sox ... trim` → split offsets, and final track trims to EOF.
5. `saracon` on a short real DFF → normal exit, completion-marker path, and a truncated output tripping the size guard.
6. Record each tool's version string.

**Accept:** each contract asserted against captured real output, quoted.

## P4.3 — Runtime observation of static-only criteria (7 subtasks) — **NEW in v2**

Closes the four acceptance criteria satisfied by inference rather than observation (§0.4).

1. **T8** — run one real conversion and confirm from `state/logs/audio.jsonl` that `DsdConvert.GainCalcComplete` and the master `Saracon.ConvertStart` show the **same** rate and bit depth. This was T8's stated criterion and was never observed.
2. **T3** — run one real `--format 16` SACD conversion end to end.
3. **T7** — run one real full Saracon conversion, confirming the estimator against actual output size.
4. **T9** — observe artifact ownership at runtime: CUE retained through a forced probe failure; temp cleanup exception not masking the primary error.
5. Fix or formally account for the mangled temp-root label in `Saracon.ConvertStart` (`task-1-report.md`) — Phase 5 gates read this log.
6. Confirm the Seq sink level deferral from T1 is either intended or corrected.
7. Confirm no gate in Phase 5 depends on a log field that renders unreadably.

**Accept:** each of the four criteria observed in a real log with the entry quoted; log rendering defects fixed or explicitly accounted for.

---

# PHASE 5 — Real media gates (5 tasks, 29 subtasks)

Ascending risk. **A gate failure stops the phase.**

**HALT rule, all tasks:** on a `RegistryOleInit` signature (`Can't open registry key`, `Cannot initialize OLE`, `wxIdleWakeUpModule`) the agent session is blocked by design. Stop, quote the signature, hand the command to the interactive terminal, resume at validation.

## P5.1 — Gate A: Disc 3, case B (7 subtasks)

Disc 3 has DFF and CUE, zero FLACs. Expected durations 1223.000 / 1158.373 / 820.720 / remainder.

1. Run with `--keep-iso`.
2. Four FLACs; non-final durations within 0.01 s.
3. Exactly one `DffMetadataStripper.Completed` with `outputBytes` < `inputBytes` — **from the log, not the filesystem**; cleanup deletes `_clean.dff` before the run ends.
4. `Saracon.Id3Detected` exactly once, not twice — proves the strip is hoisted.
5. No `Saracon.OutputTooSmall`.
6. ISO present at original size; CUE present.
7. Guard records `Complete` or no entry.

## P5.2 — Gate B: Disc 4 canary, case A (6 subtasks)

First true exercise of the fresh-disc path. ~1 hour.

1. Confirm no output directory exists beforehand (P0.4).
2. Run with `--keep-iso`; confirm extraction is reached **without throwing**.
3. FLAC count equals the P0.4 CUE oracle.
4. No leftover WAV or DFF.
5. ISO retained; CUE present.
6. Guard records `Complete`, not `NeedsExtraction` — the direct field test of P1.2.

**Subtask 6 failing means P1.2 is wrong and Phase 5 stops.**

## P5.3 — Gate C: Discs 5–9 (6 subtasks)

~10 hours, detached. Do not treat elapsed time alone as failure before the one-hour Saracon timeout.

1. Run all five with `--keep-iso`.
2. Per disc, FLAC count equals CUE track count.
3. Zero discs reach `Failed`.
4. No leftover WAV or DFF for succeeded discs.
5. All five ISOs retained; all five CUEs present.
6. Re-verify the 13 canary hashes — **prior output must be untouched.** A mismatch is a stop-everything event.

## P5.4 — Gate D: full 20-disc rerun (5 subtasks)

1. Immediately re-run all 20.
2. 20/20 logged as skipped at `Info`.
3. **20 `sacd_extract` probe invocations are expected and correct** — the orchestrator probes unconditionally before assessment. Zero **extraction** invocations (arguments containing `-e`).
4. Zero `saracon` process starts.
5. Guard contains no entries, or only `Complete`-cleared state.

## P5.5 — Gate E: cancellation (5 subtasks)

1. Ctrl+C during the Saracon master pass.
2. Reported as cancellation, not timeout — no `ProcessRunner.Timeout` entry.
3. No orphaned `saracon.exe`.
4. Exit within seconds, not after the one-hour timeout.
5. Next run resumes; the interrupted disc did not accumulate a guard count it should not have (T10.3 review-fix-2 claims no state write occurs after cancellation — this observes it).

---

# PHASE 6 — Closure (3 tasks, 18 subtasks)

## P6.1 — Documentation reconciliation (6 subtasks)

1. Update `src/Services/Audio/AGENTS.md` so its file list matches disk — it predates `DiscState` and `ReprocessGuard`.
2. Document the state model, guard semantics **as rebuilt**, and the `--reset-guard` recovery path replacing manual JSON deletion.
3. Publish the artifact ownership table with success and failure columns.
4. Record the 16-bit deviation from the guide, its cost, and the condition for revoking it.
5. Delete superseded plans: `sacd-pipeline-rescue.md`, `2026-08-12-sacd-consolidated.md`, `2026-08-09-sacd-death-loop-v2-design.md`, `2026-08-14-audio-design-assessment.md`, both logging-audit files, `2026-08-10-process-runner-streaming.md`, `sacd=extractopn.md`, `sacdprobe-editorconfig.md`.
6. Retain `sacd-guide.md`, `sacd-probe-journal.md`, `toolbox-flatline.md`, the YouTube plans, **and the entire `.superpowers/sdd/new-mega-plan/` set** — annotated, never deleted; they are the audit trail that exposed §0.

## P6.2 — Journal (5 subtasks)

1. Append findings with confidence tags and basis; tags may not be dropped or upgraded when restated.
2. Record that historical `stripped/` journal rows were byte-identical to `raw/` rows — the stripper was inert, so inferences from comparing them are void. **Do not delete historical rows.**
3. Record that the UTF-8 / ACP 65001 root cause was rejected and must not be restated as settled.
4. Record the P0.3 falsified-completion table and the P0.5 open-items register.
5. Record every `BLOCKED` subtask with its signature.

## P6.3 — Experiment E1-A (7 subtasks)

Quantifies what the historical probe/master rate mismatch cost, and whether the 13 converted discs were gained wrongly. **The one-pass optimisation is not revived** — it needs a 24-bit intermediate, which rule 2 forbids.

1. Build the dummy: truncate the real Disc 3 DFF to 60 s of DSD — 60 × (2,822,400 ÷ 8) × 2 = **42,336,000 bytes**. Copy bytes 0–15, then `FVER` (16 B) and `PROP` (100 B) verbatim, then a `DSD ` header with size 42,336,000 and that payload. Omit `DIIN`, `COMT`, all ID3. Rewrite `ckDataSize` at offset 4 to **42,336,132**. Expected total **42,336,144 bytes**.
2. Assert `ckDataSize` even and equal to filesize − 12; record SHA-256.
3. Convert at 88200/24, gain 0.00; record `sox stats` peak.
4. Convert at 44100/16, gain 0.00; record `sox stats` peak.
5. Compute `delta = peak88 − peak44` and the gain each yields (`−0.5 − peak`, capped +6).
6. Interpret: `delta > 0` → historical gain was conservative, that much level lost; `delta < 0` → optimistic, the 13 discs need clipping checks; `|delta| < 0.05` → immaterial.
7. Record both wall-clock timings and any Saracon warnings.

**Accept:** delta computed from measured peaks with both raw `sox stats` blocks quoted; explicit verdict on whether the 13 existing discs need re-conversion.

---

# Task index

| Phase | Tasks | Subtasks | Change from v1 |
|---|---:|---:|---|
| 0 — Ground truth | 5 | 25 | +P0.5 SDD reconciliation |
| 1 — Remediation | 7 | 38 | P1.2 expanded 8 → 11 |
| 2 — Unscheduled defects | 3 | 13 | — |
| 3 — Durable verification | 5 | 33 | +P3.2 decontamination |
| 4 — Build and contracts | 3 | 17 | +P4.3 runtime observation |
| 5 — Real media gates | 5 | 29 | — |
| 6 — Closure | 3 | 18 | — |
| **Total** | **31** | **173** | v1: 28 / 150 |

## Dependencies

```
P0.1 → P0.2 → P0.3 → P0.4 → P0.5
                              ↓
        ┌──────────────┬──────┴───────┬──────────────┐
       P1.1           P1.2          P1.3 … P1.7    P2.1 … P2.3
        └──────────────┴──────┬───────┴──────────────┘
                              ↓
              P3.1 → P3.2 → P3.3 / P3.4 / P3.5
                              ↓
                    P4.1 → P4.2 → P4.3
                              ↓
          P5.1 → P5.2 → P5.3 → P5.4       P5.5 (after P5.1)
                              ↓
                    P6.1 → P6.2 → P6.3
```

**Serialisation.** P1.1 precedes every Phase 5 task. P1.2 precedes P5.2, whose subtask 6 is its field test. P1.5 precedes P1.6. **P3.2 precedes P3.3** — decontamination must happen before the guard suite is written, or the inverted assertions will not be written at all. P4.3 precedes Phase 5, because the gates read logs whose rendering it repairs.

**Parallelisable.** P1.3–P1.7 touch different files. P2.1–P2.3 are independent of Phase 1. P3.4 and P3.5 are independent of P3.2 and P3.3.

**Wall clock.** Phase 5 is ~14 hours of Saracon runtime, uncompressible — Saracon is single-threaded with process-global registry and OLE state, and concurrent instances were the original death-loop cause. Everything else is roughly 24 engineering hours, up from 20 in v1.

## Completion definition

Complete when all 173 subtasks carry an observed `PASS`, or a `BLOCKED` with a quoted signature and named owner. `FAIL` on any subtask means its parent task is incomplete regardless of sibling passes. There is no deferred-minor state, and **no criterion may be satisfied by inference from a clean build, shared settings, or source reading alone.**