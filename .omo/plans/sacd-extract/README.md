# SACD Extract — .NET Pipeline Plan

## Architecture Decision

**Pragmatic Hybrid**: FFMpegCore + ATL.NET + sacd_extract CLI + custom CUE parser.

### Dependencies

| Dep | Type | License | Purpose |
|-----|------|---------|---------|
| `FFMpegCore` v5.4.0 | NuGet | MIT | Typed FFmpeg arg builder (DSD→PCM, gain, FLAC encode) |
| `z440.atl.core` v7.15.3 | NuGet | MIT | DSF/DFF metadata reading, FLAC tagging |
| `sacd_extract` binary | External CLI | GPL/LGPL | SACD ISO → DFF + CUE extraction (user-provided via .env) |
| `ffmpeg` binary | External CLI | LGPL | DSD decoding + PCM conversion + FLAC encoding (user-provided via .env) |

### What FFmpeg Does in One Step

```
ffmpeg -i input.dsf -af "volume=XdB,aresample=88200:dither_method=triangular_hp" -ar 88200 -c:a flac output.flac
```

DSD→PCM (Gesemann dsd2pcm) + gain + TPDF dither + FLAC encoding — single command. No separate FLAC encoder library needed.

### Pipeline (mirrors Python audio.py)

1. `sacd_extract -P` → probe ISO for stereo/mch presence
2. `sacd_extract -2/-m -e -c -C` → extract DSDIFF Edit Master + CUE (single-track avoids click trimming)
3. `ffmpeg volumedetect` → calculate max peak → gain = -0.5dB - max_peak
4. CUE parser → per-track start/duration/metadata
5. `ffmpeg` → split + DFF→FLAC: gain, 88.2kHz, TPDF dither, s32 sample_fmt
6. Optional: `ffmpeg` → downsample to 176.4/24, 88.2/24, 44.1/16 tiers
7. Optional: `ffmpeg` → MP3 320k
8. ATL.NET → read DSF/DFF metadata, write FLAC tags (title, artist, ISRC, catalog, album, year)

### Key Technical Facts

- DSD64 = 2.8224 MHz. 88.2kHz = 2,822,400 / 32 (integer ratio, no interpolation artifacts)
- SACDs mastered ~6dB quiet. Must volumedetect before applying gain. Target: -0.5dB headroom
- Click artifact = FIR filter init (0x69 silence pattern). Only on split tracks. Edit Master + CUE avoids this
- TPDF dither via FFmpeg: `aresample=dither_method=triangular_hp`
- FFmpeg quality vs Weiss Saracon: measurable but inaudible (Archimago 2015 testing)
- ATL.NET: DSF/DFF metadata R/W since v7.13. ISRC + Catalog Number supported. No audio decoding
- sacd_extract: C-based CLI, Scarlet Book format (NOT ISO 9660, magic `SACDMTOC`). No .NET port exists

### Python → .NET Mapping

| Python | .NET |
|--------|------|
| `sacd_extract` subprocess | SacdExtractService (Process wrapper) |
| `ffmpeg-python` (volumedetect, convert) | FFMpegCore |
| `deflacue` CUE parser | CueParser.cs (custom, ~100 lines) |
| `sox` downsampling | FFMpegCore (FFmpeg can downsample) |
| ffmpeg metadata flags | ATL.NET (proper tag R/W) |
| `tqdm` progress | Spectre.Console progress bars |

### Phases

| Phase | Focus | Files |
|-------|-------|-------|
| 0 | Foundation: packages, project, Core wiring | Directory.Packages.props, Audio.csproj, Toolbox.slnx, ServiceName.cs, Errors.cs |
| 1 | Models + CUE parser | AudioModels.cs, CueParser.cs |
| 2 | SACD extract service | AudioSetup.cs, SacdExtractService.cs |
| 3 | DSD convert + metadata | DsdConvertService.cs, AudioMetadataService.cs |
| 4 | CLI commands | AudioCommandModule.cs, SacdConvertCommand.cs, DsdConvertCommand.cs |
| 5 | Verification | End-to-end test, state, logging |
