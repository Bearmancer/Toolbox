# SACD Pipeline Rescue — Consolidation, Resume Logic, Logging Overhaul

**Repo**: C:\Users\Lance\Dev\Toolbox (.NET 11, Spectre.Console.Cli, ErrorOr, Serilog Telemetry)
**Scope**: src/Services/Audio/ + one-time library consolidation (no app code)
**Provenance**: full log forensics (logs/audio.jsonl), filesystem audit, git reflog, codegraph verification. Metis critique incorporated (14 findings).

---

## 1. Background (why this plan exists)

Aug 12-13 SACD batch runs used uncommitted working-tree code that was lost at commit `c5ea0c9` (08-14 02:50): a FLAC-exists skip check and the Tree-B output layout. Current HEAD has Tree-A layout, no skip check, ordinal sort, ProcessRunner cancellation/timeout bugs, dead completion-grace branch, and silent partial-conversion success. Result: 13 complete discs scattered across two directory trees, one disc stuck mid-conversion, six never processed, and any re-run wasting ~15h re-converting finished discs.

### Confirmed code bugs (current HEAD line numbers)

| # | Location | Bug |
|---|---|---|
| B1 | ProcessRunner.cs:117 | `Task.Delay(t, ct)` — caller-token cancellation races timeoutTask, misreported as "Timed out after 3600s" (observed: 35s elapsed). No `ct.IsCancellationRequested` check |
| B2 | ProcessRunner.cs:134 | completion-grace branch is `else if` under timeout branch; SaraconService always passes timeout=1h + completionPattern="100%" + completionTimeout=10s → grace branch dead code → saracon hangs after 100% for up to 1h |
| B3 | PipelineOrchestrator.cs:151 | channelDir = Tree-A layout; 6 complete discs live in Tree-B |
| B4 | PipelineOrchestrator.cs:103 | InspectChannelDir checks only `*.dff` presence + collision suffixes; zero FLAC awareness |
| B5 | PipelineOrchestrator.cs:296 | CleanupAll deletes DFF/CUE/XML of ALL discs and ALL ISOs when !keepIso — including failed discs (data-loss risk) |
| B6 | DsdConvertService.cs:230 | partial split failure returns Success (only all-tracks-fail detected) |
| B7 | PipelineOrchestrator.cs:33 | ordinal ISO sort (Disc 10 before Disc 2) |

### Directory trees (current, verified)

```
C:\Users\Lance\Desktop\Music\
├── Karajan 1970-79 Berlin\                      ISO source (Disc N\Disc N.iso, N=1..20)
│   └── Disc 10-17 (Stereo)\Disc N\              TREE A — old code output
│       Disc 10: 7 FLACs (unpadded "1. ") + dff + clean.dff + cue + xml
│       Disc 11: 4 FLACs | Disc 12: 10 | Disc 13: 9 | Disc 14: 15 | Disc 15: 7 | Disc 16: 6
│       Disc 17: 0 FLACs + orphan 712MB _clean-d2p.wav
└── Karajan 1970-79 Berlin (Stereo)\             TREE B — later output (canonical target)
    Disc 1: 19 FLACs (padded "01. ") | Disc 2: 8 | Disc 17: 8 | Disc 18: 9 | Disc 19: 12 | Disc 20: 8
    Disc 3: DFF+CUE only (4 tracks expected)     ← case-b resume material
    Disc 10: duplicate DFF+CUE, 0 FLACs
```

Cue-vs-FLAC audit (verified): Tree A discs 10-16 COMPLETE; Tree B discs 1,2,17,18,19,20 COMPLETE; Disc 3 and Tree-B Disc 10 have 0 FLACs; Disc 4-9 never extracted.

### Target tree (after this plan)

```
Karajan 1970-79 Berlin\                 ISOs only
Karajan 1970-79 Berlin (Stereo)\
└── Disc N\Disc N\                      N=1..20
    ├── Disc N.cue                      kept permanently (completeness manifest)
    ├── Disc N.dff                      transient — deleted only after disc SUCCESS
    └── NN. Title.flac                  padded names, complete set
```

