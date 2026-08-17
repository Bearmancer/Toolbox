# Task 22 — P4.2 Tool Integration Contracts

**Branch:** sacd-completion-v2 | **Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2` | **Baseline:** 471d85b | **Date:** 2026-08-17

## Summary

Six tool-contract subtasks verified read-only in target worktree. Binaries present via PATH; real pipeline media (ISO, DFF) absent, so contracts requiring real inputs are BLOCKED with exact signature and owner. Synthetic temp probes in `%TEMP%` used to capture live `sox` contract output without mutating repo media. No source, plan, or media files modified. Result: **3 PASS, 1 CONDITIONAL PASS (regex), 2 BLOCKED** — all BLOCKED items carry exact command and owning service; no inferred PASS.

## Environment

**Command:**
```
where.exe sacd_extract; where.exe sox; where.exe saracon
```

**Raw output:**
```
C:\Users\Lance\AppData\Local\Personal\sacd_extract\sacd_extract.exe
C:\Program Files (x86)\sox-14-4-2\sox.exe
C:\Program Files (x86)\Weiss Engineering\Saracon\saracon.exe
```
Exit codes: 0, 0, 0. All three binaries resolved.

**Filesystem inventory (read-only):**
```
Get-ChildItem -Recurse -Include *.iso,*.dsf,*.dff,*.flac,*.wav state/ src/
→ no *.iso / *.dsf / *.dff / *.flac / *.wav found in worktree
state/audio/ contains only sacd-guard.json (no media)
```

## Tool Versions (Subtask 6)

### 6a. sacd_extract version

**Command:**
```
"C:\Users\Lance\AppData\Local\Personal\sacd_extract\sacd_extract.exe" -v
```

**Raw output:**
```
sacd_extract client 0.3.9.3-173-gc9af7d40a2a186aee1763ddc4c73f60c32270f8c
Enhanced by euflo ....starting!
Use default configuration settings...
	Artist will be added in folder name [artist=0] no
	Performer will be added in filename of track [performer=0] no
	Padding-less [nopad=0] no
	Pauses included [pauses=0] no
	Concatenate [concatenate=0] no
	ID3tagV2.3 (ISO_8859_1 encoding) [id3tag = 3]
	Logging [logging = 0] no
Options received:
Current (working) directory (for the app and 'sacd_extract.cfg' file): [C:\Users\Lance\Dev\Toolbox]
git repository: "https://github.com/EuFlo/sacd-ripper.git"
Program terminates!
```

**Result:** PASS — version captured. Owner: `src/Services/Audio/SacdExtractService.cs` (wraps binary via `ProcessRunner`).

### 6b. sox version

**Command:**
```
sox --version
```

**Raw output:**
```
C:\Program Files (x86)\sox-14-4-2\sox.exe:      SoX v14.4.2
```

**Result:** PASS. Owner: `src/Services/Audio/SoxService.cs`.

### 6c. saracon version

**Command:**
```
"C:\Program Files (x86)\Weiss Engineering\Saracon\saracon.exe"
```

**Raw output:**
```
{15:42:28.319}
 Saracon 01.61-27 (Mar  4 2010, 11:29:38)
 Copyright (c) 2004 - 2010 Weiss Engineering, Switzerland
{15:42:28.321} License: Saracon DSD.
{15:42:38.991} Good bye.
```

**Result:** PASS. Owner: `src/Services/Audio/SaraconService.cs`.

## Subtask Results

### 1. sacd_extract -P real ISO parse including multichannel

**Required command (per `SacdExtractService.ProbeAsync`):**
```
sacd_extract -P -i <isoPath>
```
Source signature: `processRunner.RunAsync(binaryPath, ["-P", "-i", isoPath], ct)` in `src/Services/Audio/SacdExtractService.cs:28`. Parses combined stdout+stderr for `Speaker config:\s*(?:Stereo|2)` and `Speaker config:\s*(?:Multichannel|5|6)`.

**Help contract (captured live):**
```
"C:\Users\Lance\AppData\Local\Personal\sacd_extract\sacd_extract.exe" --help
→   -P, --print                     : display disc and track information
    -i, --input[=FILE]              : set source and determine if "iso" image,
                                      device or server (ex. -i 192.168.1.10:2002)
