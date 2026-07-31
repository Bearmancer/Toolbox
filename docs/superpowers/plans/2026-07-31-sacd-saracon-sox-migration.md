# SACD Pipeline Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all FFMpegCore/ffmpeg usage with saracon (DSD→PCM) and sox (gain detection, splitting, resampling), per the SACD.red.md guide.

**Architecture:** Service-per-binary wrappers (SaraconService, SoxService) mirror the existing SacdExtractService pattern. DsdConvertService becomes an analysis/orchestration layer (DFF header probe, gain calculation) delegating to those wrappers. PipelineOrchestrator shifts from per-track ffmpeg seek+convert to convert-once-then-split: one saracon D2P pass produces a master WAV, sox trim splits it per CUE track.

**Tech Stack:** .NET 11.0, Spectre.Console.Cli, ErrorOr, ATL.NET, ProcessRunner (custom), saracon.exe, sox.exe

**Spec:** `docs/superpowers/specs/2026-07-31-sacd-saracon-sox-migration-design.md`

## Global Constraints

- .NET 11.0 preview SDK. `SuppressNETCoreSdkPreviewMessage` set.
- Zero inline/explanatory comments. Code is self-documenting.
- One class per file. No Constants.cs, no Helpers.cs.
- `using ErrorOr;` inside namespace (project pattern).
- Primary constructors on classes.
- ErrorOr<T> for all fallible operations.
- No test NuGet packages. Verification = `dotnet build` + `lsp_diagnostics`.
- No `global::`, no fully-qualified inline invocations, no `#pragma warning disable`.
- No PropertyNamingPolicy on JsonSerializerOptions.
- `.editorconfig` enforced as errors. Build must pass clean.
- Binary resolution: PATH only. No env vars (SARACON_PATH, SOX_PATH, SACD_EXTRACT_PATH, FFMPEG_PATH all removed). Eager validation at DI registration — throw `InvalidOperationException` if any binary missing.
- Saracon flags: always include `-T` (tolerant) and `-V all` (verbose).

## Parallel Execution Waves

```
Wave 1 (parallel):  Task 1 (SaraconService)  |  Task 2 (SoxService)  |  Task 3 (ProcessRunner + SacdExtractService)
Wave 2 (parallel):  Task 4 (DsdConvertService rewrite)  |  Task 5 (AudioSetup + csproj + packages)
Wave 3 (parallel):  Task 6 (PipelineOrchestrator)  |  Task 7 (DsdConvertCommand + Errors)
Wave 4 (sequential): Task 8 (AGENTS.md)  →  Task 9 (Build verification)
```

---

### Task 1: SaraconService.cs (NEW)

**Wave:** 1 (parallel with Tasks 2, 3)

**Files:**
- Create: `src/Services/Audio/SaraconService.cs`

**Interfaces:**
- Consumes: `ProcessRunner.RunAsync(string binaryPath, string[] args, CancellationToken ct, string? workingDir = null)` → `ErrorOr<ProcessResult>`
- Produces: `SaraconService.ConvertDsdToPcmAsync(...)` → `ErrorOr<string>` (output file path), `SaraconService.ConvertDsdToFlacAsync(...)` → `ErrorOr<string>`

- [ ] **Step 1: Create SaraconService.cs**

```csharp
using System.Globalization;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SaraconService(ProcessRunner processRunner, string binaryPath)
{
	public async Task<ErrorOr<string>> ConvertDsdToPcmAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "wav");
		return await RunConversionAsync(inputDff, outputDir, "wav", args, ct);
	}

	public async Task<ErrorOr<string>> ConvertDsdToFlacAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "flac");
		return await RunConversionAsync(inputDff, outputDir, "flac", args, ct);
	}

	private static string[] BuildD2pArgs(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		string format
	) =>
	[
		"-c", "d2p",
		"-r", sampleRate.ToString(CultureInfo.InvariantCulture),
		"-f", format,
		"-n", $"{bitDepth}bit",
		"-d", "tpdf",
		"-g", gainDb.ToString("F2", CultureInfo.InvariantCulture),
		"-T",
		"-V", "all",
		"-t", outputDir,
		inputDff,
	];

	private async Task<ErrorOr<string>> RunConversionAsync(
		string inputDff,
		string outputDir,
		string extension,
		string[] args,
		CancellationToken ct
	)
	{
		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		var result = await processRunner.RunAsync(binaryPath, args, ct);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		var expectedOutput = Path.Combine(
			outputDir,
			Path.GetFileNameWithoutExtension(inputDff) + $".{extension}"
		);

		if (!File.Exists(expectedOutput))
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon reported success but {expectedOutput} not found"
			);

		return expectedOutput;
	}
}
```

