# SRP Refactor + RED Adherence Fix Pass

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 11 Oracle SRP findings, restructure output directories to sibling pattern, audit error messages, remove dead code. Orchestrator becomes purely orchestration — zero business logic.

**Architecture:** DsdConvertService becomes the true conversion facade absorbing SaraconService/SoxService/AudioMetadataService calls. PipelineOrchestrator drops to 6 deps and only sequences steps. Sample-rate mapping extracted to DsdConversionSettings factory. Output dirs use sibling pattern: `../Karajan (Stereo)/`.

**Tech Stack:** .NET 11.0, ErrorOr, ProcessRunner, saracon.exe, sox.exe

**Spec:** `docs/superpowers/specs/2026-07-31-sacd-saracon-sox-migration-design.md`

## Global Constraints

- Zero inline/explanatory comments
- Primary constructors, `using ErrorOr;` inside namespace, tabs
- ErrorOr<T> for all fallible operations
- No test NuGet packages. Verification = `dotnet build` + `lsp_diagnostics`
- No `global::`, no `#pragma warning disable`
- PATH-only binary resolution. No custom env vars anywhere.
- Orchestrator MUST NOT call SaraconService or SoxService directly — only DsdConvertService

## Parallel Execution Waves

```
Wave 1 (parallel):  Task 1 (AudioModels + Errors)  |  Task 2 (DsdConvertService facade)
Wave 2 (parallel):  Task 3 (PipelineOrchestrator)  |  Task 4 (DsdConvertCommand + AudioSetup)
Wave 3 (sequential): Task 5 (Build verify)  →  Task 6 (AGENTS.md)
```

---

### Task 1: AudioModels.cs + Errors.cs

**Wave:** 1 (parallel with Task 2)

**Files:**
- Modify: `src/Services/Audio/AudioModels.cs`
- Modify: `src/Core/Errors.cs`

- [ ] **Step 1: Add ForDsdRate factory to AudioModels.cs**

Add after `DsdConversionSettings` record:

```csharp
public sealed record DsdConversionSettings(int SampleRate, int BitDepth, double GainDb)
{
	public static (DsdConversionSettings Primary, DsdConversionSettings? Derived) ForDsdRate(
		int dsdSampleRate,
		AudioOutputFormat format,
		double gain
	) =>
		dsdSampleRate switch
		{
			2822400 => format switch
			{
				AudioOutputFormat.Bit16 => (new DsdConversionSettings(44100, 16, gain), null),
				AudioOutputFormat.Bit24 => (new DsdConversionSettings(88200, 24, gain), null),
				AudioOutputFormat.Both => (
					new DsdConversionSettings(88200, 24, gain),
					new DsdConversionSettings(44100, 16, gain)
				),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			5644800 => format switch
			{
				AudioOutputFormat.Bit16 => (new DsdConversionSettings(88200, 16, gain), null),
				AudioOutputFormat.Bit24 => (new DsdConversionSettings(176400, 24, gain), null),
				AudioOutputFormat.Both => (
					new DsdConversionSettings(176400, 24, gain),
					new DsdConversionSettings(88200, 16, gain)
				),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			_ => throw new InvalidOperationException(
				$"Unsupported DSD sample rate {dsdSampleRate} Hz. Expected 2822400 (DSD64) or 5644800 (DSD128)."
			),
		};
}
```

- [ ] **Step 2: Remove PipelineStepResult from AudioModels.cs**

Delete the `PipelineStepResult` record (unused dead code).

- [ ] **Step 3: Fix Errors.cs BinaryNotFound message**

Change:
```csharp
$"{name} binary not found. Set {name.ToUpper()}_PATH in .env."
```
To:
```csharp
$"{name} not found on system PATH. Install it and ensure it is available in your system PATH."
```

- [ ] **Step 4: Remove Errors.Audio.UnsupportedDsdFormat**

