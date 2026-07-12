# Phase 1: Models + CUE Parser

## Tasks

### Task 6: Create AudioModels.cs

**What to do:**
Create `src/Services/Audio/AudioModels.cs` with record types for the SACD pipeline:

```csharp
namespace Services.Audio;

internal sealed record SacdDisc(
    string IsoPath,
    string AlbumTitle,
    string? AlbumArtist,
    string? Publisher,
    int? Year,
    string? CatalogNumber,
    string? Genre,
    bool HasStereo,
    bool HasMultichannel,
    List<SacdTrack> Tracks
);

internal sealed record SacdTrack(
    int TrackNumber,
    string Title,
    string? Artist,
    string? Isrc,
    TimeSpan StartOffset,
    TimeSpan? Duration
);

internal sealed record DsdConversionSettings(
    int SampleRate = 88200,
    int BitDepth = 24,
    double GainDb = 0.0,
    string SampleFormat = "s32",
    bool TrimClicks = false,
    double TrimStartSeconds = 0.0065,
    double TrimEndSeconds = 0.0065
);

internal enum AudioOutputFormat
{
    All,
    Bit24,
    Cd,
    Mp3,
    Bit16
}

internal enum AudioTier
{
    Rate176400Depth24,
    Rate88200Depth24,
    Rate44100Depth16
}

internal sealed record ConversionResult(
    string OutputPath,
    TimeSpan Duration,
    long FileSizeBytes
);

internal sealed record CueSheet(
    string SourceFile,
    string? AlbumTitle,
    string? AlbumArtist,
    string? Genre,
    string? Date,
    List<CueTrack> Tracks
);

internal sealed record CueTrack(
    int TrackNumber,
    string Title,
    string? Performer,
    string? Isrc,
    TimeSpan StartTime,
    TimeSpan? Duration
);
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use `Constants.cs` or `Helpers.cs` — inline constants only
- Add interfaces (anti-pattern per project rules)

**References:**
- Python `types.py`: `AudioTier`, `AudioFormat` TypedDicts
- Python `cuesheet.py`: `TrackInfo` dataclass
- `AGENTS.md` rule: "One class per file. No Constants.cs, no Helpers.cs."
- Note: records are in one file because they are related data contracts, not separate classes with logic

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add SACD pipeline model records`

---

### Task 7: Create CueParser.cs

**What to do:**
Create `src/Services/Audio/CueParser.cs` — a custom CUE sheet parser.

CUE format is simple text:
```
TITLE "Album Title"
PERFORMER "Artist"
FILE "filename.dff" WAVE
  TRACK 01 AUDIO
    TITLE "Track Title"
    PERFORMER "Track Artist"
    ISRC "XXXXXXXXXXX"
    INDEX 01 00:02:35
```

Time format: `MM:SS:FF` (75 frames/second for CDDA, but SACD CUEs use 44100 Hz frame rate — same as Python `cuesheet.py` line 63: `track.start / 44100`)

**Implementation:**

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

internal sealed class CueParser
{
    private static readonly Regex QuotedValue = new(@"^""(.+)""$", RegexOptions.Compiled);
    private static readonly Regex TimeFormat = new(
        @"^(\d+):(\d+):(\d+)$",
        RegexOptions.Compiled
    );

