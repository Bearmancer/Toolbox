# Phase 2: SACD Extract Service + DI Setup

## Tasks

### Task 8: Create AudioSetup.cs

**What to do:**
Create `src/Services/Audio/AudioSetup.cs` — DI registration, reads binary paths from env:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Services.Audio;

public static class AudioSetup
{
    extension(IServiceCollection services)
    {
        public void AddAudioServices()
        {
            var sacdExtractPath =
                Environment.GetEnvironmentVariable("SACD_EXTRACT_PATH")
                ?? throw new InvalidOperationException(
                    "SACD_EXTRACT_PATH not set in .env. "
                    + "Download sacd_extract from https://github.com/Sound-Linux-More/sacd-extract"
                );

            var ffmpegPath =
                Environment.GetEnvironmentVariable("FFMPEG_PATH")
                ?? "ffmpeg";

            FFMpegCore.GlobalFFOptions.Configure(new FFMpegCore.FFOptions
            {
                BinaryFolder = Path.GetDirectoryName(ffmpegPath) ?? "",
                TemporaryFilesFolder = Path.GetTempPath()
            });

            services.AddSingleton(new SacdExtractService(sacdExtractPath));
            services.AddSingleton<DsdConvertService>();
            services.AddSingleton<AudioMetadataService>();
            services.AddSingleton<CueParser>();
        }
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Hardcode binary paths
- Throw for FFMPEG_PATH (ffmpeg on PATH is a valid default)
- Throw for SACD_EXTRACT_PATH being optional (only needed for ISO extraction, not DSD conversion)

**Revised approach:** SACD_EXTRACT_PATH should NOT throw at DI time. It should only be checked when the SACD extract command is actually invoked. This allows DSD conversion (non-ISO input) to work without sacd_extract installed.

```csharp
public void AddAudioServices()
{
    var sacdExtractPath = Environment.GetEnvironmentVariable("SACD_EXTRACT_PATH");
    var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";

    if (Directory.Exists(Path.GetDirectoryName(ffmpegPath)))
    {
        FFMpegCore.GlobalFFOptions.Configure(new FFMpegCore.FFOptions
        {
            BinaryFolder = Path.GetDirectoryName(ffmpegPath) ?? "",
            TemporaryFilesFolder = Path.GetTempPath()
        });
    }

    services.AddSingleton(new SacdExtractService(sacdExtractPath ?? "sacd_extract"));
    services.AddSingleton<DsdConvertService>();
    services.AddSingleton<AudioMetadataService>();
    services.AddSingleton<CueParser>();
}
```

**References:**
- `src/Services/LastFm/LastFmSetup.cs:1-34` — DI pattern
- `src/App/Program.cs:54-56` — existing `Add*Services()` calls
- FFMpegCore config: https://github.com/rosenbjerg/FFMpegCore#configuration

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add AudioSetup DI registration`

---

### Task 9: Create SacdExtractService.cs

**What to do:**
Create `src/Services/Audio/SacdExtractService.cs` — wraps sacd_extract CLI.

This service mirrors the Python `convert_iso_to_dff_and_cue` function:
1. `sacd_extract -P -i <iso>` → probe for stereo/mch presence
2. `sacd_extract -2 -e -c -C -i <iso>` → extract stereo DSDIFF Edit Master + CUE
3. `sacd_extract -m -e -c -C -i <iso>` → extract multichannel DSDIFF Edit Master + CUE

Key flags:
- `-2` / `-m`: stereo / multichannel
- `-e`: DSDIFF Edit Master (single track + CUE, avoids click trimming)
- `-c`: convert DST to DSD
- `-C`: export CUE sheet
- `-i <file>`: input ISO

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

internal sealed class SacdExtractService
{
    private static readonly Regex StereoPattern = new(
        @"Speaker config:\s*(?:Stereo|2)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly Regex MultichannelPattern = new(
        @"Speaker config:\s*(?:Multichannel|5|6)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private readonly string _binaryPath;

    public SacdExtractService(string binaryPath) => _binaryPath = binaryPath;

    public async Task<ErrorOr<SacdProbeResult>> ProbeAsync(
        string isoPath,
        CancellationToken ct = default
    )
    {
        ErrorOr<(string stdout, string stderr)> result = await RunProcessAsync(
            ["-P", "-i", isoPath],
            ct
        );

        if (result.IsError)
            return result.Errors;

        string output = result.Value.stdout + "\n" + result.Value.stderr;
        bool hasStereo = StereoPattern.IsMatch(output);
        bool hasMch = MultichannelPattern.IsMatch(output);

        if (!hasStereo && !hasMch)
            return Errors.Audio.ExtractionFailed(isoPath, "No stereo or multichannel tracks detected");

        return new SacdProbeResult(isoPath, hasStereo, hasMch);
    }

    public async Task<ErrorOr<List<string>>> ExtractAsync(
        string isoPath,
        string outputDir,
        bool multichannel,
        CancellationToken ct = default
    )
    {
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        string channelFlag = multichannel ? "-m" : "-2";
        string[] beforeDirs = Directory.GetDirectories(outputDir);

        ErrorOr<(string stdout, string stderr)> result = await RunProcessAsync(
            [channelFlag, "-e", "-c", "-C", "-i", isoPath],
            ct,
            outputDir
        );

        if (result.IsError)
            return result.Errors;

        string[] afterDirs = Directory.GetDirectories(outputDir);
        List<string> newDirs = afterDirs.Except(beforeDirs).ToList();

        if (newDirs.Count == 0)
        {
            string[] dffFiles = Directory.GetFiles(outputDir, "*.dff", SearchOption.AllDirectories);
            if (dffFiles.Length > 0)
                newDirs = [Path.GetDirectoryName(dffFiles[0])!];
        }

        return newDirs;
    }

    private async Task<ErrorOr<(string stdout, string stderr)>> RunProcessAsync(
        string[] args,
        CancellationToken ct,
        string? workingDir = null
    )
    {
        if (!File.Exists(_binaryPath) && !IsOnPath(_binaryPath))
            return Errors.Audio.BinaryNotFound("sacd_extract");

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory
        };

        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start sacd_extract");

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            string stderr = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                return Errors.Audio.ExtractionFailed(
                    psi.Arguments,
                    $"Exit code {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 500)]}"
                );

            return (stdout, stderr);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errors.Audio.ExtractionFailed(psi.Arguments, ex.Message);
        }
    }

    private static bool IsOnPath(string binaryName)
    {
        if (Path.IsPathRooted(binaryName))
            return File.Exists(binaryName);

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return false;

        string[] dirs = path.Split(Path.PathSeparator);
        return dirs.Any(d => File.Exists(Path.Combine(d, binaryName)) ||
                             File.Exists(Path.Combine(d, binaryName + ".exe")));
    }
}

internal sealed record SacdProbeResult(
    string IsoPath,
    bool HasStereo,
    bool HasMultichannel
);
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Add interfaces
- Use `async void`
- Bundle sacd_extract binary

**References:**
- Python `audio.py:189-253` — `convert_iso_to_dff_and_cue` function
- Python `utils.py:1-31` — `run_command` helper
- sacd_extract CLI flags from reference guide (Part III)
- `src/Core/Errors.cs` — `Errors.Audio` class from Phase 0

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds
- ProbeAsync returns stereo/mch detection
- ExtractAsync runs sacd_extract and returns new directories
- BinaryNotFound error returned when sacd_extract missing

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add SacdExtractService CLI wrapper`

---

## Verify Phase 2

```bash
dotnet build src/Services/Audio/Audio.csproj
```

Clean build. DI setup + sacd_extract wrapper in place.

**Dependencies:** Phase 1
**Blocks:** Phase 3
