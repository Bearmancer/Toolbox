# Audio Services

SACD ISO extraction and DSD→FLAC conversion pipeline.

## STRUCTURE

```
Audio/
├── AudioSetup.cs              # DI: extension AddAudioServices(), PATH validation for saracon/sox/sacd_extract
├── PipelineOrchestrator.cs    # Pure orchestration: ISO enumeration, extraction, format routing, cleanup. 6 deps
├── DiscOutputInspector.cs     # Disc assessment: CUE/FLAC/DFF probing, resume state detection
├── FlacCompletenessChecker.cs # Duration validation, FLAC-by-track mapping, DFF dir resolution
├── ProcessRunner.cs           # Shared external process abstraction: ArgumentList, concurrent stdout/stderr, CancellationToken
├── PathValidator.cs           # Path traversal protection, input/output validation, containment checks
├── DiskSpaceChecker.cs        # Pre-flight disk space checks (4x extraction, 8x conversion)
├── SacdExtractService.cs      # wraps sacd_extract CLI: probe ISO, extract DFF+CUE
├── SaraconService.cs          # wraps saracon CLI: DSD→PCM conversion (d2p). Internal dep of DsdConvertService
├── SoxService.cs              # wraps sox CLI: track splitting, gain stats, duration, resampling. Internal dep
├── DsdConvertService.cs       # Conversion facade: DFF header probe, gain orchestration, saracon→split→tag pipeline, derivation. Absorbs SaraconService/SoxService/AudioMetadataService
├── AudioMetadataService.cs    # ATL.NET: read DSF/DFF tags, write FLAC tags
├── CueParser.cs               # CUE sheet parser (custom, BOM + UTF-8 heuristic + Windows-1252 fallback)
├── DiscState.cs                # Disc assessment and guard verdict enum
├── ReprocessGuard.cs           # Persistent consecutive-failure guard and reset support
└── AudioModels.cs             # SacdDisc, SacdTrack, CueSheet, CueTrack, DsdConversionSettings, ConversionResult, PipelineResult
```

## WHERE TO LOOK

| Task                      | File                                        | Notes                                                       |
| ------------------------- | ------------------------------------------- | ----------------------------------------------------------- |
| Add audio conversion step | `DsdConvertService.cs`                      | Add method to facade, call from PipelineOrchestrator      |
| Change DSD→PCM conversion | `SaraconService.cs`                         | Internal dep of DsdConvertService. saracon d2p: gain, sample rate, bit depth, dither |
| Change sox operations     | `SoxService.cs`                             | Internal dep of DsdConvertService. Split, stats, duration, derive |
| Change gain calculation   | `DsdConvertService.cs`                      | DFF header parse + saracon/sox stats → gain = -0.5 - peak, cap 6.0 |
| Add CUE field support     | `CueParser.cs`                              | Add parsing in `Parse()` method                             |
| Add metadata field        | `DsdConvertService.cs`                      | Metadata tagging handled inside ConvertAndSplitAsync       |
| Change binary paths       | `AudioSetup.cs`                             | PATH validation at DI registration. No env vars.           |
| Modify pipeline logic     | `PipelineOrchestrator.cs`                   | ISO enumeration, extraction, format routing, cleanup       |
| Disc resume/assessment   | `DiscOutputInspector.cs`                    | CUE/FLAC/DFF probing, duration checks, resume state       |
| Add pre-flight check      | `PathValidator.cs` or `DiskSpaceChecker.cs` | Validation before pipeline starts                           |

## CONVENTIONS

- **CUE parsing:** Custom parser, no external dependency. BOM detection + UTF-8 heuristic + Windows-1252 fallback.
- **ProcessRunner:** Shared abstraction for all external binary calls. ArgumentList only, concurrent stdout/stderr, CancellationToken ALWAYS.
- **PipelineOrchestrator:** Pure orchestration. ISO enumeration, extraction, format routing, cleanup. Calls ONLY DsdConvertService for conversion, never SaraconService/SoxService directly.
- **DiscOutputInspector:** Disc state assessment. CUE parsing, FLAC enumeration, DFF probing, duration validation. Returns DiscAssessment for orchestrator routing decisions.
- **PathValidator:** Path traversal protection. Input/output validation. Containment checks.
- **DiskSpaceChecker:** Pre-flight disk space checks. 4x ISO size for extraction, 8x for conversion, 500MB safety margin.
- **SaraconService/SoxService:** Internal dependencies of DsdConvertService. Thin binary wrappers via ProcessRunner. Not called by PipelineOrchestrator directly.
- **DsdConvertService:** Conversion facade. DFF header probe, gain orchestration, saracon→split→tag pipeline, derivation. PipelineOrchestrator calls ONLY this service.
- **ATL.NET for metadata:** `new Track(path)`, set properties, `track.Save()`.
- **ErrorOr pattern:** All fallible operations return `ErrorOr<T>`.
- **DsdConversionSettings.ForDsdRate():** Single source for sample-rate mapping. No inline switches.
- **Output directories:** Sibling pattern: `../Name (Stereo)/` not `Name/[Stereo]/`.

