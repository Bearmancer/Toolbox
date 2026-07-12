# Phase 3: DSD Convert Service + Metadata Service

## Tasks

### Task 10: Create DsdConvertService.cs

**What to do:**
Create `src/Services/Audio/DsdConvertService.cs` — FFMpegCore-based DSD→FLAC conversion.

This service mirrors Python functions:
- `calculate_gain()` — FFmpeg volumedetect → parse max_volume → gain = -0.5 - max_peak
- `process_tracks()` — per-track FFmpeg: split DFF + gain + 88.2kHz + FLAC
- `convert_audio()` / `flac_directory_conversion()` — downsampling tiers
- `convert_to_mp3()` — MP3 320k conversion

**FFmpeg filter chain for DSD→PCM + FLAC:**
```
-af "volume=XdB,aresample=88200:dither_method=triangular_hp"
-ar 88200
-c:a flac
-sample_fmt s32
```

**Implementation:**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Core;
using FFMpegCore;
using FFMpegCore.Enums;

namespace Services.Audio;

using ErrorOr;

internal sealed class DsdConvertService
{
    private static readonly Regex MaxVolumePattern = new(
        @"max_volume:\s*(-?\d+\.?\d*)\s*dB",
        RegexOptions.Compiled
    );

    private const double TargetHeadroomDb = -0.5;

