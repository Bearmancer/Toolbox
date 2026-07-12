# Phase 4: CLI Commands + DI Wiring

## Tasks

### Task 12: Create AudioCommandModule.cs

**What to do:**
Create `src/CLI/Audio/AudioCommandModule.cs`:

```csharp
using CLI.Audio;
using Spectre.Console.Cli;

namespace CLI.Audio;

public static class AudioCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg) =>
        cfg.AddBranch(
            "audio",
            b =>
            {
                b.SetDescription("Audio conversion: SACD ISO extraction and DSD→FLAC");
                b.AddCommand<SacdConvertCommand>("sacd-convert");
                b.AddCommand<DsdConvertCommand>("dsd-convert");
            }
        );
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add business logic to module

**References:**
- `src/CLI/Sync/SyncCommandModule.cs:1-19` — existing branch pattern
- `src/CLI/Azure/AzureCommandModule.cs` — azure branch pattern

**Acceptance criteria:**
- `dotnet build` succeeds

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(cli): add Audio command module`

---

### Task 13: Create SacdConvertCommand.cs

**What to do:**
Create `src/CLI/Audio/SacdConvertCommand.cs` — the full SACD ISO→FLAC pipeline command.

This command orchestrates the entire pipeline:
1. Probe ISO for stereo/mch
2. Extract DFF + CUE (Edit Master mode)
3. Calculate gain from DFF
4. Parse CUE for track boundaries
5. Split + convert each track to FLAC with gain + TPDF dither at 88.2kHz
6. Tag FLACs with CUE metadata
7. Optional: downsample to lower tiers, convert to MP3
8. Delete interim DFF file