## ENVIRONMENT VARIABLES

All binaries (saracon, sox, sacd_extract) resolved from PATH. Validated eagerly at DI registration in `AudioSetup.AddAudioServices()`. No environment variables. No `SACD_EXTRACT_PATH`, `FFMPEG_PATH`, `SARACON_PATH`, or `SOX_PATH`.

## ANTI-PATTERNS

- **NEVER** bundle saracon, sox, or sacd_extract binaries in the repo
- **NEVER** hardcode binary paths
- **NEVER** use TagLibSharp (ATL.NET is better maintained)
- **NEVER** call SaraconService or SoxService from PipelineOrchestrator — use DsdConvertService facade
- **NEVER** duplicate sample-rate mapping logic — use DsdConversionSettings.ForDsdRate()

## PIPELINE

1. `sacd_extract -P` → probe ISO for stereo/mch
2. `sacd_extract -2/-m -e -c -C` → DSDIFF Edit Master + CUE
3. DFF binary header parse → sample rate, channels
4. saracon d2p (0dB) → temp WAV → sox stats → gain = -0.5 - peak, cap 6.0
5. saracon d2p (gain, resolved SACD 44.1k/16bit/tpdf) → single WAV master (via DsdConvertService.ConvertAndSplitAsync)
6. sox trim per cue track → individual FLACs (inside ConvertAndSplitAsync)
7. ATL.NET → tag FLACs (inside ConvertAndSplitAsync)
8. Delete intermediate WAV (inside ConvertAndSplitAsync)
9. Optional: DsdConvertService.DeriveDirectoryAsync → 16-bit FLACs

## STATE AND RECOVERY

`DiscOutputInspector` assesses each disc as `Complete`, `NeedsPrimaryConversion`, `NeedsExtraction`, or `InvalidArtifacts`. `ReprocessGuard` records consecutive non-`Complete` outcomes in `state/audio/sacd-guard.json`; the third consecutive non-complete outcome becomes `Failed`, and a genuine `Complete` outcome removes the entry. Guard transitions are logged at `Warn`.

Use `dotnet run --project src\App -- audio sacd-convert --reset-guard` to clear all guard entries. This is supported recovery path; do not edit guard JSON manually during normal operation.

## SARACON CLI INVOCATION

Use Saracon headlessly. Never open the Saracon GUI for pipeline work. `SaraconService` resolves `saracon` from `PATH` and invokes:

```text
saracon -c d2p -r <sample-rate> -f wav -n <bit-depth>bit -d tpdf -g <gain-db> -T -V all -t "<output-directory>" "<input.dff>"
```

The final argument is the input DFF. `-t` is the output directory. `-c d2p` is required; `d2p` is not a positional subcommand. For a DSD64 16-bit run, resolved settings produce:

```text
saracon -c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00 -T -V all -t "<output-directory>" "<input.dff>"
```

The wrapper uses `ProcessRunner`, `-V all`, a one-hour timeout, and the `100%` completion marker. Pipeline tests must pass `--keep-iso`; do not delete source ISOs or CUE files during verification. At the application layer, omit `--format` because the default is `Bit16`; the current Spectre enum parser rejects numeric `--format 16`.

## ARTIFACT OWNERSHIP

| Artifact | Success | Failure / cancellation |
|---|---|---|
| ISO | delete only if `--keep-iso` absent **and** all outputs validate | retain |
| CUE | retain | **retain — never deleted** |
| DFF / `_clean.dff` | delete after full output validation, including with `--keep-iso` | retain or quarantine |
| FLAC | retain | delete only for a deliberate re-split, logged |
| Master PCM | best-effort delete in `finally` | never masks the primary error |
| Temp files | run-owned unique path, publish on success | remove run-owned only |