    public async Task<ErrorOr<double>> CalculateGainAsync(
        string dffFilePath,
        CancellationToken ct = default
    )
    {
        try
        {
            IMediaAnalysis analysis = await FFProbe.AnalyseAsync(dffFilePath, cancellationToken: ct);
            string? stderr = await RunVolumeDetectAsync(dffFilePath, ct);

            if (stderr is null)
                return Errors.Audio.GainDetectionFailed(dffFilePath);

            Match m = MaxVolumePattern.Match(stderr);
            if (!m.Success)
                return Errors.Audio.GainDetectionFailed(dffFilePath);

            double maxVolume = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double gain = TargetHeadroomDb - maxVolume;

            return Math.Min(gain, 6.0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.GainDetectionFailed(dffFilePath);
        }
    }

    public async Task<ErrorOr<ConversionResult>> ConvertTrackAsync(
        string inputDff,
        string outputFlac,
        CueTrack track,
        double gainDb,
        DsdConversionSettings settings,
        CancellationToken ct = default
    )
    {
        try
        {
            string gainFilter = $"volume={gainDb.ToString("F2", CultureInfo.InvariantCulture)}dB";
            string resampleFilter = $"aresample={settings.SampleRate}:dither_method=triangular_hp";
            string filterChain = $"{gainFilter},{resampleFilter}";

            await FFMpegArguments
                .FromFileInput(inputDff, true, options => options
                    .Seek(track.StartTime)
                    .WithDuration(track.Duration ?? TimeSpan.Zero))
                .OutputToFile(outputFlac, true, options => options
                    .WithAudioCodec("flac")
                    .WithAudioSamplingRate(settings.SampleRate)
                    .WithCustomArgument($"-af \"{filterChain}\"")
                    .WithCustomArgument("-sample_fmt s32")
                    .OverwriteExisting())
                .ProcessAsynchronously(ct);

            var info = await FFProbe.AnalyseAsync(outputFlac, cancellationToken: ct);
            var fileInfo = new FileInfo(outputFlac);

            return new ConversionResult(outputFlac, info.Duration, fileInfo.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.ConversionFailed(outputFlac, ex.Message);
        }
    }

    public async Task<ErrorOr<ConversionResult>> ConvertFullDffAsync(
        string inputDff,
        string outputFlac,
        double gainDb,
        DsdConversionSettings settings,
        CancellationToken ct = default
    )
    {
        try
        {
            string gainFilter = $"volume={gainDb.ToString("F2", CultureInfo.InvariantCulture)}dB";
            string resampleFilter = $"aresample={settings.SampleRate}:dither_method=triangular_hp";
            string filterChain = $"{gainFilter},{resampleFilter}";

            await FFMpegArguments
                .FromFileInput(inputDff)
                .OutputToFile(outputFlac, true, options => options
                    .WithAudioCodec("flac")
                    .WithAudioSamplingRate(settings.SampleRate)
                    .WithCustomArgument($"-af \"{filterChain}\"")
                    .WithCustomArgument("-sample_fmt s32")
                    .OverwriteExisting())
                .ProcessAsynchronously(ct);

            var info = await FFProbe.AnalyseAsync(outputFlac, cancellationToken: ct);
            var fileInfo = new FileInfo(outputFlac);

            return new ConversionResult(outputFlac, info.Duration, fileInfo.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.ConversionFailed(outputFlac, ex.Message);
        }
    }

    public async Task<ErrorOr<ConversionResult>> DownsampleAsync(
        string sourceFlac,
        string outputFlac,
        AudioTier tier,
        CancellationToken ct = default
    )
    {
        (int sampleRate, int bitDepth) = tier switch
        {
            AudioTier.Rate176400Depth24 => (176400, 24),
            AudioTier.Rate88200Depth24 => (88200, 24),
            AudioTier.Rate44100Depth16 => (44100, 16),
            _ => (88200, 24)
        };

        try
        {
            string ditherArg = bitDepth == 16
                ? ",aresample=dither_method=triangular_hp"
                : "";

            await FFMpegArguments
                .FromFileInput(sourceFlac)
                .OutputToFile(outputFlac, true, options => options
                    .WithAudioCodec("flac")
                    .WithAudioSamplingRate(sampleRate)
                    .WithCustomArgument($"-sample_fmt {(bitDepth == 16 ? "s16" : "s32")}{ditherArg}")
                    .OverwriteExisting())
                .ProcessAsynchronously(ct);

            var info = await FFProbe.AnalyseAsync(outputFlac, cancellationToken: ct);
            var fileInfo = new FileInfo(outputFlac);

            return new ConversionResult(outputFlac, info.Duration, fileInfo.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.ConversionFailed(outputFlac, ex.Message);
        }
    }

    public async Task<ErrorOr<ConversionResult>> ConvertToMp3Async(
        string sourceFlac,
        string outputMp3,
        CancellationToken ct = default
    )
    {
        try
        {
            await FFMpegArguments
                .FromFileInput(sourceFlac)
                .OutputToFile(outputMp3, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithAudioBitrate(AudioBitRate.Kbps_320)
                    .OverwriteExisting())
                .ProcessAsynchronously(ct);

            var fileInfo = new FileInfo(outputMp3);
            var info = await FFProbe.AnalyseAsync(outputMp3, cancellationToken: ct);

            return new ConversionResult(outputMp3, info.Duration, fileInfo.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.ConversionFailed(outputMp3, ex.Message);
        }
    }

    private static async Task<string?> RunVolumeDetectAsync(
        string dffFilePath,
        CancellationToken ct
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetFfmpegPath(),
            Arguments = $"-i \"{dffFilePath}\" -af volumedetect -f null -",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            string stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return stderr;
        }
        catch
        {
            return null;
        }
    }

    private static string GetFfmpegPath()
    {
        string? envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;
        return "ffmpeg";
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use Xabe.FFmpeg (non-commercial license)
- Use NAudio or AudioWorks (unnecessary, AGPL)
- Use SoX (FFmpeg handles downsampling)
- Hardcode ffmpeg path

**References:**
- Python `audio.py:274-309` — `calculate_gain` (volumedetect + regex + headroom)
- Python `cuesheet.py:102-181` — `process_tracks` (FFmpeg split + convert with gain)
- Python `audio.py:383-415` — `downsample_flac` (SoX → FFmpeg equivalent)
- Python `audio.py:417-448` — `convert_to_mp3`
- FFMpegCore API: `FFMpegArguments.FromFileInput().OutputToFile().ProcessAsynchronously()`
- FFmpeg DSD filter: `aresample=88200:dither_method=triangular_hp`
- Research: FFmpeg uses Gesemann dsd2pcm internally (same algorithm as Weiss Saracon)
- Research: 88.2kHz = 2,822,400/32 (integer ratio, no interpolation artifacts)
- Research: TPDF dither via `dither_method=triangular_hp` (high-pass TPDF)

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds
- CalculateGainAsync: runs volumedetect, parses max_volume, returns gain ≤ 6dB
- ConvertTrackAsync: seeks to track start, applies gain + resample + dither, outputs FLAC
- ConvertFullDffAsync: converts entire DFF without seeking
- DownsampleAsync: downsamples to 176.4/24, 88.2/24, or 44.1/16
- ConvertToMp3Async: 320kbps MP3

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add DsdConvertService with FFMpegCore pipeline`

---

### Task 11: Create AudioMetadataService.cs

**What to do:**
Create `src/Services/Audio/AudioMetadataService.cs` — ATL.NET wrapper for reading DSF/DFF metadata and writing FLAC tags.

```csharp
using ATL;

namespace Services.Audio;

using ErrorOr;

internal sealed class AudioMetadataService
{
    public ErrorOr<TrackMetadata> ReadDsdMetadata(string filePath)
    {
        try
        {
            var track = new Track(filePath);
            return new TrackMetadata(
                track.Title,
                track.Artist,
                track.Album,
                track.AlbumArtist,
                track.Year,
                track.Genre,
                track.ISRC,
                track.CatalogNumber,
                track.TrackNumber,
                track.TrackTotal,
                track.DiscNumber,
                track.DiscTotal,
                track.Composer,
                track.Conductor
            );
        }
        catch (Exception ex)
        {
            return Error.Failure("Audio.MetadataReadFailed", ex.Message);
        }
    }

    public ErrorOr<Success> WriteFlacTags(string flacPath, TrackMetadata metadata)
    {
        try
        {
            var track = new Track(flacPath);

            if (!string.IsNullOrEmpty(metadata.Title))
                track.Title = metadata.Title;
            if (!string.IsNullOrEmpty(metadata.Artist))
                track.Artist = metadata.Artist;
            if (!string.IsNullOrEmpty(metadata.Album))
                track.Album = metadata.Album;
            if (!string.IsNullOrEmpty(metadata.AlbumArtist))
                track.AlbumArtist = metadata.AlbumArtist;
            if (metadata.Year > 0)
                track.Year = metadata.Year;
            if (!string.IsNullOrEmpty(metadata.Genre))
                track.Genre = metadata.Genre;
            if (!string.IsNullOrEmpty(metadata.Isrc))
                track.ISRC = metadata.Isrc;
            if (!string.IsNullOrEmpty(metadata.CatalogNumber))
                track.CatalogNumber = metadata.CatalogNumber;
            if (metadata.TrackNumber > 0)
                track.TrackNumber = metadata.TrackNumber;
            if (metadata.TrackTotal > 0)
                track.TrackTotal = metadata.TrackTotal;
            if (metadata.DiscNumber > 0)
                track.DiscNumber = metadata.DiscNumber;
            if (metadata.DiscTotal > 0)
                track.DiscTotal = metadata.DiscTotal;
            if (!string.IsNullOrEmpty(metadata.Composer))
                track.Composer = metadata.Composer;
            if (!string.IsNullOrEmpty(metadata.Conductor))
                track.Conductor = metadata.Conductor;

            track.Save();

            return Result.Success;
        }
        catch (Exception ex)
        {
            return Error.Failure("Audio.TagWriteFailed", ex.Message);
        }
    }

    public ErrorOr<Success> CopyMetadataFromCue(
        string flacPath,
        CueSheet cue,
        CueTrack track
    )
    {
        try
        {
            var t = new Track(flacPath);

            t.Title = track.Title;
            t.TrackNumber = track.TrackNumber;
            t.TrackTotal = cue.Tracks.Count;

            if (!string.IsNullOrEmpty(track.Performer))
                t.Artist = track.Performer;
            if (!string.IsNullOrEmpty(cue.AlbumTitle))
                t.Album = cue.AlbumTitle;
            if (!string.IsNullOrEmpty(cue.AlbumArtist))
                t.AlbumArtist = cue.AlbumArtist;
            if (!string.IsNullOrEmpty(cue.Genre))
                t.Genre = cue.Genre;
            if (!string.IsNullOrEmpty(cue.Date) && int.TryParse(cue.Date, out int year))
                t.Year = year;
            if (!string.IsNullOrEmpty(track.Isrc))
                t.ISRC = track.Isrc;

            t.Save();

            return Result.Success;
        }
        catch (Exception ex)
        {
            return Error.Failure("Audio.CueTagFailed", ex.Message);
        }
    }
}

internal sealed record TrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    int Year,
    string? Genre,
    string? Isrc,
    string? CatalogNumber,
    int TrackNumber,
    int TrackTotal,
    int DiscNumber,
    int DiscTotal,
    string? Composer,
    string? Conductor
);
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use TagLibSharp (ATL.NET is better maintained, more formats)
- Use NAudio (metadata-only requirement, ATL handles this)
- Add interfaces

**References:**
- ATL.NET API: https://github.com/Zeugma440/atldotnet
- ATL.NET Track class: `new Track(path)`, `track.Title`, `track.ISRC`, `track.CatalogNumber`, `track.Save()`
- Python `cuesheet.py:127-136` — metadata mapping (title, track, album, artist, genre, date)
- Research: ATL.NET v7.13+ supports DFF R/W. ISRC + CatalogNumber supported.

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds
- ReadDsdMetadata: reads title, artist, album, ISRC, catalog from DSF/DFF
- WriteFlacTags: writes all metadata fields to FLAC
- CopyMetadataFromCue: writes CUE-parsed metadata to FLAC

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add AudioMetadataService with ATL.NET tagging`

---

## Verify Phase 3

```bash
dotnet build src/Services/Audio/Audio.csproj
```

Clean build. DSD conversion + metadata tagging in place.

**Dependencies:** Phase 2
**Blocks:** Phase 4