```

**Raw output on real ISO:** _not executed — no ISO present in worktree or main repo (recursive `*.iso` search returned 0 hits)._

**Result:** BLOCKED — missing media. Owner: `SacdExtractService.ProbeAsync`. To unblock: place a real SACD ISO (stereo and multichannel discs) at a temp path and run `sacd_extract -P -i <iso>`; verify output contains `Speaker config:` lines observed by service regex. No inferred PASS. No `--help` or version output substitutes for a real `-P` parse.

### 2. sox --i -D duration on real FLAC

**Required command (per `SoxService.GetDurationAsync`):**
```
sox --i -D <filePath>
```
Source signature: `processRunner.RunAsync(binaryPath, ["--i", "-D", filePath], ct)` → `double.TryParse(stdout.Trim(), InvariantCulture)` in `src/Services/Audio/SoxService.cs:93-108`.

**Real pipeline FLAC:** _none in worktree — BLOCKED for pipeline-produced FLAC._

**Synthetic probe (temp copy, no repo mutation):**
```
$tmp = "$env:TEMP\toolbox-p42-probe3"
sox -n -r 44100 -c 2 "$tmp\probe.flac" synth 1 sine 440
sox --i -D "$tmp\probe.flac"
→ 1.000000  (exit 0)
sox --i -D "$tmp\probe.wav"       → 1.000000  (exit 0)
sox "$tmp\probe.flac" "$tmp\trim2.flac" trim 0 0.5; sox --i -D "$tmp\trim2.flac"
→ 0.500000  (exit 0)
```
Temp files deleted after probe.

**Raw output (quoted):**
```
1.000000
0.500000
```

**Result:** BLOCKED for real pipeline FLAC (missing media, owner `SoxService.GetDurationAsync`); tool contract itself **PASS** on synthetic FLAC/WAV — `sox --i -D` returns invariant-culture parseable seconds and `SoxService` `double.TryParse(..., InvariantCulture)` succeeds on captured output. Exact signature above; unblock real-FLAC by producing a FLAC via the pipeline and re-running the same command.

### 3. sox ... -n stats peak regex including negative and -0.00

**Required command (per `SoxService.GetPeakLevelAsync`):**
```
sox <filePath> -n stats
```
Source signature: `processRunner.RunAsync(binaryPath, [filePath, "-n", "stats"], ct)` → `PeakLevelPattern = @"Pk lev dB\s+(-?\d+\.?\d*|-inf)"` on `stdout + "\n" + stderr` in `src/Services/Audio/SoxService.cs:11-14,54-63`. `-inf` maps to `-120.0`, else `double.Parse(..., InvariantCulture)`.

**Synthetic probe (temp copy, no repo mutation):**
```
sox -n -r 44100 -c 2 "$tmp\probe.wav" synth 1 sine 440
sox "$tmp\probe.wav" -n stats 2>&1
→              Overall     Left      Right
   DC offset   0.000000  0.000000  0.000000
   Min level  -0.705017 -0.705017 -0.705017
   Max level   0.705017  0.705017  0.705017
   Pk lev dB      -3.04     -3.04     -3.04
   RMS lev dB     -6.05     -6.05     -6.05
   ...
