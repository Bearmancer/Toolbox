# Audio Services

SACD ISO extraction and DSD→FLAC conversion pipeline.

## STRUCTURE

```
Audio/
├── AudioSetup.cs              # DI: extension AddAudioServices(), PATH validation for saracon/sox/sacd_extract
├── PipelineOrchestrator.cs    # Pure orchestration: ISO enumeration, extraction, format routing, cleanup. 5 deps
├── ProcessRunner.cs           # Shared external process abstraction: ArgumentList, concurrent stdout/stderr, CancellationToken
├── PathValidator.cs           # Path traversal protection, input/output validation, containment checks
├── DiskSpaceChecker.cs        # Pre-flight disk space checks (4x extraction, 8x conversion)
├── SacdExtractService.cs      # wraps sacd_extract CLI: probe ISO, extract DFF+CUE
├── SaraconService.cs          # wraps saracon CLI: DSD→PCM conversion (d2p). Internal dep of DsdConvertService
├── SoxService.cs              # wraps sox CLI: track splitting, gain stats, duration, resampling. Internal dep
├── DsdConvertService.cs       # Conversion facade: DFF header probe, gain orchestration, saracon→split→tag pipeline, derivation. Absorbs SaraconService/SoxService/AudioMetadataService
├── AudioMetadataService.cs    # ATL.NET: read DSF/DFF tags, write FLAC tags
├── CueParser.cs               # CUE sheet parser (custom, BOM + UTF-8 heuristic + Windows-1252 fallback)
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
| Add pre-flight check      | `PathValidator.cs` or `DiskSpaceChecker.cs` | Validation before pipeline starts                           |

## CONVENTIONS

- **CUE parsing:** Custom parser, no external dependency. BOM detection + UTF-8 heuristic + Windows-1252 fallback.
- **ProcessRunner:** Shared abstraction for all external binary calls. ArgumentList only, concurrent stdout/stderr, CancellationToken ALWAYS.
- **PipelineOrchestrator:** Pure orchestration. ISO enumeration, extraction, format routing, cleanup. Calls ONLY DsdConvertService for conversion, never SaraconService/SoxService directly.
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
5. saracon d2p (gain, 88.2k/24bit/tpdf) → single WAV master (via DsdConvertService.ConvertAndSplitAsync)
6. sox trim per cue track → individual FLACs (inside ConvertAndSplitAsync)
7. ATL.NET → tag FLACs (inside ConvertAndSplitAsync)
8. Delete intermediate WAV (inside ConvertAndSplitAsync)
9. Optional: DsdConvertService.DeriveDirectoryAsync → 16-bit FLACs
