# Audio — SACD ISO → DSD → FLAC

External-bin pipeline: sacd_extract → DFF → saracon d2p → sox split → ATL.NET tag.

## STRUCTURE — 18 files

```
Audio/
├── AudioSetup.cs              # DI extension AddAudioServices(), eager PATH check saracon/sox/sacd_extract
├── PipelineOrchestrator.cs    # Pure orchestration: enumerate ISOs (natural sort), probe, route, cleanup. 6 deps
├── DsdConvertService.cs       # Facade: ProbeDsdAsync, PrepareDffAsync, CalculateGainAsync, ConvertAndSplitAsync, Derive. Owns Saracon/Sox/Metadata
├── SaraconService.cs          # Internal of DsdConvertService. saracon -c d2p wrapper. 1h timeout, 100% marker, Validates WAV/FLAC output
├── SoxService.cs              # Internal of DsdConvertService. trim split, stats (Pk lev dB), duration --i -D, derive rate -v
├── SacdExtractService.cs      # sacd_extract: -P probe, -2/-m -e -c -C extract (Edit Master + CUE)
├── ProcessRunner.cs           # Shared: ArgumentList, concurrent stdout/stderr drain, CancellationToken, timeout/inactivity/completionPattern
├── LogPaths.cs                # Path redaction: Setup/Reset IsoRoot+OutputRoot, Format → «ISO»/«OUT»/«TMP»
├── PathValidator.cs           # Traversal/containment validation
├── DiskSpaceChecker.cs        # Pre-flight: 4x extraction, 8x conversion + 500MB margin
├── DiscOutputInspector.cs     # Disc assessment: CUE/FLAC/DFF probe → DiscState
├── FlacCompletenessChecker.cs # Duration checks, FLAC-by-track map, DFF dir resolution
├── DiscState.cs               # Complete | NeedsPrimaryConversion | NeedsExtraction | InvalidArtifacts | Failed
├── ReprocessGuard.cs          # state/audio/sacd-guard.json — 3 consecutive non-Complete → Failed
├── DffMetadataStripper.cs     # ID3 chunk strip → _clean.dff, FRM8 size rewrite, odd-pad handling
├── AudioMetadataService.cs    # ATL.NET: new Track(path), set props, Save()
├── CueParser.cs               # Custom CUE: BOM + UTF-8 heuristic + Windows-1252 fallback, no external dep
└── AudioModels.cs             # SacdDisc/Track, DsdProbeResult, CueSheet/Track, DsdConversionSettings.ForDsdRate, ConversionResult, PipelineResult
```

Facade: `PipelineOrchestrator` → `DsdConvertService` only. Never `SaraconService`/`SoxService` directly.

## WHERE TO LOOK

| Task | File | Notes |
|---|---|---|
| Add conversion step | `DsdConvertService.cs` | Add method to facade, call from PipelineOrchestrator |
| Change DSD→PCM | `SaraconService.cs` | Internal dep. d2p: gain/sample-rate/bit-depth/tpdf |
| Change sox op | `SoxService.cs` | Internal dep. Split/stats/duration/derive |
| Change gain | `DsdConvertService.cs` | DFF header + saracon 0dB→sox stats → gain = -0.5 - peak, cap 6.0 |
| Add CUE field | `CueParser.cs` | Parse() method |
| Add metadata | `DsdConvertService.cs` | ATL tag inside ConvertAndSplitAsync |
| Change binary path | `AudioSetup.cs` | PATH only, no env vars |
| Pipeline logic | `PipelineOrchestrator.cs` | Enumeration, routing, cleanup |
| Resume/assessment | `DiscOutputInspector.cs` | CUE/FLAC/DFF probe, resume state |
| Pre-flight check | `PathValidator.cs` / `DiskSpaceChecker.cs` | Before pipeline start |

## CONVENTIONS

- ProcessRunner: ArgumentList only, concurrent stdout/stderr collectors, CancellationToken always, TerminationReason (Exited/Timeout/Inactivity/KilledAfterCompletionMarker/Canceled/StartFailed), completionPattern "100%" + 10s grace.
- PipelineOrchestrator pure orchestration: natural-sort ISO enumeration, sacd_extract probe, DiscOutputInspector routing, delegates ONLY to DsdConvertService.
- CUE: custom parser, no lib. BOM + UTF-8 heuristic + Windows-1252 fallback.
- DsdConversionSettings.ForDsdRate(): single sample-rate mapping. DSD64→44100/16,88200/24; DSD128→88200/16,176400/24. No inline switches.
- ATL.NET metadata: new Track(path), set props, Save().
- ErrorOr<T> on all fallible ops. Telemetry.ForService(ServiceName.Audio) scope.
- Output dirs: sibling `../Name (Stereo)/` not `Name/[Stereo]/`. Single disc per subdir assessment.
- DiskSpace: 4x ISO extraction, 8x conversion, +500MB margin via DriveInfo.AvailableFreeSpace.

## ENVIRONMENT

Binaries `saracon`, `sox`, `sacd_extract` from PATH only. Validated eagerly in AudioSetup.AddAudioServices() via ProcessRunner.IsOnPath() — throws InvalidOperationException if missing. No env vars.

## PIPELINE — 9 steps

1. sacd_extract -P -i <iso> → stereo/mch probe
2. sacd_extract -2/-m -e -c -C -i <iso> → DSDIFF Edit Master DFF + CUE (in channelDir sibling)
3. DFF FRM8/DSD header parse (PROP/SND/FS/CHNL) → sample rate + channels
4. Prepare: DffMetadataStripper ID3 check → _clean.dff if needed
5. Gain: saracon d2p 0dB → temp WAV → sox stats → gain = -0.5 - Pk lev dB, cap 6.0
6. saracon -c d2p -r <rate> -f wav -n <bit>bit -d tpdf -g <gain> -T -V all -t <outDir> <dff> → master WAV
7. sox trim per CueTrack → FLACs (inside ConvertAndSplitAsync), ATL.NET tag per track
8. Delete master WAV in finally; best-effort (never masks primary error)
9. Optional: DeriveDirectoryAsync → 16-bit FLAC via sox rate -v

## SARACON

Headless only, never GUI. SaraconService builds: `saracon -c d2p -r <rate> -f wav -n <bit>bit -d tpdf -g <gain> -T -V all -t "<outDir>" "<input.dff>"`. -c d2p required, final arg = input DFF, -t = output dir. Default Bit16 at app layer (omit --format, parser rejects --format 16). Validates RIFF/WAVE/fmt/data chunks, checks -d2p variant filename, warns if output <50% expected PCM bytes. 1h timeout.

## STATE / RECOVERY

DiscOutputInspector → Complete / NeedsPrimaryConversion / NeedsExtraction / InvalidArtifacts. ReprocessGuard in state/audio/sacd-guard.json, 3 consecutive non-Complete → Failed, Complete clears entry, Warn log on transition. Reset: `dotnet run --project src\App -- audio sacd-convert --reset-guard`. Don't edit JSON manually.

## ARTIFACT OWNERSHIP

| Artifact | Success | Failure |
|---|---|---|
| ISO | delete iff --keep-iso absent AND outputs validate (FLAC count==CUE tracks, non-zero) | retain |
| CUE | retain — never deleted | retain |
| DFF/_clean.dff | delete after full validation (even with --keep-iso) | retain/quarantine |
| FLAC | retain | delete only deliberate re-split, logged |
| Master WAV | finally best-effort delete | never masks error |