```csharp
using System.ComponentModel;
using Core;
using Services.Audio;
using Spectre.Console.Cli;

using ErrorOr;

namespace CLI.Audio;

internal sealed class SacdConvertCommand : AsyncCommand<SacdConvertCommand.Settings>
{
    private readonly SacdExtractService _extractService;
    private readonly DsdConvertService _convertService;
    private readonly AudioMetadataService _metadataService;
    private readonly CueParser _cueParser;

    public SacdConvertCommand(
        SacdExtractService extractService,
        DsdConvertService convertService,
        AudioMetadataService metadataService,
        CueParser cueParser
    )
    {
        _extractService = extractService;
        _convertService = convertService;
        _metadataService = metadataService;
        _cueParser = cueParser;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Input SACD ISO file or directory containing .iso files")]
        [CommandArgument(0, "<input>")]
        public required string Input { get; init; }

        [Description("Output directory for FLAC files")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Output format: all, 24-bit, cd, 16bit, mp3 (default: 24-bit)")]
        [CommandOption("-f|--format")]
        public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Bit24;

        [Description("Force multichannel extraction (auto-detected if omitted)")]
        [CommandOption("-m|--multichannel")]
        public bool? Multichannel { get; init; }

        [Description("Keep interim DFF files (deleted by default)")]
        [CommandOption("--keep-dff")]
        public bool KeepDff { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var _ = Telemetry.ForService(ServiceName.Audio);

        string inputPath = Path.GetFullPath(settings.Input);
        string outputDir = settings.Output ?? Path.GetDirectoryName(inputPath)!;

        string[] isoFiles = File.GetAttributes(inputPath).HasFlag(FileAttributes.Directory)
            ? Directory.GetFiles(inputPath, "*.iso", SearchOption.AllDirectories)
            : [inputPath];

        if (isoFiles.Length == 0)
        {
            await Console.Error.WriteLineAsync($"No ISO files found in {inputPath}");
            return 1;
        }

        Telemetry.Info("Found {Count} SACD ISO(s) to process", isoFiles.Length);

        foreach (string iso in isoFiles)
        {
            ErrorOr<int> result = await ProcessIsoAsync(iso, outputDir, settings);
            if (result.IsError)
            {
                foreach (Error e in result.Errors)
                    await Console.Error.WriteLineAsync(e.Description);
                return 1;
            }
        }

        await Console.WriteLineAsync("SACD processing completed");
        return 0;
    }

    private async Task<ErrorOr<int>> ProcessIsoAsync(string isoPath, string outputDir, Settings settings)
    {
        Telemetry.Info("Probing {Iso}", isoPath);

        ErrorOr<SacdProbeResult> probe = await _extractService.ProbeAsync(isoPath, default);
        if (probe.IsError)
            return probe.Errors;

        bool extractMch = settings.Multichannel ?? probe.Value.HasMultichannel;
        string suffix = extractMch ? "Multichannel" : "Stereo";
        string channelDir = Path.Combine(outputDir, $"[{suffix}]");

        Telemetry.Info("Extracting {Channel} from {Iso}", suffix, isoPath);

        ErrorOr<List<string>> extractResult = await _extractService.ExtractAsync(
            isoPath,
            channelDir,
            extractMch,
            default
        );
        if (extractResult.IsError)
            return extractResult.Errors;

        foreach (string dir in extractResult.Value)
        {
            ErrorOr<int> dirResult = await ProcessExtractedDirectoryAsync(dir, settings);
            if (dirResult.IsError)
                return dirResult.Errors;
        }

        return 0;
    }

    private async Task<ErrorOr<int>> ProcessExtractedDirectoryAsync(
        string dffDir,
        Settings settings
    )
    {
        string[] dffFiles = Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories);
        string[] cueFiles = Directory.GetFiles(dffDir, "*.cue", SearchOption.AllDirectories);

        if (dffFiles.Length == 0)
            return Errors.Audio.NoDffFound(dffDir);
        if (cueFiles.Length == 0)
            return Errors.Audio.NoCueFound(dffDir);

        string dffFile = dffFiles[0];
        string cueFile = cueFiles[0];

        Telemetry.Info("Calculating gain for {Dff}", Path.GetFileName(dffFile));
        ErrorOr<double> gainResult = await _convertService.CalculateGainAsync(dffFile, default);
        if (gainResult.IsError)
            return gainResult.Errors;

        double gain = gainResult.Value;
        Telemetry.Info("Gain: {Gain:F2} dB", gain);

        Telemetry.Info("Parsing CUE: {Cue}", Path.GetFileName(cueFile));
        ErrorOr<CueSheet> cueResult = _cueParser.Parse(cueFile);
        if (cueResult.IsError)
            return cueResult.Errors;

        CueSheet cue = cueResult.Value;
        var settings2 = new DsdConversionSettings();

        foreach (CueTrack track in cue.Tracks)
        {
            string trackNum = track.TrackNumber.ToString("D2");
            string safeTitle = SanitizeFilename(track.Title);
            string outputFlac = Path.Combine(dffDir, $"{trackNum}. {safeTitle}.flac");

            Telemetry.Info(
                "Converting track {Num}: {Title}",
                trackNum,
                track.Title
            );

            ErrorOr<ConversionResult> convertResult = await _convertService.ConvertTrackAsync(
                dffFile,
                outputFlac,
                track,
                gain,
                settings2,
                default
            );

            if (convertResult.IsError)
                return convertResult.Errors;

            ErrorOr<Success> tagResult = _metadataService.CopyMetadataFromCue(
                outputFlac,
                cue,
                track
            );

            if (tagResult.IsError)
                Telemetry.Warn("Tagging failed for {File}: {Error}", outputFlac, tagResult.Errors[0].Description);
        }

        if (!settings.KeepDff && File.Exists(dffFile))
            File.Delete(dffFile);

        if (settings.Format != AudioOutputFormat.Bit24)
            await DownsampleDirectoryAsync(dffDir, settings.Format);

        return 0;
    }

    private async Task DownsampleDirectoryAsync(string directory, AudioOutputFormat format)
    {
        string[] flacFiles = Directory.GetFiles(directory, "*.flac", SearchOption.AllDirectories);

        AudioTier[] tiers = format switch
        {
            AudioOutputFormat.All => [AudioTier.Rate176400Depth24, AudioTier.Rate88200Depth24, AudioTier.Rate44100Depth16],
            AudioOutputFormat.Bit24 => [AudioTier.Rate88200Depth24],
            AudioOutputFormat.Cd or AudioOutputFormat.Bit16 => [AudioTier.Rate44100Depth16],
            _ => []
        };

        foreach (AudioTier tier in tiers)
        {
            (int sr, int bd) = tier switch
            {
                AudioTier.Rate176400Depth24 => (176400, 24),
                AudioTier.Rate88200Depth24 => (88200, 24),
                AudioTier.Rate44100Depth16 => (44100, 16),
                _ => (88200, 24)
            };

            string tierDir = Path.Combine(
                Path.GetDirectoryName(directory)!,
                $"{Path.GetFileName(directory)} [{bd}-bit {sr / 1000.0:F1}]"
            );
            Directory.CreateDirectory(tierDir);

            foreach (string flac in flacFiles)
            {
                string rel = Path.GetRelativePath(directory, flac);
                string dest = Path.Combine(tierDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                ErrorOr<ConversionResult> result = await _convertService.DownsampleAsync(
                    flac,
                    dest,
                    tier,
                    default
                );

                if (result.IsError)
                    Telemetry.Warn("Downsample failed for {File}: {Error}", flac, result.Errors[0].Description);
            }
        }

        if (format is AudioOutputFormat.All or AudioOutputFormat.Mp3)
        {
            string mp3Dir = Path.Combine(Path.GetDirectoryName(directory)!, $"{Path.GetFileName(directory)} [MP3]");
            Directory.CreateDirectory(mp3Dir);

            foreach (string flac in flacFiles)
            {
                string rel = Path.GetRelativePath(directory, flac);
                string dest = Path.ChangeExtension(Path.Combine(mp3Dir, rel), ".mp3");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                ErrorOr<ConversionResult> result = await _convertService.ConvertToMp3Async(
                    flac,
                    dest,
                    default
                );

                if (result.IsError)
                    Telemetry.Warn("MP3 conversion failed for {File}: {Error}", flac, result.Errors[0].Description);
            }
        }
    }

    private static string SanitizeFilename(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Join("-", name.Split(invalid)).Trim();
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Put service logic in command (orchestration only — delegates to services)
- Use interfaces

**References:**
- Python `audio.py:135-187` — `process_sacd_directory` orchestration
- Python `audio.py:256-268` — `convert_dff_to_flac` (gain + CUE + convert + delete DFF)
- Python `audio.py:312-350` — `convert_audio` (downsampling tiers)
- `src/CLI/Sync/YouTube/SyncYoutubeCommand.cs` — existing command pattern
- `src/CLI/AGENTS.md` — thin command pattern: args → service → result.Match → exit code
- Spectre.Console.Cli Settings pattern: `[CommandArgument]`, `[CommandOption]`

**Acceptance criteria:**
- `dotnet build` succeeds
- Command accepts: input path, output dir, format, multichannel flag, keep-dff flag
- Full pipeline: probe → extract → gain → parse CUE → convert tracks → tag → downsample → MP3
- Interim DFF deleted unless --keep-dff

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(cli): add SacdConvertCommand with full pipeline orchestration`

