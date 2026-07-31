# SACD Pipeline Migration: ffmpeg → saracon + sox

**Date:** 2026-07-31
**Status:** Approved (rev 2 — post cross-review)
**Scope:** Replace all FFMpegCore/ffmpeg usage in the SACD conversion pipeline with saracon (DSD→PCM) and sox (gain detection, splitting, resampling), per the SACD.red.md guide.

## Motivation

The current pipeline uses ffmpeg for DSD→PCM conversion. The SACD.red.md guide explicitly recommends Weiss Saracon as "the only tool currently recommended" for DSD→PCM resampling, and sox for click trimming and audio manipulation. The current per-track ffmpeg approach also decodes the entire DFF N times for an N-track disc (each `ConvertTrackAsync` call seeks independently). The saracon-native workflow — convert once, split in PCM — is both guide-faithful and strictly more efficient.

## Phase 0: Spike (before any C# changes)

Three underspecified behaviors must be confirmed empirically:

1. **Output filename convention.** Saracon `-t` sets a target directory; no output-filename flag exists. Run once and confirm: does `Disc 1.dff` → `Disc 1.wav`?
2. **`-b split` batch mode.** If saracon can emit one file per track from Edit Master markers, the sox-split step may be unnecessary. Test against one real disc.
3. **`-g` sign handling.** Gain formula can produce negative values (hot masters). Confirm saracon accepts `-g -0.40` cleanly.

```powershell
saracon.exe -c d2p -r 88200 -f wav -n 24bit -d tpdf -g 2.70 -T -V all -t "C:\temp\saracon_test" "C:\path\Disc 1.dff"
```

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Gain detection | saracon 0dB → temp WAV → sox stats | SoX cannot read DFF. Guide says "you should always check." Saracon has no dry-run; decode once at unity, measure, discard. |
| Intermediate format | WAV (uncompressed) | Fast random access for sox splitting. Temporary, cleaned up after. Guide uses CAF (also uncompressed). |
| Conversion flow | Convert-once-then-split | Guide: "splitting using the cue in the PCM domain makes this trimming process unnecessary." sacd_extract already uses `-e` (Edit Master = single file). One DSD decode per disc instead of N. |
| Click trimming | Not needed | Splitting in PCM domain eliminates the per-track click issue per the guide. |
| Binary resolution | `SARACON_PATH`/`SOX_PATH` env vars with PATH fallback | Matches existing `SACD_EXTRACT_PATH`/`FFMPEG_PATH` convention. ProcessRunner.BinaryNotFound error text references `*_PATH` env vars. |
| DFF probing | Binary header parse | DSDIFF (FRM8) header contains sample rate and channel count. No external tool needed. Replaces FFProbe. |
| Service structure | Service-per-binary (SaraconService, SoxService) | Mirrors existing `SacdExtractService(ProcessRunner, string binaryPath)` pattern. DsdConvertService becomes analysis/orchestration layer. |
| Saracon flags | `-T` (tolerant) + `-V all` (verbose) | `-T` prevents interactive overwrite prompts in unattended runs. `-V all` captures diagnostic output for error messages. |

## Pipeline Flow

```
1. sacd_extract -P              → probe ISO for stereo/mch        (unchanged)
2. sacd_extract -2/-m -e -c -C  → DSDIFF Edit Master + CUE       (unchanged)
3. DFF header parse             → sample rate (2822400/5644800), channels
4. saracon d2p (0dB) → temp WAV → sox stats → parse peak dB → gain = -0.5 - peak, cap 6.0
5. saracon d2p (gain, 88.2k/24bit/tpdf) → single WAV in dffDir
6. sox trim per cue track       → individual FLACs
7. ATL.NET                      → tag FLACs                       (unchanged)
8. Delete intermediate WAV
9. Optional: sox rate -v        → derive 16-bit FLACs
```

### Standalone dsd-convert command (single DFF → single FLAC)

```
1. DFF header parse → sample rate, channels
2. Gain detection (same as pipeline step 4)
3. saracon d2p (gain) → temp dir FLAC → move to output path
4. Optional: sox derive 16-bit
5. Optional: ATL.NET copy tags
```

## File Changes

### SaraconService.cs — NEW (mirrors SacdExtractService pattern)

Constructor: `SaraconService(ProcessRunner processRunner, string binaryPath)`

| Method | Implementation |
|---|---|
| `ConvertDsdToPcmAsync(inputDff, outputDir, sampleRate, bitDepth, gainDb, ct)` | saracon `-c d2p -r {rate} -f wav -n {bits}bit -d tpdf -g {gain} -T -V all -t {outputDir} {inputDff}`. Returns output WAV path. Exit code check + stderr in error. |
| `ConvertDsdToFlacAsync(inputDff, outputDir, sampleRate, bitDepth, gainDb, ct)` | Same but `-f flac`. Returns output FLAC path. |

### SoxService.cs — NEW (same pattern)

Constructor: `SoxService(ProcessRunner processRunner, string binaryPath)`

| Method | Implementation |
|---|---|
| `SplitTrackAsync(sourcePcm, outputFlac, start, duration?, ct)` | sox `{source} {output} trim {startSeconds} [{durationSeconds}]`. `trim` with no duration runs to EOF (last track). |
| `GetStatsAsync(filePath, ct)` | sox `{file} -n stats` → parse `Pk lev dB` from stderr → return peak dB. |
| `GetDurationAsync(filePath, ct)` | sox `--i -D {file}` → parse stdout as double seconds → TimeSpan. |
| `DeriveFlacAsync(sourceFlac, outputFlac, targetSampleRate, ct)` | sox `{source} -b 16 {output} rate -v {targetRate}`. |

### DsdConvertService.cs — Rewritten as analysis/orchestration layer