- [ ] **Step 2: Verify** — `dotnet build` (will fail until Wave 2 wires DI, but file must compile syntactically). Run `lsp_diagnostics` on the file.

---

### Task 2: SoxService.cs (NEW)

**Wave:** 1 (parallel with Tasks 1, 3)

**Files:**
- Create: `src/Services/Audio/SoxService.cs`

**Interfaces:**
- Consumes: `ProcessRunner.RunAsync(...)` → `ErrorOr<ProcessResult>`
- Produces: `SoxService.SplitTrackAsync(...)`, `SoxService.GetPeakLevelAsync(...)`, `SoxService.GetDurationAsync(...)`, `SoxService.DeriveFlacAsync(...)`

- [ ] **Step 1: Create SoxService.cs**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SoxService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly Regex PeakLevelPattern = new(
		@"Pk lev dB\s+(-?\d+\.?\d*)",
		RegexOptions.Compiled
	);

	public async Task<ErrorOr<string>> SplitTrackAsync(
		string sourcePcm,
		string outputFlac,
		TimeSpan start,
		TimeSpan? duration,
		CancellationToken ct = default
	)
	{
		List<string> args = [sourcePcm, outputFlac, "trim", FormatSeconds(start)];
		if (duration is { } d && d > TimeSpan.Zero)
			args.Add(FormatSeconds(d));

		var result = await processRunner.RunAsync(binaryPath, [.. args], ct);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourcePcm,
				$"sox split exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		return outputFlac;
	}

	public async Task<ErrorOr<double>> GetPeakLevelAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		var result = await processRunner.RunAsync(binaryPath, [filePath, "-n", "stats"], ct);
		if (result.IsError)
			return result.Errors;

		var output = result.Value.Stdout + "\n" + result.Value.Stderr;
		var match = PeakLevelPattern.Match(output);
		if (!match.Success)
			return Errors.Audio.GainDetectionFailed(filePath, "Could not parse sox stats output");

		return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	public async Task<ErrorOr<TimeSpan>> GetDurationAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		var result = await processRunner.RunAsync(binaryPath, ["--i", "-D", filePath], ct);
		if (result.IsError)
			return result.Errors;

		if (!double.TryParse(
			result.Value.Stdout.Trim(),
			CultureInfo.InvariantCulture,
			out var seconds
		))
			return Errors.Audio.ProbeFailed(filePath, "Could not parse sox duration output");

		return TimeSpan.FromSeconds(seconds);
	}

	public async Task<ErrorOr<string>> DeriveFlacAsync(
		string sourceFlac,
		string outputFlac,
		int targetSampleRate,
		CancellationToken ct = default
	)
	{
		var result = await processRunner.RunAsync(
			binaryPath,
			[
				sourceFlac,
				"-b", "16",
				outputFlac,
				"rate", "-v",
				targetSampleRate.ToString(CultureInfo.InvariantCulture),
			],
			ct
		);

		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourceFlac,
				$"sox derive exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		return outputFlac;
	}

	private static string FormatSeconds(TimeSpan t) =>
		t.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 2: Verify** — `lsp_diagnostics` on the file.

---

### Task 3: ProcessRunner.IsOnPath + SacdExtractService PATH-only

**Wave:** 1 (parallel with Tasks 1, 2)

**Files:**
- Modify: `src/Services/Audio/ProcessRunner.cs` (line 56: `private` → `public`)
- Modify: `src/Services/Audio/SacdExtractService.cs` (no change needed — already takes `string binaryPath`)

**Interfaces:**
- Produces: `ProcessRunner.IsOnPath(string binaryName)` → `bool` (public static)

- [ ] **Step 1: Make IsOnPath public**

In `ProcessRunner.cs`, change line 56:
```csharp
// Before:
private static bool IsOnPath(string binaryName)
// After:
public static bool IsOnPath(string binaryName)
```