---

### Task 14: Create DsdConvertCommand.cs

**What to do:**
Create `src/CLI/Audio/DsdConvertCommand.cs` — for standalone DSD file conversion (no ISO).

This handles direct DSF/DFF→FLAC conversion without sacd_extract. Useful when DSD files are already extracted.

```csharp
using System.ComponentModel;
using Core;
using Services.Audio;
using Spectre.Console.Cli;

using ErrorOr;

namespace CLI.Audio;

internal sealed class DsdConvertCommand : AsyncCommand<DsdConvertCommand.Settings>
{
    private readonly DsdConvertService _convertService;
    private readonly AudioMetadataService _metadataService;

    public DsdConvertCommand(
        DsdConvertService convertService,
        AudioMetadataService metadataService
    )
    {
        _convertService = convertService;
        _metadataService = metadataService;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Input DSF or DFF file")]
        [CommandArgument(0, "<input>")]
        public required string Input { get; init; }

        [Description("Output FLAC file path")]
        [CommandArgument(1, "[output]")]
        public string? Output { get; init; }

        [Description("Gain in dB (default: auto-detect from volumedetect)")]
        [CommandOption("-g|--gain")]
        public double? GainDb { get; init; }

        [Description("Sample rate (default: 88200)")]
        [CommandOption("-r|--sample-rate")]
        public int SampleRate { get; init; } = 88200;

        [Description("Copy metadata from source DSD file to output FLAC")]
        [CommandOption("--copy-tags")]
        public bool CopyTags { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var _ = Telemetry.ForService(ServiceName.Audio);

        string inputPath = Path.GetFullPath(settings.Input);
        string outputPath = settings.Output
            ?? Path.ChangeExtension(inputPath, ".flac");

        if (!File.Exists(inputPath))
        {
            await Console.Error.WriteLineAsync($"Input file not found: {inputPath}");
            return 1;
        }

        double gain = settings.GainDb ?? 0.0;

        if (settings.GainDb is null)
        {
            Telemetry.Info("Auto-detecting gain for {File}", inputPath);
            ErrorOr<double> gainResult = await _convertService.CalculateGainAsync(inputPath, default);
            if (gainResult.IsError)
            {
                await Console.Error.WriteLineAsync(gainResult.Errors[0].Description);
                return 1;
            }
            gain = gainResult.Value;
        }

        Telemetry.Info("Converting with gain {Gain:F2} dB", gain);

        var convSettings = new DsdConversionSettings(
            SampleRate: settings.SampleRate,
            GainDb: gain
        );

        ErrorOr<ConversionResult> result = await _convertService.ConvertFullDffAsync(
            inputPath,
            outputPath,
            gain,
            convSettings,
            default
        );

        if (result.IsError)
        {
            await Console.Error.WriteLineAsync(result.Errors[0].Description);
            return 1;
        }

        if (settings.CopyTags)
        {
            ErrorOr<TrackMetadata> metaResult = _metadataService.ReadDsdMetadata(inputPath);
            if (!metaResult.IsError)
            {
                ErrorOr<Success> tagResult = _metadataService.WriteFlacTags(
                    outputPath,
                    metaResult.Value
                );
                if (tagResult.IsError)
                    Telemetry.Warn("Tagging failed: {Error}", tagResult.Errors[0].Description);
            }
        }

        await Console.WriteLineAsync(
            $"Converted: {inputPath} → {outputPath} ({result.Value.FileSizeBytes / 1024 / 1024} MB)"
        );
        return 0;
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments

**References:**
- Python `audio.py:256-272` — `convert_dff_to_flac` (standalone conversion)
- `src/CLI/AGENTS.md` — thin command pattern

**Acceptance criteria:**
- `dotnet build` succeeds
- Command accepts: input DSD file, output path, gain, sample rate, copy-tags flag
- Auto-detects gain if not specified
- Copies metadata from DSD source when --copy-tags

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(cli): add DsdConvertCommand for standalone DSD conversion`

