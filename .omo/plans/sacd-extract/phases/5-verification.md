# Phase 5: Verification + State + .env Documentation

## Tasks

### Task 16: Create AGENTS.md for Audio service

**What to do:**
Create `src/Services/Audio/AGENTS.md`:

```markdown
# Audio Services

SACD ISO extraction and DSD→FLAC conversion pipeline.

## STRUCTURE

```
Audio/
├── AudioSetup.cs              # DI: extension AddAudioServices(), reads SACD_EXTRACT_PATH + FFMPEG_PATH
├── SacdExtractService.cs      # wraps sacd_extract CLI: probe ISO, extract DFF+CUE
├── DsdConvertService.cs       # FFMpegCore: gain calc, DSD→FLAC, downsampling, MP3
├── AudioMetadataService.cs    # ATL.NET: read DSF/DFF tags, write FLAC tags
├── CueParser.cs               # CUE sheet parser (custom, encoding detection)
└── AudioModels.cs             # SacdDisc, SacdTrack, CueSheet, CueTrack, ConversionSettings records
```

## WHERE TO LOOK

| Task                       | File                      | Notes                                              |
| -------------------------- | ------------------------- | -------------------------------------------------- |
| Add audio conversion step  | `DsdConvertService.cs`    | Add method, call from CLI command                  |
| Change DSD→PCM filter      | `DsdConvertService.cs`    | `-af` filter chain in ConvertTrackAsync/ConvertFullDffAsync |
| Add CUE field support      | `CueParser.cs`            | Add parsing in `Parse()` method                    |
| Add metadata field         | `AudioMetadataService.cs` | Add to `TrackMetadata` record + Read/Write methods |
| Change gain calculation    | `DsdConvertService.cs`    | `CalculateGainAsync` — TargetHeadroomDb constant   |
| Change binary paths        | `AudioSetup.cs`           | SACD_EXTRACT_PATH, FFMPEG_PATH env vars            |

## CONVENTIONS

- **External binaries via .env:** `SACD_EXTRACT_PATH` and `FFMPEG_PATH` env vars. No bundling.
- **FFMpegCore for FFmpeg:** Typed fluent API. Custom args via `WithCustomArgument()`.
- **ATL.NET for metadata:** `new Track(path)`, set properties, `track.Save()`.
- **ErrorOr pattern:** All fallible operations return `ErrorOr<T>`.
- **CUE parsing:** Custom parser, no external dependency. BOM detection for encoding.

## ENVIRONMENT VARIABLES

| Variable           | Required | Description                                      |
| ------------------ | -------- | ------------------------------------------------ |
| `SACD_EXTRACT_PATH` | For ISO extraction only | Path to sacd_extract binary. Download from https://github.com/Sound-Linux-More/sacd-extract |
| `FFMPEG_PATH`       | Optional (defaults to `ffmpeg` on PATH) | Path to ffmpeg binary. Must support DSD demuxer (FFmpeg 4.0+) |

## ANTI-PATTERNS

- **NEVER** bundle sacd_extract or ffmpeg binaries in the repo
- **NEVER** use Xabe.FFmpeg (CC BY-NC-SA 3.0, non-commercial license)
- **NEVER** use AudioWorks (AGPL v3, license poison)
- **NEVER** use TagLibSharp (ATL.NET is better maintained)
- **NEVER** add SoX dependency (FFmpeg handles downsampling)
- **NEVER** hardcode binary paths

## PIPELINE

1. `sacd_extract -P` → probe ISO for stereo/mch
2. `sacd_extract -2/-m -e -c -C` → DSDIFF Edit Master + CUE
3. `ffmpeg volumedetect` → gain = -0.5dB - max_peak
4. CueParser → track boundaries + metadata
5. `ffmpeg` → split + DFF→FLAC: gain + 88.2kHz + TPDF dither
6. ATL.NET → tag FLACs
7. Optional: downsample to 176.4/24, 88.2/24, 44.1/16
8. Optional: MP3 320k
```