- [ ] **Step 2: Verify** — `lsp_diagnostics` on ProcessRunner.cs.

---

### Task 4: DsdConvertService.cs rewrite

**Wave:** 2 (parallel with Task 5; depends on Tasks 1, 2)

**Files:**
- Modify: `src/Services/Audio/DsdConvertService.cs` (full rewrite)

**Interfaces:**
- Consumes: `SaraconService.ConvertDsdToPcmAsync(...)`, `SoxService.GetPeakLevelAsync(...)`, `SoxService.GetDurationAsync(...)`, `SoxService.DeriveFlacAsync(...)`
- Produces: `DsdConvertService.ProbeDsdAsync(...)`, `DsdConvertService.CalculateGainAsync(...)`, `DsdConvertService.ConvertFullDffAsync(...)`, `DsdConvertService.DeriveFlacAsync(...)`

- [ ] **Step 1: Rewrite DsdConvertService.cs**

Replace entire file contents:

```csharp
using System.Buffers.Binary;
using System.Globalization;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class DsdConvertService(SaraconService saracon, SoxService sox)
{
	private const double TargetHeadroomDb = -0.5;
	private const int ProbeSampleRate = 88200;
	private const int ProbeBitDepth = 24;

	public async Task<ErrorOr<DsdProbeResult>> ProbeDsdAsync(
		string dffFilePath,
		CancellationToken ct = default
	)
	{
		try
		{
			await using var stream = File.OpenRead(dffFilePath);
			using var reader = new BinaryReader(stream);

			var magic = new string(reader.ReadChars(4));
			if (magic != "FRM8")
				return Errors.Audio.ProbeFailed(dffFilePath, $"Not a DSDIFF file (magic: {magic})");

			reader.ReadBytes(8);
			var formType = new string(reader.ReadChars(4));
			if (formType != "DSD ")
				return Errors.Audio.ProbeFailed(dffFilePath, $"Unexpected form type: {formType}");

			var sampleRate = 0;
			var channels = 0;

			while (stream.Position < stream.Length - 12)
			{
				var chunkId = new string(reader.ReadChars(4));
				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));

				if (chunkId == "PROP")
				{
					var propType = new string(reader.ReadChars(4));
					if (propType == "SND ")
					{
						var propEnd = stream.Position + (long)chunkSize - 4;
						while (stream.Position < propEnd - 12)
						{
							var subId = new string(reader.ReadChars(4));
							var subSize = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));

							if (subId == "FS  ")
							{
								sampleRate = (int)BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
								if (subSize > 4)
									reader.ReadBytes((int)subSize - 4);
							}
							else if (subId == "CHNL")
							{
								channels = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
								if (subSize > 2)
									reader.ReadBytes((int)subSize - 2);
							}
							else
							{
								reader.ReadBytes((int)subSize);
							}

							if (subSize % 2 != 0 && stream.Position < stream.Length)
								reader.ReadByte();
						}
					}
					else
					{
						reader.ReadBytes((int)chunkSize - 4);
					}
				}
				else
				{
					reader.ReadBytes((int)chunkSize);
				}

				if (chunkSize % 2 != 0 && stream.Position < stream.Length)
					reader.ReadByte();

				if (sampleRate > 0 && channels > 0)
					break;
			}

			if (sampleRate == 0 || channels == 0)
				return Errors.Audio.ProbeFailed(dffFilePath, "Could not parse FS or CHNL chunks from DFF header");

			return new DsdProbeResult(dffFilePath, "dsd", sampleRate, channels);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Errors.Audio.ProbeFailed(dffFilePath, ex.Message);
		}
	}

	public async Task<ErrorOr<double>> CalculateGainAsync(
		string dffFilePath,
		CancellationToken ct = default
	)
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"gain_probe_{Guid.NewGuid():N}");

		try
		{
			var convertResult = await saracon.ConvertDsdToPcmAsync(
				dffFilePath, tempDir, ProbeSampleRate, ProbeBitDepth, 0.0, ct
			);
			if (convertResult.IsError)
				return convertResult.Errors;

			var peakResult = await sox.GetPeakLevelAsync(convertResult.Value, ct);
			if (peakResult.IsError)
				return peakResult.Errors;

			var gain = TargetHeadroomDb - peakResult.Value;
			return Math.Min(gain, 6.0);
		}
		finally
		{
			if (Directory.Exists(tempDir))
				Directory.Delete(tempDir, recursive: true);
		}
	}

	public async Task<ErrorOr<ConversionResult>> ConvertFullDffAsync(
		string inputDff,
		string outputFlac,
		DsdConversionSettings settings,
		CancellationToken ct = default
	)
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"saracon_{Guid.NewGuid():N}");

		try
		{
			var convertResult = await saracon.ConvertDsdToFlacAsync(
				inputDff, tempDir, settings.SampleRate, settings.BitDepth, settings.GainDb, ct
			);
			if (convertResult.IsError)
				return convertResult.Errors;

			var tempFlac = convertResult.Value;
			var outputDir = Path.GetDirectoryName(outputFlac);
			if (!string.IsNullOrEmpty(outputDir))
				Directory.CreateDirectory(outputDir);

			File.Move(tempFlac, outputFlac, overwrite: true);

			var durationResult = await sox.GetDurationAsync(outputFlac, ct);
			if (durationResult.IsError)
				return durationResult.Errors;

			var fileInfo = new FileInfo(outputFlac);
			fileInfo.Refresh();

			return new ConversionResult(outputFlac, durationResult.Value, fileInfo.Length);
		}
		finally
		{
			if (Directory.Exists(tempDir))
				Directory.Delete(tempDir, recursive: true);
		}
	}

	public async Task<ErrorOr<ConversionResult>> DeriveFlacAsync(
		string sourceFlac,
		string outputFlac,
		int targetSampleRate,
		CancellationToken ct = default
	)
	{
		var deriveResult = await sox.DeriveFlacAsync(sourceFlac, outputFlac, targetSampleRate, ct);
		if (deriveResult.IsError)
			return deriveResult.Errors;

		var durationResult = await sox.GetDurationAsync(outputFlac, ct);
		if (durationResult.IsError)
			return durationResult.Errors;

		var fileInfo = new FileInfo(outputFlac);
		fileInfo.Refresh();

		return new ConversionResult(outputFlac, durationResult.Value, fileInfo.Length);
	}
}
```