---

### Task 15: Wire Audio into CLI.csproj + App Program.cs

**What to do:**

**1. Add Audio project reference to CLI.csproj:**
Add `<ProjectReference Include="..\Services\Audio\Audio.csproj" />` to the `<ItemGroup>` in `src/CLI/CLI.csproj`.

**2. Add using + ConfigureCommands to Program.cs:**
Add `using CLI.Audio;` and `using Services.Audio;` to the usings block.
Add `AudioCommandModule.ConfigureCommands(cfg: cfg);` in the `toolbox.Configure` block.
Add `services.AddAudioServices();` in the try block.

**Modified Program.cs sections:**
```csharp
// usings (add):
using CLI.Audio;
using Services.Audio;

// try block (add after AddLastFmServices):
services.AddAudioServices();

// toolbox.Configure (add after DashboardCommandModule):
AudioCommandModule.ConfigureCommands(cfg: cfg);
```

**Must NOT:**
- Reorder existing registrations
- Add any other commands

**References:**
- `src/App/Program.cs:1-85` — existing wiring pattern
- `src/CLI/CLI.csproj:1-14` — existing project references

**Acceptance criteria:**
- `dotnet build` succeeds
- `dotnet run --project src/App -- audio sacd-convert --help` shows help
- `dotnet run --project src/App -- audio dsd-convert --help` shows help

**QA:**
```bash
dotnet build
dotnet run --project src/App -- audio sacd-convert --help
dotnet run --project src/App -- audio dsd-convert --help
```
Expected: Build clean, both help texts visible

**Commit:** `feat(app): wire Audio services and CLI commands`

---

## Verify Phase 4

```bash
dotnet build
dotnet run --project src/App -- audio sacd-convert --help
dotnet run --project src/App -- audio dsd-convert --help
```

All succeed. Full pipeline wired and accessible from CLI.

**Dependencies:** Phase 3
**Blocks:** Phase 5