**References:**
- `src/Services/Azure/AGENTS.md` — existing service AGENTS.md pattern
- `src/Services/LastFm/` — existing service structure

**Acceptance criteria:**
- File exists with correct structure documentation

**Commit:** `docs(audio): add AGENTS.md for Audio service`

---

### Task 17: Add .env documentation

**What to do:**
Add SACD_EXTRACT_PATH and FFMPEG_PATH documentation to the repo's .env.example or README (if either exists). If neither exists, document in the Audio AGENTS.md (Task 16 covers this).

Check for existing .env.example:
```bash
ls .env.example 2>/dev/null || echo "No .env.example found"
```

If .env.example exists, add:
```
# Audio pipeline (SACD extraction + DSD→FLAC conversion)
SACD_EXTRACT_PATH=/path/to/sacd_extract
FFMPEG_PATH=/path/to/ffmpeg
```

**Acceptance criteria:**
- Environment variable documentation available to user

**Commit:** `docs(env): document SACD_EXTRACT_PATH and FFMPEG_PATH`

---

### Task 18: Full pipeline verification

**What to do:**
Verify the complete pipeline end-to-end with a real SACD ISO.

**Prerequisites:**
1. `sacd_extract` binary installed and `SACD_EXTRACT_PATH` set in .env
2. `ffmpeg` installed and `FFMPEG_PATH` set (or on PATH)
3. A test SACD ISO file available

**Test 1: SACD ISO → FLAC (full pipeline)**
```bash
dotnet run --project src/App -- audio sacd-convert -i /path/to/test.iso -o ./output --format 24-bit
```

Verify:
- [ ] ISO probed correctly (stereo/mch detected)
- [ ] DFF + CUE extracted to output directory
- [ ] Gain calculated and logged (should be ≤ 6dB)
- [ ] CUE parsed with correct track count
- [ ] Each track converted to FLAC at 88.2kHz/24-bit
- [ ] FLAC files tagged with title, artist, album, track number
- [ ] Interim DFF file deleted (unless --keep-dff)
- [ ] No errors in `logs/audio.jsonl`

**Test 2: Standalone DSD → FLAC**
```bash
dotnet run --project src/App -- audio dsd-convert -i /path/to/test.dsf -o ./output.flac --copy-tags
```

Verify:
- [ ] Gain auto-detected
- [ ] FLAC created at 88.2kHz/24-bit
- [ ] Metadata copied from DSF source
- [ ] No errors in `logs/audio.jsonl`

**Test 3: Full format tier (all)**
```bash
dotnet run --project src/App -- audio sacd-convert -i /path/to/test.iso --format all
```

Verify:
- [ ] 24-bit/88.2kHz FLAC created (master tier)
- [ ] 24-bit/176.4kHz FLAC created (upsample tier — if source allows)
- [ ] 16-bit/44.1kHz FLAC created (CD tier)
- [ ] 320kbps MP3 created
- [ ] Each tier in separate subdirectory with suffix

**Test 4: Error handling**
```bash
# Missing sacd_extract
unset SACD_EXTRACT_PATH
dotnet run --project src/App -- audio sacd-convert -i /path/to/test.iso
# Expected: BinaryNotFound error with helpful message

# No ISO files in directory
dotnet run --project src/App -- audio sacd-convert -i /empty/directory
# Expected: NoIsoFound error
```

**Acceptance criteria:**
- All 4 tests pass
- No unhandled exceptions
- `logs/audio.jsonl` contains structured log entries with Service=Audio
- Exit codes correct (0 success, 1 error)

**Commit:** `test(audio): verify full SACD→FLAC pipeline`

---

## Verify Phase 5

```bash
dotnet build
dotnet run --project src/App -- audio sacd-convert --help
dotnet run --project src/App -- audio dsd-convert --help
```

All succeed. Pipeline verified end-to-end.

**Dependencies:** Phase 4
**Blocks:** None (complete)