Delete the `UnsupportedDsdFormat` method (never called post-migration — ProbeDsdAsync parses DFF header directly, doesn't check codec names).

- [ ] **Step 5: Verify** — `lsp_diagnostics` on both files.

---

### Task 2: DsdConvertService facade expansion

**Wave:** 1 (parallel with Task 1)

**Files:**
- Modify: `src/Services/Audio/DsdConvertService.cs`

**Interfaces:**
- Consumes: `SaraconService.ConvertDsdToPcmAsync`, `SoxService.SplitTrackAsync`, `SoxService.GetDurationAsync`, `SoxService.DeriveFlacAsync`, `AudioMetadataService.CopyMetadataFromCue`
- Produces: `ConvertAndSplitAsync(...)`, `DeriveDirectoryAsync(...)`

- [ ] **Step 1: Add AudioMetadataService to constructor**

```csharp
public sealed class DsdConvertService(
	SaraconService saracon,
	SoxService sox,
	AudioMetadataService metadata
)
```

- [ ] **Step 2: Add ConvertAndSplitAsync**

This is the facade method that absorbs the convert-once-then-split flow from PipelineOrchestrator:

```csharp
public async Task<ErrorOr<List<string>>> ConvertAndSplitAsync(
	string dffFile,
	string outputDir,
	CueSheet cue,
	DsdConversionSettings settings,
	CancellationToken ct = default
)
{
	var masterResult = await saracon.ConvertDsdToPcmAsync(
		dffFile, outputDir, settings.SampleRate, settings.BitDepth, settings.GainDb, ct
	);
	if (masterResult.IsError)
		return masterResult.Errors;

	var masterPcm = masterResult.Value;
	var outputFiles = new List<string>();
	var errors = new List<string>();

	foreach (var track in cue.Tracks)
	{
		var trackNum = track.TrackNumber.ToString("D2");
		var safeTitle = SanitizeFilename(track.Title);
		var outputFlac = Path.Combine(outputDir, $"{trackNum}. {safeTitle}.flac");

		var splitResult = await sox.SplitTrackAsync(
			masterPcm, outputFlac, track.StartTime, track.Duration, ct
		);

		if (splitResult.IsError)
		{
			errors.Add(splitResult.Errors[0].Description);
			continue;
		}

		outputFiles.Add(outputFlac);

		var tagResult = metadata.CopyMetadataFromCue(outputFlac, cue, track);
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

	return outputFiles;
}

private static string SanitizeFilename(string name)
{
	var invalid = Path.GetInvalidFileNameChars();
	return string.Join("-", name.Split(invalid)).Trim();
}
```

Add `using Core;` at top for Telemetry.

- [ ] **Step 3: Add DeriveDirectoryAsync**

```csharp
public async Task<ErrorOr<Success>> DeriveDirectoryAsync(
	string sourceDir,
	string derivedDir,
	int targetSampleRate,
	CancellationToken ct = default
)
{
	Directory.CreateDirectory(derivedDir);

	foreach (var flac in Directory.GetFiles(sourceDir, "*.flac"))
	{
		var dest = Path.Combine(derivedDir, Path.GetFileName(flac));
		var deriveResult = await sox.DeriveFlacAsync(flac, dest, targetSampleRate, ct);
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

- [ ] **Step 4: Verify** — `lsp_diagnostics`.

---

### Task 3: PipelineOrchestrator — pure orchestration

**Wave:** 2 (parallel with Task 4; depends on Tasks 1, 2)

**Files:**
- Modify: `src/Services/Audio/PipelineOrchestrator.cs`

- [ ] **Step 1: Remove SaraconService and SoxService from constructor**

```csharp
public sealed class PipelineOrchestrator(
	SacdExtractService extractService,
	DsdConvertService convertService,
	CueParser cueParser,
	PathValidator pathValidator,
	DiskSpaceChecker diskSpaceChecker
)
```

Remove `AudioMetadataService` too — it's now inside DsdConvertService.

- [ ] **Step 2: Update ProcessIsoAsync — sibling output directory**

Change channel directory from child `[Suffix]` to sibling `Name (Suffix)`:

```csharp
var isoDir = Path.GetDirectoryName(isoPath) ?? isoPath;
var discName = Path.GetFileNameWithoutExtension(isoPath);
var parentDir = Path.GetDirectoryName(isoDir) ?? isoDir;
var suffix = extractMch ? "Multichannel" : "Stereo";
var channelDir = Path.Combine(parentDir, $"{Path.GetFileName(isoDir)} ({suffix})");
```

- [ ] **Step 3: Rewrite ProcessExtractedDirectoryAsync — delegate to facade**

Slim down to pure orchestration. Use `DsdConversionSettings.ForDsdRate` factory:

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

	var dsdProbe = await convertService.ProbeDsdAsync(dffFile, ct);
	if (dsdProbe.IsError)
		return dsdProbe.Errors;

	var gainResult = await convertService.CalculateGainAsync(dffFile, ct);
	if (gainResult.IsError)
		return gainResult.Errors;

	var cueResult = cueParser.Parse(cueFile);
	if (cueResult.IsError)
		return cueResult.Errors;

	var (primary, derived) = DsdConversionSettings.ForDsdRate(
		dsdProbe.Value.SampleRate, format, gainResult.Value
	);

	var convertResult = await convertService.ConvertAndSplitAsync(
		dffFile, dffDir, cueResult.Value, primary, ct
	);
	if (convertResult.IsError)
		return convertResult.Errors;

	if (derived is not null)
	{
		var parentDir = Path.GetDirectoryName(dffDir) ?? dffDir;
		var derivedDir = Path.Combine(
			parentDir,
			$"{Path.GetFileName(dffDir)} [16-bit {derived.SampleRate / 1000.0:F1}]"
		);
		await convertService.DeriveDirectoryAsync(dffDir, derivedDir, derived.SampleRate, ct);
	}

	return Result.Success;
}
```

- [ ] **Step 4: Remove ConvertTracksAsync and ConvertBothFormatsAsync**

These are now inside DsdConvertService. Delete them entirely.

- [ ] **Step 5: Remove SanitizeFilename**

Moved to DsdConvertService. Delete from orchestrator.

- [ ] **Step 6: Add safety to CleanupAll**

Wrap individual file deletes in try-catch:

```csharp
private static void CleanupAll(List<string> dffDirs, string[] isoFiles, bool keepIso)
{
	var extensions = new[] { "*.dff", "*.cue", "*.xml" };
	foreach (var dir in dffDirs)
	{
		foreach (var ext in extensions)
		{
			foreach (var file in Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
			{
				try { File.Delete(file); }
				catch (Exception ex) { Telemetry.Warn("Cleanup failed for {File}: {Error}", file, ex.Message); }
			}
		}
	}

	if (!keepIso)
	{
		foreach (var iso in isoFiles)
		{
			try { if (File.Exists(iso)) File.Delete(iso); }
			catch (Exception ex) { Telemetry.Warn("Cleanup failed for {File}: {Error}", iso, ex.Message); }
		}
	}
}
```

- [ ] **Step 7: Remove Telemetry logging from ProcessExtractedDirectoryAsync**

The orchestrator should not log domain details — that's the service's job. Keep only high-level flow logging in RunAsync and ProcessIsoAsync. Remove per-step Telemetry.Info calls from ProcessExtractedDirectoryAsync (probe, gain, cue parsing logs).

- [ ] **Step 8: Verify** — `lsp_diagnostics`.

---

### Task 4: DsdConvertCommand + AudioSetup

**Wave:** 2 (parallel with Task 3; depends on Tasks 1, 2)

**Files:**
- Modify: `src/CLI/Audio/DsdConvertCommand.cs`
- Modify: `src/Services/Audio/AudioSetup.cs`

- [ ] **Step 1: Slim DsdConvertCommand**

Replace the sample-rate mapping switch and derive logic with `DsdConversionSettings.ForDsdRate`:

```csharp
protected override async Task<int> ExecuteAsync(
	CommandContext context,
	Settings settings,
	CancellationToken cancellationToken
)
{
	using var _ = Telemetry.ForService(ServiceName.Audio);

	var inputPath = Path.GetFullPath(settings.Input);
	var outputPath = settings.Output ?? Path.ChangeExtension(inputPath, ".flac");

	if (!File.Exists(inputPath))
	{
		await Console.Error.WriteLineAsync($"Input file not found: {inputPath}", cancellationToken);
		return 1;
	}

	var dsdProbe = await convertService.ProbeDsdAsync(inputPath, cancellationToken);
	if (dsdProbe.IsError)
	{
		await Console.Error.WriteLineAsync(dsdProbe.Errors[0].Description, cancellationToken);
		return 1;
	}

	var gain = settings.GainDb ?? 0.0;
	if (settings.GainDb is null)
	{
		var gainResult = await convertService.CalculateGainAsync(inputPath, cancellationToken);
		if (gainResult.IsError)
		{
			await Console.Error.WriteLineAsync(gainResult.Errors[0].Description, cancellationToken);
			return 1;
		}
		gain = gainResult.Value;
	}

	var (primary, derived) = DsdConversionSettings.ForDsdRate(
		dsdProbe.Value.SampleRate, settings.Format, gain
	);

	var result = await convertService.ConvertFullDffAsync(
		inputPath, outputPath, primary, cancellationToken
	);

	if (result.IsError)
	{
		await Console.Error.WriteLineAsync(result.Errors[0].Description, cancellationToken);
		return 1;
	}

	if (derived is not null)
	{
		var derivedPath = Path.ChangeExtension(outputPath, null) + $" [16-bit {derived.SampleRate}].flac";
		var deriveResult = await convertService.DeriveFlacAsync(outputPath, derivedPath, derived.SampleRate, cancellationToken);
		if (deriveResult.IsError)
			Telemetry.Warn("Derive failed: {Error}", deriveResult.Errors[0].Description);
	}

	if (settings.CopyTags)
	{
		var metaResult = metadataService.ReadDsdMetadata(inputPath);
		if (!metaResult.IsError)
		{
			var tagResult = metadataService.WriteFlacTags(outputPath, metaResult.Value);
			if (tagResult.IsError)
				Telemetry.Warn("Tagging failed: {Error}", tagResult.Errors[0].Description);
		}
	}

	await Console.Out.WriteLineAsync(
		$"Converted: {inputPath} → {outputPath} ({result.Value.FileSizeBytes / 1024 / 1024} MB)",
		cancellationToken
	);
	return 0;
}
```

- [ ] **Step 2: Update AudioSetup.cs**

DsdConvertService now takes AudioMetadataService. Update wiring:

```csharp
services.AddSingleton<DsdConvertService>();
```

This should auto-resolve since AudioMetadataService is already registered. Verify DI can resolve `DsdConvertService(SaraconService, SoxService, AudioMetadataService)`.

- [ ] **Step 3: Verify** — `lsp_diagnostics` on both files.

---

### Task 5: Build verification

**Wave:** 3 (sequential; after Tasks 3, 4)

- [ ] **Step 1: Full build** — `dotnet build`, expect 0 errors
- [ ] **Step 2: Grep for remnants** — `grep -r "FFMpegCore\|FFProbe\|FFMpegArguments\|SACD_EXTRACT_PATH\|FFMPEG_PATH\|SARACON_PATH\|SOX_PATH" --include="*.cs" src/` → zero matches
- [ ] **Step 3: Grep for env var mentions in error messages** — `grep -r "_PATH" --include="*.cs" src/Core/Errors.cs` → zero matches
- [ ] **Step 4: Verify PipelineStepResult removed** — `grep -r "PipelineStepResult" --include="*.cs" src/` → zero matches
- [ ] **Step 5: Verify UnsupportedDsdFormat removed** — `grep -r "UnsupportedDsdFormat" --include="*.cs" src/` → zero matches

---

### Task 6: AGENTS.md updates (deferred to end)

**Wave:** 3 (after Task 5)

- [ ] **Step 1: Update src/Services/Audio/AGENTS.md** — reflect facade pattern, remove env var references, update pipeline, update STRUCTURE (DsdConvertService description), remove SaraconService/SoxService from orchestrator deps
- [ ] **Step 2: Check root AGENTS.md** — remove any env var or ffmpeg references in audio context