- [ ] **Step 2: Verify** — `lsp_diagnostics` on DsdConvertService.cs. Expect errors until AudioSetup wires the new constructor (Task 5).

---

### Task 5: AudioSetup.cs + Audio.csproj + Directory.Packages.props

**Wave:** 2 (parallel with Task 4; depends on Tasks 1, 2, 3)

**Files:**
- Modify: `src/Services/Audio/AudioSetup.cs`
- Modify: `src/Services/Audio/Audio.csproj`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Rewrite AudioSetup.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Services.Audio;

public static class AudioSetup
{
	extension(IServiceCollection services)
	{
		public void AddAudioServices()
		{
			ValidateBinaryOnPath("saracon");
			ValidateBinaryOnPath("sox");
			ValidateBinaryOnPath("sacd_extract");

			services.AddSingleton<ProcessRunner>();
			services.AddSingleton<PathValidator>();
			services.AddSingleton<DiskSpaceChecker>();
			services.AddSingleton(sp => new SacdExtractService(
				sp.GetRequiredService<ProcessRunner>(),
				"sacd_extract"
			));
			services.AddSingleton(sp => new SaraconService(
				sp.GetRequiredService<ProcessRunner>(),
				"saracon"
			));
			services.AddSingleton(sp => new SoxService(
				sp.GetRequiredService<ProcessRunner>(),
				"sox"
			));
			services.AddSingleton<DsdConvertService>();
			services.AddSingleton<AudioMetadataService>();
			services.AddSingleton<CueParser>();
			services.AddSingleton<PipelineOrchestrator>();
		}
	}