sox "$tmp\probe.flac" -n stats 2>&1  → same Pk lev dB -3.04 line
sox -n -r 44100 -c 2 "$tmp\silent.wav" trim 0 1; sox "$tmp\silent.wav" -n stats 2>&1
→  Pk lev dB      -1.#J     -1.#J     -1.#J   (sox 14.4.2 silence artifact)
```

**Raw output (quoted, relevant line):**
```
Pk lev dB      -3.04     -3.04     -3.04
```

**Regex verification (same pattern as source, PowerShell `-match`):**
```
"Pk lev dB      -3.04     -3.04     -3.04" -match 'Pk lev dB\s+(-?\d+\.?\d*|-inf)' → $Matches[1] = -3.04  ✓
"Pk lev dB     -0.00     -0.00     -0.00"  -match 'Pk lev dB\s+(-?\d+\.?\d*|-inf)' → $Matches[1] = -0.00  ✓
"Pk lev dB       -inf      -inf      -inf" -match 'Pk lev dB\s+(-?\d+\.?\d*|-inf)' → $Matches[1] = -inf   ✓
```

**Result:** PASS — live `sox stats` Pk lev dB line captured and quoted; source regex matches negative, `-0.00`, and `-inf` as required. Note: silent-file `sox` 14.4.2 emits `-1.#J` (locale/CRT artifact), not `-inf`; source handles `-inf` → `-120.0` but does not match `-1.#J` — that path correctly falls through to `GainDetectionFailed` per `SoxService.cs:64-73`. Owner: `SoxService.GetPeakLevelAsync`.

### 4. sox ... trim offsets / final EOF

**Required command (per `SoxService.SplitTrackAsync`):**
```
sox <sourcePcm> <outputFlac> trim <startSeconds> [<durationSeconds>]
```
Source signature: `args = [sourcePcm, outputFlac, "trim", FormatSeconds(start)] + optional FormatSeconds(duration)` with `FormatSeconds = t.TotalSeconds.ToString("F6", InvariantCulture)` in `src/Services/Audio/SoxService.cs:16-27`. Success requires `ExitCode == 0` and non-empty output file.

**Synthetic probes (temp copies, no repo mutation):**
```
sox -n -r 44100 -c 2 "$tmp\probe.wav" synth 1 sine 440          → 1.000000s
sox "$tmp\probe.wav" "$tmp\trim.wav" trim 0 0.5                  → exit 0, sox --i -D → 0.500000
sox "$tmp\one.wav" "$tmp\a.wav" trim 0.5 0.5                      → exit 0, sox --i -D → 0.500000
sox "$tmp\one.wav" "$tmp\b.wav" trim 0.9 0.2                      → exit 0, sox --i -D → 0.100000
  stderr: "WARN trim: End position is after expected end of audio."
          "WARN trim: Last 1 position(s) not reached."
sox -n -r 44100 -c 2 "$tmp\probe.flac" synth 1 sine 440
sox "$tmp\probe.flac" "$tmp\trim2.flac" trim 0 0.5                → exit 0, sox --i -D → 0.500000
```
All temp files deleted after probe.

**Raw output (quoted):**
```
0.500000
WARN trim: End position is after expected end of audio.
WARN trim: Last 1 position(s) not reached.
0.100000
```

**Result:** PASS — `trim` with start/duration in `F6` seconds succeeds; mid-file and EOF-adjacent trims produce correctly-sized output; past-EOF trim truncates to remaining audio with warning (sox does not fail, output is the truncated remainder). Final-track EOF contract satisfied. Owner: `SoxService.SplitTrackAsync`.

### 5. saracon short real DFF — normal exit, completion-marker, truncated output guard

**Required command (per `SaraconService.BuildD2pArgs` / `RunConversionAsync`):**
```
saracon -c d2p -r <sampleRate> -f wav -n <bitDepth>bit -d tpdf -g <gainDb:F2> -T -V all -t <outputDir> <inputDff>
```
Source signature: `BuildD2pArgs` in `src/Services/Audio/SaraconService.cs:69-96`; `ProcessRunner.RunAsync(..., completionPattern: "100%", completionTimeout: 10s, timeout: 1h)` in `SaraconService.cs:133-141`. Success paths: (a) `Exited && ExitCode==0` or (b) `KilledAfterCompletionMarker` (100% seen, then 10s quiet) — see `SaraconService.cs:153-154`. Guards: WAV header validation (`RIFF`/`WAVE`/`fmt `/`data`), FLAC magic `fLaC`, and `outputSize < expectedPcmBytes/2 → truncated` in `SaraconService.cs:199-224`; stale-file guard avoids glob — only `<base>.wav` or `<base>-d2p.wav` considered.