Constructor: `DsdConvertService(SaraconService saracon, SoxService sox)`

Remove: `using FFMpegCore`, `using FFMpegCore.Enums`, `MaxVolumePattern`, `ValidDsdCodecs`, `GetFfmpegPath()`, `ProcessRunner` dependency.

| Method | Implementation |
|---|---|
| `ProbeDsdAsync(dffFilePath, ct)` | Binary parse DFF header: FRM8 magic → PROP chunk → FS chunk (4-byte BE uint32 sample rate) + CHNL chunk (2-byte BE uint16 channels). Return `DsdProbeResult(path, "dsd", sampleRate, channels)`. No external tool. |
| `CalculateGainAsync(dffFilePath, ct)` | saracon.ConvertDsdToPcmAsync(dff, tempDir, 88200, 24, 0.0) → sox.GetStatsAsync(tempWav) → gain = -0.5 - peakDb, cap 6.0. Delete temp WAV in finally. |
| `ConvertFullDffAsync(inputDff, outputFlac, settings, ct)` | saracon.ConvertDsdToFlacAsync(dff, tempDir, rate, bits, gain) → move to outputFlac. sox.GetDurationAsync for duration. |
| `DeriveFlacAsync(sourceFlac, outputFlac, targetSampleRate, ct)` | Delegates to sox.DeriveFlacAsync. sox.GetDurationAsync for duration. |

### PipelineOrchestrator.cs — Convert-once-then-split

- Constructor gains: `SaraconService saraconService, SoxService soxService` (DsdConvertService stays for probe/gain).
- `ProcessExtractedDirectoryAsync`: After gain + cue parse, call `saraconService.ConvertDsdToPcmAsync` once → WAV path. Pass WAV path to `ConvertTracksAsync`.
- `ConvertTracksAsync`: Signature changes from `(dffFile, outputDir, cue, convSettings, ct)` to `(pcmFile, outputDir, cue, ct)`. Calls `soxService.SplitTrackAsync` per track. Deletes intermediate WAV after splitting.
- `ConvertBothFormatsAsync`: Updated to match. Derive step calls `soxService.DeriveFlacAsync` per split track (follow-up: resample master once, then split).

### AudioSetup.cs — DI rewiring

- Remove: `FFMpegCore.GlobalFFOptions.Configure(...)` block, `ffmpegPath` variable, `FFMPEG_PATH` env var.
- Add: `var saraconPath = Environment.GetEnvironmentVariable("SARACON_PATH") ?? "saracon";`
- Add: `var soxPath = Environment.GetEnvironmentVariable("SOX_PATH") ?? "sox";`
- Wire: `SaraconService`, `SoxService`, updated `DsdConvertService(saracon, sox)`.

### ProcessRunner.cs

- `IsOnPath`: `private static` → `public static` (for potential DI-time validation).

### Audio.csproj

Remove: `<PackageReference Include="FFMpegCore" />`

### Directory.Packages.props

Remove: `<PackageVersion Include="FFMpegCore" Version="5.4.0" />`

### Errors.cs

`ProbeFailed`: `$"ffprobe failed for {file}: {reason}"` → `$"DSD probe failed for {file}: {reason}"`

### DsdConvertCommand.cs

Adapt to changed service methods. `ProbeDsdAsync`, `CalculateGainAsync`, `ConvertFullDffAsync`, `DeriveFlacAsync` signatures preserved on DsdConvertService. Minimal changes.

### AudioModels.cs

No changes. `DsdProbeResult.CodecName` will contain `"dsd"` instead of ffmpeg codec names.

### AGENTS.md updates (in-scope)

- `src/Services/Audio/AGENTS.md`: Remove "NEVER add SoX dependency" anti-pattern. Update STRUCTURE (add SaraconService, SoxService). Update PIPELINE section. Update ENVIRONMENT VARIABLES (SARACON_PATH, SOX_PATH; remove FFMPEG_PATH). Update CONVENTIONS (remove FFMpegCore references).
- Root `AGENTS.md`: Update if it references ffmpeg/FFMpegCore in audio context.

## CLI Tool Reference

### saracon (D2P conversion)
```
saracon -c d2p -r {rate} -f {format} -n {bits}bit -d tpdf -g {gainDb} -T -V all -t {outputDir} {input.dff}
```
- Rates: 44100, 88200, 176400, etc.
- Formats: wav, flac, aif, rf64, caf, raw
- Number: 16bit, 24bit, 32bit, float, double
- Dither: off, tpdf, powr1, powr2, powr3
- `-T`: tolerant mode (ignore overwrites/collisions)
- `-V all`: verbose output to stdout
- Output filename derived from input filename + new extension

### sox
```
sox {input} -n stats                          # peak/RMS levels → stderr
sox {input} {output} trim {start} [{dur}]     # extract segment (no dur = to EOF)
sox {input} -b 16 {output} rate -v {rate}     # resample to 16-bit
sox --i -D {file}                             # duration in seconds → stdout
```

### DSDIFF (DFF) header structure
```
Offset 0:  "FRM8" magic (4 bytes)
Offset 4:  File size (8 bytes, BE uint64)
Offset 12: "DSD " form type (4 bytes)
Chunks:    "PROP" → "SND " → contains "FS  " (sample rate, 4-byte BE uint32)
                             and "CHNL" (channel count, 2-byte BE uint16)
```

## Out of Scope

- SacdExtractService.cs (unchanged, already uses sacd_extract)
- CueParser.cs, AudioMetadataService.cs, PathValidator.cs, DiskSpaceChecker.cs (unchanged)
- Click trimming (unnecessary with convert-once-then-split per guide)
- ConvertBothFormatsAsync master-then-split optimization (follow-up: resample master once, then split)
- Tee/real-time output (future ProcessRunner enhancement)