---

## 2. Phase 0 — Library consolidation (manual, PowerShell, NO app code)

Canonical layout: Tree B. sacd_extract nests `Disc N\` inside the channelDir it is given; the pipeline writes FLACs beside the DFF.

Steps (order matters; nothing deleted before gate G1 passes):

1. Create missing Tree B dirs: `Berlin (Stereo)\Disc N\Disc N\` for N=11..16.
2. For N=10..16: Move-Item each FLAC from `Berlin\Disc N (Stereo)\Disc N\*.flac` into `Berlin (Stereo)\Disc N\Disc N\`, renaming `^(\d+)\. ` → zero-padded two-digit (`1. ` → `01. `). Disc 10 target dir already exists with DFF+CUE — FLACs merge in, no collision (target has 0 FLACs).
3. Copy `*.cue` + `*.xml` for N=11..16 from Tree A into their Tree B dirs. Disc 10 keeps its existing Tree B cue/xml.
4. **Gate G1 (MANDATORY before any deletion)**:
   - For all 13 discs (1,2,10..20): parse cue, count `TRACK` entries, count FLACs matching `^\d{1,2}\. `; every track number 1..T must have exactly one FLAC. Any mismatch → STOP, keep Tree A, report.
   - Spot-check one FLAC per disc: `sox --i -D <file>` returns duration > 0 with exit 0.
5. After G1 passes:
   - Delete Tree A entirely (`Disc 10 (Stereo)` … `Disc 17 (Stereo)`) — frees ~40GB incl. orphan WAV and redundant DFFs.
   - In Tree B: for the 13 verified-complete discs delete `*.dff` and `*.xml` (re-extraction from ISO costs ~2 min/disc if ever needed); KEEP every `*.cue` and all FLACs.
   - Keep Tree B `Disc 3\Disc 3\` DFF+CUE+XML untouched (case-b resume).

Rollback: steps 2-3 are moves/copies; Tree A deletion is the only destructive step and is gated.

---

## 3. Phase 1 — ProcessRunner.cs (bugs B1, B2)

Rewrite the wait logic after `BeginOutputReadLine`/`BeginErrorReadLine`:

1. `exitTask = process.WaitForExitAsync(linkedToken)` (unchanged).
2. Build `timeoutTask = Task.Delay(t, linkedToken)` ONLY when `timeout is { } t` — use linkedToken, not raw ct.
3. Completion grace: a `TaskCompletionSource` (`completionTcs`) set from the output handler the first time `completionPattern` matches (alongside the existing `completionDetected` flag). When it fires and `completionTimeout is { } ct2`, start `graceTask = Task.Delay(ct2, linkedToken)`.
4. Loop `Task.WhenAny(exitTask, timeoutTask?, graceTask?)`:
   - exitTask completes → break, normal path.
   - graceTask completes and `!process.HasExited` → `process.Kill(entireProcessTree: true)`, log INFO `ProcessRunner.CompletionGraceKill binary={Binary} waited={Ms}ms`, then treat as normal exit with **ExitCode normalized to 0** in the returned ProcessResult (process finished its work; the real validity gate is the caller's output-size check).
   - timeoutTask completes → **first check `ct.IsCancellationRequested`**: true → kill tree and `throw OperationCanceledException` (the existing catch filter `when ex is not OperationCanceledException` already lets it propagate); false → kill tree, log WARN with ACTUAL elapsed, return `Errors.Audio.ProcessFailed(binaryPath, $"Timed out after {sw.Elapsed.TotalSeconds:F0}s (limit {t.TotalSeconds:F0}s)")`.
5. If `ct` cancels at any point while waiting (WhenAny returns a canceled task): kill tree, throw OperationCanceledException. Never report cancellation as timeout.
6. Keep inactivity-timeout path (else branch) behavior as-is.

SaraconService unchanged except: its existing output-size sanity check (<50% expected PCM bytes → fail) remains the authority on grace-killed conversions — no exit-code change needed there because ProcessRunner normalizes to 0.

---

## 4. Phase 2 — PipelineOrchestrator.cs (bugs B3, B4, B5, B7 + resume matrix + logging hooks)

### 4a. Layout (B3)
```
channelDir = Path.Combine($"{parentDir} ({suffix})", Path.GetFileName(isoDir))
```
(Tree B). sacd_extract creates the inner `Disc N\` as before.

### 4b. Natural numeric sort (B7)
Sort key: `Regex.Replace(fileName, @"\d+", m => m.Value.PadLeft(20, '0'))` with OrdinalIgnoreCase → 1,2,…,9,10,…,20.

### 4c. Forced per-disc assessment gate (B4) — runs BEFORE any extraction/conversion work

Inputs: channelDir contents (`*.dff`, `*.cue`, `*.flac` recursively), requested `AudioOutputFormat`.

1. Locate cue. If cue exists → parse (CueParser; INDEX 01 only — correct for splitting, INDEX 00 pregaps intentionally ignored).
2. Enumerate FLACs in the primary dir; map by leading `^\d{1,2}\.` number.
3. **Completeness test** (requires cue):
   - every cue track number has exactly one FLAC, AND
   - for every non-last track: `|flacDuration − cueDuration| ≤ 2.0s` via `sox --i -D` (SoxService.GetDurationAsync), AND
   - last track (cue Duration is null): FLAC duration ≥ 30s.
   - If format == Both: run the same test against the derived dir `{discName} [16-bit {kHz}]` (derived FLACs share names with primary).
4. Decision matrix (log the decided case at INFO, see Phase 4):

| State | Action |
|---|---|
| cue + FLACs complete (all dirs for requested format) | **SKIP disc** — no extraction, no conversion |
| cue + FLACs complete primary, derived missing/partial (format=Both) | run DeriveDirectoryAsync only |
| FLACs partial/absent + valid DFF present | **case B**: skip extraction → full gain→convert→split |
| FLACs partial/absent + DFF missing | **case A**: extract → full pipeline |
| FLACs partial/absent + DFF present but INVALID (ProbeDsdAsync fails) | delete `*.dff`+`*.cue`+`*.xml` from dffDir first (avoid Windows collision suffixes; never assume sacd_extract overwrites), then case A |
| FLACs exist but NO cue | WARN "cannot verify without cuesheet" → treat as partial (reprocess) |

5. DFF validity check = existing `convertService.ProbeDsdAsync` (header parse: FRM8/DSD magic, FS + CHNL chunks). A DFF is trusted only after this passes.
6. Before re-splitting a disc with pre-existing FLACs (partial cases): orchestrator deletes all `*.flac` in the primary dffDir (and derived dir if format=Both) and logs the count. Deletion lives in the orchestrator, NOT DsdConvertService (the standalone dsd-convert CLI command may target arbitrary user dirs). Re-split is deliberately non-atomic: interruption leaves partials; next run deletes and re-converts.

### 4d. Safe cleanup (B5)
- Track success per ISO. `CleanupAll` receives only succeeded discs' dirs and ISO paths.
- Extensions deleted: `*.dff`, `*.xml` — **`*.cue` is kept permanently** (completeness manifest; `*.dff` glob already covers `_clean.dff`).
- Failed discs keep DFF+CUE for resume; their ISOs are never deleted.

---

## 5. Phase 3 — DsdConvertService.cs (bug B6)

In `ConvertAndSplitAsync` after the split loop: if `outputFiles.Count < cue.Tracks.Count` → return `Errors.Audio.ConversionFailed(dffFile, $"Incomplete conversion: missing tracks {missing}")` where missing = cue track numbers without a produced FLAC. Keep master-WAV deletion before returning.

---

## 6. Phase 4 — Logging overhaul (all severities: Debug/Info/Warn/Error)

1. **Run start, INFO, absolute paths once**:
   `SACD run: ISO root={absolute}` and `SACD run: output root={absolute}` (output root = `{isoRoot} ({suffix})`).
2. **Thereafter: relative paths everywhere.** New static class in src/Services/Audio/ (one class per file; suggested `LogPaths.cs`): holds isoRoot/outputRoot (set by orchestrator at run start) and `Format(string path)`:
   - prefix == isoRoot → `«ISO»\rest`
   - prefix == outputRoot → `«OUT»\rest`
   - prefix == Path.GetTempPath() → `«TMP»\rest`
   - roots unset or no prefix match → absolute path unchanged (safe default)
   All path-bearing log arguments across PipelineOrchestrator, DsdConvertService, SaraconService, SoxService, SacdExtractService, DffMetadataStripper route through it — including ProcessRunner.Start's rendered args string (display-only rewrite; the real ArgumentList stays absolute).
3. **Skip messages at INFO with adequate detail**:
   `Skipping {Disc} — {n}/{n} FLACs complete ({total})` where total = sum of cue durations formatted `h:mm:ss`.
   Derived-only re-derivation: `{Disc} — primary complete, deriving 16-bit…`.
4. **Case decision at INFO per disc**: `{Disc}: case {A|B|C-skip|C-partial} — {evidence}` e.g. `Disc 3: case B — DFF valid (2822400Hz 2ch, 3178.3MB), 0/4 FLACs → converting`.
5. Existing Telemetry.* structured style preserved; no inline comments; editorconfig-as-error respected.

---

## 7. Resume matrix — expected behavior of the NEXT full run (all 20 discs)

Natural order 1..20, roots printed absolute once, then:

| Disc | State after Phase 0 | Logged case | Work done |
|---|---|---|---|
| 1,2 | complete (cue kept, dff deleted) | C-skip 19/19, 8/8 | none |
| 3 | DFF+CUE, 0 FLACs | B — DFF valid, 0/4 | gain→convert→split (4 FLACs) |
| 4-9 | nothing | A | extract→gain→convert→split |
| 10-16 | complete (moved FLACs + cues) | C-skip n/n | none |
| 17-20 | complete | C-skip n/n | none |

After the run: CleanupAll deletes DFF/XML of succeeded discs only, keeps all cues. Re-running immediately → 20/20 skips at INFO.

Ctrl+C at any point → OperationCanceledException propagates (no false "Timed out after 3600s"); partial state is resumable: completed discs skip via FLAC gate, in-flight disc resumes via case A/B.

---

## 8. Acceptance criteria (executable)

1. `dotnet build` → 0 errors, 0 style violations (editorconfig-as-error).
2. Phase 0 gate G1 audit output: 13 discs `COMPLETE`, 0 mismatches.
3. `dotnet run --project src\App -- audio sacd-convert 'C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\' --verbose`:
   - first two lines: absolute ISO root + output root
   - ≥13 INFO skip lines with n/n detail and durations, all paths relative
   - Disc 3 case-B line, then 4 FLACs created in `«OUT»\Disc 3\Disc 3\`
   - Disc 4-9 case-A lines, extraction + conversion proceed
4. Ctrl+C during any saracon conversion → console shows cancellation, log shows NO `ProcessRunner.Timeout` entry for it; exit within seconds.
5. Re-run after completion → 20/20 skips, zero sacd_extract/saracon process starts.
6. `git diff` per phase commit: only intended files; commits atomic per phase (repo rule).

## 9. Constraints

- Repo rules: one class per file, no inline comments, no test NuGet packages (verification = builds + real runs + standalone .cs if needed), ErrorOr pattern, no `#pragma` suppressions, PascalCase JSON.
- src/Services/Audio/AGENTS.md: PipelineOrchestrator calls ONLY DsdConvertService for conversion; binaries from PATH; sibling output pattern.
- No new NuGet dependencies.