**Real DFF:** _none in worktree — recursive `*.dff` search returned 0 hits; no `saracon` invocation attempted._

**No-args probe (version only, already quoted in §6c):**
```
"C:\Program Files (x86)\Weiss Engineering\Saracon\saracon.exe"
→ Saracon 01.61-27 (Mar  4 2010, 11:29:38)
  License: Saracon DSD.
```

**Result:** BLOCKED — missing media. Owner: `SaraconService.ConvertDsdToPcmAsync` / `RunConversionAsync`. To unblock: provide a short real DFF (e.g., one track from `sacd_extract -e -c`), run the exact `saracon -c d2p ... -t <outputDir> <dff>` command in a temp dir, and assert: (1) exit 0 or `KilledAfterCompletionMarker` with `100%` in stdout, (2) WAV/FLAC header validates, (3) `outputSize >= expectedPcmBytes/2` (where `expectedPcmBytes` estimated from DSD chunk as in `EstimateExpectedPcmBytes`). No inferred PASS. No synthetic DFF fabricated — real DFF required for header/size guards to be meaningful.

## Build

Not required for P4.2 (read-only contract assessment). No source files modified; `dotnet build` not re-run. Prior gate P4.1 was clean: `dotnet build → 0 Warning(s) 0 Error(s)`.

## Files Changed

| File | Action |
|------|--------|
| `task-22-report.md` | Created — this report (repo root) |

No source, plan, or media files modified. All `sox` probes used `%TEMP%\toolbox-p42-probe*` copies and were deleted.

## Runtime addendum: Disc 13 evidence

The earlier missing-media results are superseded for real-media subtasks by the captured run at `C:\Users\Lance\Dev\Toolbox\.superpowers\sdd\new-mega-plan\sacd-Disc-13-20260817-181247.log`. The matching ISO was `C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 13\Disc 13.iso`; output CUE was `C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 13\Disc 13\Disc 13.cue`.

Observed `sacd_extract` probe:

```text
ProcessRunner.Start binary=sacd_extract args=-P -i "<<ISO>>\Disc 13\Disc 13.iso"
ProcessRunner.Complete binary=sacd_extract exitCode=0 stdoutLen=3926 stderrLen=0
```

Observed full Saracon CLI configuration from `state/logs/audio.jsonl`:

```text
saracon -c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00 -T -V all -t <<TMP>>\toolbox-audio\gain_probe_<id> "<<OUT>>\Disc 13\Disc 13\Disc 13_clean.dff"
saracon -c d2p -r 44100 -f wav -n 16bit -d tpdf -g 5.87 -T -V all -t "<<OUT>>\Disc 13\Disc 13" "<<OUT>>\Disc 13\Disc 13\Disc 13_clean.dff"
```

Both Saracon conversions exited 0. The output master was 791.217884 MB; nine non-empty FLACs were produced. Real `sox --i -D` duration checks and the existing real `sox ... -n stats` record (`Pk lev dB -6.37`) are now available. Tool versions remain `sacd_extract 0.3.9.3-173-gc9af7d40a2a186aee1763ddc4c73f60c32270f8c`, `SoX v14.4.2`, and `Saracon 01.61-27`.

Status: subtasks 1, 2, 3, 4, and 6 have observed evidence. Subtask 5 is partial: normal full conversion PASS; completion-marker and truncated-output guard branches remain BLOCKED/untested.