    public ErrorOr<CueSheet> Parse(string cueFilePath)
    {
        if (!File.Exists(cueFilePath))
            return Error.NotFound("Cue.FileNotFound", $"CUE file not found: {cueFilePath}");

        byte[] raw = File.ReadAllBytes(cueFilePath);
        string content = DetectEncoding(raw);

        string? albumTitle = null;
        string? albumArtist = null;
        string? genre = null;
        string? date = null;
        string? sourceFile = null;
        List<CueTrack> tracks = [];
        CueTrack? current = null;

        foreach (string line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
            {
                string val = Unquote(trimmed[6..]);
                if (current is null)
                    albumTitle = val;
                else
                    current = current with { Title = val };
            }
            else if (trimmed.StartsWith("PERFORMER ", StringComparison.OrdinalIgnoreCase))
            {
                string val = Unquote(trimmed[10..]);
                if (current is null)
                    albumArtist = val;
                else
                    current = current with { Performer = val };
            }
            else if (trimmed.StartsWith("GENRE ", StringComparison.OrdinalIgnoreCase))
            {
                genre = Unquote(trimmed[6..]);
            }
            else if (trimmed.StartsWith("DATE ", StringComparison.OrdinalIgnoreCase))
            {
                date = Unquote(trimmed[5..]);
            }
            else if (trimmed.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = trimmed[5..];
                int lastSpace = rest.LastIndexOf(' ');
                if (lastSpace > 0)
                    sourceFile = Unquote(rest[..lastSpace]);
            }
            else if (trimmed.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                    tracks.Add(current);

                string[] parts = trimmed[6..].Split(' ', 2);
                if (parts.Length < 2 || !int.TryParse(parts[0], out int num))
                    return Errors.Audio.InvalidCueFormat(cueFilePath, $"Bad TRACK line: {trimmed}");

                current = new CueTrack(num, "", null, null, TimeSpan.Zero, null);
            }
            else if (trimmed.StartsWith("ISRC ", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                    current = current with { Isrc = Unquote(trimmed[5..]) };
            }
            else if (trimmed.StartsWith("INDEX ", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null)
                    continue;

                string[] parts = trimmed[6..].Split(' ', 2);
                if (parts.Length < 2)
                    continue;

                ErrorOr<TimeSpan> time = ParseCueTime(parts[1]);
                if (time.IsError)
                    return time.Errors;

                current = current with { StartTime = time.Value };
            }
        }

        if (current is not null)
            tracks.Add(current);

        if (sourceFile is null)
            return Errors.Audio.InvalidCueFormat(cueFilePath, "No FILE directive found");
        if (tracks.Count == 0)
            return Errors.Audio.InvalidCueFormat(cueFilePath, "No TRACK entries found");

        for (int i = 0; i < tracks.Count - 1; i++)
        {
            CueTrack t = tracks[i];
            tracks[i] = t with { Duration = tracks[i + 1].StartTime - t.StartTime };
        }

        return new CueSheet(sourceFile, albumTitle, albumArtist, genre, date, tracks);
    }

    private static string DetectEncoding(byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);

        return Encoding.UTF8.GetString(raw);
    }

    private static string Unquote(string value)
    {
        Match m = QuotedValue.Match(value.Trim());
        return m.Success ? m.Groups[1].Value : value.Trim();
    }

    private static ErrorOr<TimeSpan> ParseCueTime(string time)
    {
        Match m = TimeFormat.Match(time.Trim());
        if (!m.Success)
            return Error.Validation("Cue.BadTime", $"Invalid time format: {time}");

        int minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int frames = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

        double totalSeconds = minutes * 60 + seconds + frames / 75.0;
        return TimeSpan.FromSeconds(totalSeconds);
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use external CUE parsing library (custom parser per architecture decision)
- Use `chardet` equivalent — BOM detection + UTF-8 fallback is sufficient for SACD CUE files

**References:**
- Python `cuesheet.py:1-201` — `parse_cue_file`, `extract_track_data`, `calculate_track_durations`
- Python `cuesheet.py:63` — `track.start / 44100` (SACD CUE uses 44100 Hz frame rate, but CUE spec uses 75 frames/sec)
- CUE spec: https://en.wikipedia.org/wiki/Cue_sheet_(computing)
- Note: CUE time format is MM:SS:FF where FF = frames (75 per second for CDDA). For SACD DSDIFF Edit Master CUEs, sacd_extract writes standard 75fps CUE timecodes.

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds
- Parser handles: TITLE, PERFORMER, FILE, TRACK, INDEX, ISRC, GENRE, DATE
- Encoding detection handles: UTF-8 BOM, UTF-16 LE, UTF-16 BE, plain UTF-8
- Track durations calculated from sequential start times deltas

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add CUE sheet parser with encoding detection`

---

## Verify Phase 1

```bash
dotnet build src/Services/Audio/Audio.csproj
```

Clean build. Models and CUE parser in place.

**Dependencies:** Phase 0
**Blocks:** Phase 2