	private static void ValidateBinaryOnPath(string name)
	{
		if (!ProcessRunner.IsOnPath(name))
			throw new InvalidOperationException(
				$"{name} not found on PATH. Install it and ensure it is available in your system PATH."
			);
	}
}
```

- [ ] **Step 2: Remove FFMpegCore from Audio.csproj**

Remove this line from `src/Services/Audio/Audio.csproj`:
```xml
<PackageReference Include="FFMpegCore" />
```

- [ ] **Step 3: Remove FFMpegCore from Directory.Packages.props**

Remove this line from `Directory.Packages.props`:
```xml
<PackageVersion Include="FFMpegCore" Version="5.4.0" />
```

- [ ] **Step 4: Verify** — `dotnet build` (may fail until Tasks 6, 7 complete). `lsp_diagnostics` on AudioSetup.cs.

---

### Task 6: PipelineOrchestrator.cs — convert-once-then-split

**Wave:** 3 (parallel with Task 7; depends on Tasks 4, 5)

**Files:**
- Modify: `src/Services/Audio/PipelineOrchestrator.cs`

**Interfaces:**
- Consumes: `SaraconService.ConvertDsdToPcmAsync(...)`, `SoxService.SplitTrackAsync(...)`, `SoxService.GetDurationAsync(...)`, `SoxService.DeriveFlacAsync(...)`, `DsdConvertService.ProbeDsdAsync(...)`, `DsdConvertService.CalculateGainAsync(...)`

- [ ] **Step 1: Update constructor**

Add `SaraconService saraconService` and `SoxService soxService` to the primary constructor:

```csharp
public sealed class PipelineOrchestrator(
	SacdExtractService extractService,
	DsdConvertService convertService,
	SaraconService saraconService,
	SoxService soxService,
	AudioMetadataService metadataService,
	CueParser cueParser,
	PathValidator pathValidator,
	DiskSpaceChecker diskSpaceChecker
)
```

- [ ] **Step 2: Rewrite ProcessExtractedDirectoryAsync**

After gain calculation and cue parsing, convert the full DFF to WAV once, then pass the WAV path to ConvertTracksAsync:

```csharp
private async Task<ErrorOr<Success>> ProcessExtractedDirectoryAsync(
	string dffDir,
	AudioOutputFormat format,
	CancellationToken ct
)
{
	var dffFiles = Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories);
	var cueFiles = Directory.GetFiles(dffDir, "*.cue", SearchOption.AllDirectories);

	if (dffFiles.Length == 0)
		return Errors.Audio.NoDffFound(dffDir);
	if (cueFiles.Length == 0)
		return Errors.Audio.NoCueFound(dffDir);

	var dffFile = dffFiles[0];
	var cueFile = cueFiles[0];

	Telemetry.Info("Probing DSD file: {Dff}", Path.GetFileName(dffFile));
	var dsdProbe = await convertService.ProbeDsdAsync(dffFile, ct);
	if (dsdProbe.IsError)
		return dsdProbe.Errors;

	Telemetry.Info(
		"DSD: {Codec} @ {Rate} Hz, {Channels} ch",
		dsdProbe.Value.CodecName,
		dsdProbe.Value.SampleRate,
		dsdProbe.Value.Channels
	);

	Telemetry.Info("Calculating gain for {Dff}", Path.GetFileName(dffFile));
	var gainResult = await convertService.CalculateGainAsync(dffFile, ct);
	if (gainResult.IsError)
		return gainResult.Errors;

	var gain = gainResult.Value;
	Telemetry.Info("Gain: {Gain:F2} dB", gain);

	Telemetry.Info("Parsing CUE: {Cue}", Path.GetFileName(cueFile));
	var cueResult = cueParser.Parse(cueFile);
	if (cueResult.IsError)
		return cueResult.Errors;

	var cue = cueResult.Value;

	var (rate16, rate24) = dsdProbe.Value.SampleRate switch
	{
		2822400 => (44100, 88200),
		5644800 => (88200, 176400),
		_ => default,
	};

	if (rate16 == 0)
		return Errors.Audio.ConversionFailed(
			dffFile,
			$"Unsupported DSD sample rate {dsdProbe.Value.SampleRate} Hz. Expected 2822400 (DSD64) or 5644800 (DSD128)."
		);

	var convertResult = format switch
	{
		AudioOutputFormat.Bit16 => await ConvertTracksAsync(
			dffFile, dffDir, cue, new DsdConversionSettings(rate16, 16, gain), ct
		),
		AudioOutputFormat.Bit24 => await ConvertTracksAsync(
			dffFile, dffDir, cue, new DsdConversionSettings(rate24, 24, gain), ct
		),
		AudioOutputFormat.Both => await ConvertBothFormatsAsync(
			dffFile, dffDir, cue, gain, rate16, rate24, ct
		),
		_ => Errors.Audio.ConversionFailed(dffFile, $"Unsupported format: {format}"),
	};

	if (convertResult.IsError)
		return convertResult.Errors;

	return Result.Success;
}
```

- [ ] **Step 3: Rewrite ConvertTracksAsync**

Convert full DFF → WAV once with saracon, then split per cue track with sox:

```csharp
private async Task<ErrorOr<Success>> ConvertTracksAsync(
	string dffFile,
	string outputDir,
	CueSheet cue,
	DsdConversionSettings convSettings,
	CancellationToken ct
)
{
	Telemetry.Info("Converting DFF to PCM master: {Dff}", Path.GetFileName(dffFile));
	var masterResult = await saraconService.ConvertDsdToPcmAsync(
		dffFile, outputDir, convSettings.SampleRate, convSettings.BitDepth, convSettings.GainDb, ct
	);
	if (masterResult.IsError)
		return masterResult.Errors;

	var masterPcm = masterResult.Value;
	var errors = new List<string>();

	foreach (var track in cue.Tracks)
	{
		var trackNum = track.TrackNumber.ToString("D2");
		var safeTitle = SanitizeFilename(track.Title);
		var outputFlac = Path.Combine(outputDir, $"{trackNum}. {safeTitle}.flac");

		Telemetry.Info("Splitting track {Num}: {Title}", trackNum, track.Title);

		var splitResult = await soxService.SplitTrackAsync(
			masterPcm, outputFlac, track.StartTime, track.Duration, ct
		);

		if (splitResult.IsError)
		{
			var msg = splitResult.Errors[0].Description;
			Telemetry.Warn("Track {Num} split failed (recoverable): {Error}", trackNum, msg);
			errors.Add(msg);
			continue;
		}

		var tagResult = metadataService.CopyMetadataFromCue(outputFlac, cue, track);
		if (tagResult.IsError)
			Telemetry.Warn(
				"Tagging failed for {File}: {Error}",
				outputFlac,
				tagResult.Errors[0].Description
			);
	}

	if (File.Exists(masterPcm))
		File.Delete(masterPcm);

	if (errors.Count == cue.Tracks.Count)
		return Errors.Audio.ConversionFailed(dffFile, "All tracks failed conversion.");

	return Result.Success;
}
```

- [ ] **Step 4: Update ConvertBothFormatsAsync**

Same shape — convert master once, split, then derive 16-bit per split track:

```csharp
private async Task<ErrorOr<Success>> ConvertBothFormatsAsync(
	string dffFile,
	string dffDir,
	CueSheet cue,
	double gain,
	int rate16,
	int rate24,
	CancellationToken ct
)
{
	var conv24 = new DsdConversionSettings(rate24, 24, gain);
	var masterResult = await ConvertTracksAsync(dffFile, dffDir, cue, conv24, ct);
	if (masterResult.IsError)
		return masterResult.Errors;

	var parentDir = Path.GetDirectoryName(dffDir) ?? dffDir;
	var derivedDir = Path.Combine(
		parentDir,
		$"{Path.GetFileName(dffDir)} [16-bit {rate16 / 1000.0:F1}]"
	);
	Directory.CreateDirectory(derivedDir);

	foreach (var flac in Directory.GetFiles(dffDir, "*.flac"))
	{
		var dest = Path.Combine(derivedDir, Path.GetFileName(flac));
		Telemetry.Info("Deriving 16-bit: {File}", Path.GetFileName(flac));
		var deriveResult = await soxService.DeriveFlacAsync(flac, dest, rate16, ct);
		if (deriveResult.IsError)
			Telemetry.Warn(
				"Derive failed for {File}: {Error}",
				flac,
				deriveResult.Errors[0].Description
			);
	}

	return Result.Success;
}
```

- [ ] **Step 5: Verify** — `lsp_diagnostics` on PipelineOrchestrator.cs.

---

### Task 7: DsdConvertCommand.cs + Errors.cs

**Wave:** 3 (parallel with Task 6; depends on Task 4)

**Files:**
- Modify: `src/CLI/Audio/DsdConvertCommand.cs`
- Modify: `src/Core/Errors.cs`

- [ ] **Step 1: Update Errors.cs ProbeFailed message**

In `src/Core/Errors.cs`, change the ProbeFailed method:

```csharp
// Before:
Error.Failure("Audio.ProbeFailed", $"ffprobe failed for {file}: {reason}");
// After:
Error.Failure("Audio.ProbeFailed", $"DSD probe failed for {file}: {reason}");
```

- [ ] **Step 2: Verify DsdConvertCommand.cs compiles**

DsdConvertCommand uses `DsdConvertService.ProbeDsdAsync`, `CalculateGainAsync`, `ConvertFullDffAsync`, `DeriveFlacAsync` — all signatures preserved. The constructor changes from `DsdConvertService(ProcessRunner)` to `DsdConvertService(SaraconService, SoxService)` but DI handles that. No code changes expected in DsdConvertCommand.cs itself.

Run `lsp_diagnostics` to confirm.

---

### Task 8: AGENTS.md updates

**Wave:** 4 (sequential; depends on all above)

**Files:**
- Modify: `src/Services/Audio/AGENTS.md`
- Modify: `AGENTS.md` (root, if it references ffmpeg in audio context)

- [ ] **Step 1: Update src/Services/Audio/AGENTS.md**

Changes:
- STRUCTURE: Add `SaraconService.cs` and `SoxService.cs`. Update `DsdConvertService.cs` description to "DFF header probe, gain orchestration, delegates to SaraconService/SoxService". Remove FFMpegCore references.
- WHERE TO LOOK: Update "Change DSD→PCM filter" → "Change DSD→PCM conversion" → `SaraconService.cs`. Add "Change sox operations" → `SoxService.cs`. Update "Change binary paths" → "AudioSetup.cs — PATH validation, no env vars".
- CONVENTIONS: Remove "FFMpegCore for FFmpeg" line. Add "SaraconService/SoxService: thin binary wrappers via ProcessRunner. Same pattern as SacdExtractService." Add "DsdConvertService: analysis/orchestration layer. DFF header parsing, gain calculation."
- ENVIRONMENT VARIABLES: Remove entire table. Replace with: "All binaries (saracon, sox, sacd_extract) resolved from PATH. Validated eagerly at DI registration. No env vars."
- ANTI-PATTERNS: Remove "NEVER add SoX dependency (FFmpeg handles downsampling)". Remove "NEVER use Xabe.FFmpeg". Remove "NEVER use AudioWorks". Keep "NEVER bundle binaries in the repo". Keep "NEVER hardcode binary paths".
- PIPELINE: Update to reflect saracon/sox flow.

- [ ] **Step 2: Check root AGENTS.md**

Grep for ffmpeg/FFMpegCore references in root AGENTS.md. Update if present.

---

### Task 9: Build verification

**Wave:** 4 (sequential; after Task 8)

- [ ] **Step 1: Full build**

```powershell
dotnet build
```

Expected: Exit code 0, zero warnings related to our changes.

- [ ] **Step 2: LSP diagnostics on all changed files**

Run `lsp_diagnostics` on:
- `src/Services/Audio/SaraconService.cs`
- `src/Services/Audio/SoxService.cs`
- `src/Services/Audio/DsdConvertService.cs`
- `src/Services/Audio/PipelineOrchestrator.cs`
- `src/Services/Audio/AudioSetup.cs`
- `src/Services/Audio/ProcessRunner.cs`
- `src/CLI/Audio/DsdConvertCommand.cs`
- `src/Core/Errors.cs`

Expected: Zero errors.

- [ ] **Step 3: Verify no FFMpegCore references remain**

```powershell
grep -r "FFMpegCore\|FFProbe\|FFMpegArguments\|ffmpeg" --include="*.cs" src/
```

Expected: Zero matches.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(audio): migrate SACD pipeline from ffmpeg to saracon + sox

Replace FFMpegCore with saracon (DSD→PCM) and sox (gain detection,
splitting, resampling) per SACD.red.md guide.

- Add SaraconService, SoxService (service-per-binary pattern)
- Rewrite DsdConvertService as analysis/orchestration layer
- DFF probing via binary header parse (replaces FFProbe)
- Gain detection via saracon 0dB → temp WAV → sox stats
- Convert-once-then-split: one DSD decode per disc (was N)
- Remove FFMpegCore NuGet dependency
- PATH-only binary resolution with eager validation
- Update AGENTS.md to reflect new pipeline"
```
