using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class PipelineOrchestrator(
	SacdExtractService extractService,
	DsdConvertService convertService,
	CueParser cueParser,
	PathValidator pathValidator,
	DiskSpaceChecker diskSpaceChecker
)
{
	public async Task<ErrorOr<PipelineResult>> RunAsync(
		string inputPath,
		AudioOutputFormat format,
		bool? multichannel,
		bool keepIso,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Audio);

		ErrorOr<string> validatedPath = pathValidator.ValidateInputPath(inputPath);
		if (validatedPath.IsError)
			return validatedPath.Errors;

		var isoFiles = EnumerateIsoFiles(validatedPath.Value);
		if (isoFiles.Length == 0)
			return Errors.Audio.NoIsoFound(validatedPath.Value);

		Array.Sort(isoFiles, StringComparer.OrdinalIgnoreCase);

		var totalIsoSize = isoFiles.Sum(f => new FileInfo(f).Length);
		var baseDir = Path.GetDirectoryName(isoFiles[0]) ?? validatedPath.Value;
		ErrorOr<Success> spaceCheck = diskSpaceChecker.CheckSpaceForExtraction(
			baseDir,
			totalIsoSize
		);
		if (spaceCheck.IsError)
			return spaceCheck.Errors;

		Telemetry.Info("Found {Count} SACD ISO(s) to process", isoFiles.Length);

		var succeeded = 0;
		var failed = 0;
		List<string> recoverableErrors = [];
		List<string> dffDirsToClean = [];

		foreach (var iso in isoFiles)
		{
			ct.ThrowIfCancellationRequested();

			ErrorOr<Success> result = await ProcessIsoAsync(
				iso,
				format,
				multichannel,
				dffDirsToClean,
				ct
			);
			if (result.IsError)
			{
				failed++;
				foreach (Error error in result.Errors)
				{
					Telemetry.Error("ISO failed: {Error}", error.Description);
					recoverableErrors.Add(error.Description);
				}
			}
			else
			{
				succeeded++;
			}
		}

		CleanupAll(dffDirsToClean, isoFiles, keepIso);

		return new PipelineResult(succeeded, failed, recoverableErrors);
	}

	private static string[] EnumerateIsoFiles(string validatedPath)
	{
		var isDirectory = File.GetAttributes(validatedPath).HasFlag(FileAttributes.Directory);
		return isDirectory
			? Directory.GetFiles(validatedPath, "*.iso", SearchOption.AllDirectories)
			: [validatedPath];
	}

	private enum ChannelDirState
	{
		NotPresent,
		Clean,
		Contaminated,
	}

	/// <summary>
	/// Inspects an existing channelDir to determine if it can be reused.
	/// Clean = has DFF files, none with Windows collision suffixes like "(1)", "(2)".
	/// Contaminated = has collision-suffixed DFFs from a partial prior run.
	/// NotPresent = dir doesn't exist yet.
	/// </summary>
	private static ChannelDirState InspectChannelDir(string channelDir, string discName)
	{
		if (!Directory.Exists(channelDir))
			return ChannelDirState.NotPresent;

		var dffFiles = Directory.GetFiles(channelDir, "*.dff", SearchOption.AllDirectories);
		if (dffFiles.Length == 0)
			return ChannelDirState.NotPresent;

		// Collision suffix pattern: filename contains " (N)" before the extension
		var hasCollision = dffFiles.Any(f =>
		{
			var name = Path.GetFileNameWithoutExtension(f);
			return System.Text.RegularExpressions.Regex.IsMatch(name, @"\s\(\d+\)$");
		});

		if (hasCollision)
		{
			Telemetry.Warn(
				"Pipeline.InspectChannelDir disc={Disc} collisionFiles={Files}",
				discName,
				string.Join(", ", dffFiles.Select(Path.GetFileName))
			);
			return ChannelDirState.Contaminated;
		}

		return ChannelDirState.Clean;
	}

	private async Task<ErrorOr<Success>> ProcessIsoAsync(
		string isoPath,
		AudioOutputFormat format,
		bool? multichannel,
		List<string> dffDirsToClean,
		CancellationToken ct
	)
	{
		var isoDir = Path.GetDirectoryName(isoPath) ?? isoPath;
		var discName = Path.GetFileNameWithoutExtension(isoPath);
		Telemetry.Info("Probing {Disc}", discName);

		ErrorOr<SacdProbeResult> probe = await extractService.ProbeAsync(isoPath, ct);
		if (probe.IsError)
			return probe.Errors;

		var extractMch = multichannel ?? probe.Value.HasMultichannel;
		var parentDir = Path.GetDirectoryName(isoDir) ?? isoDir;
		var suffix = extractMch ? "Multichannel" : "Stereo";
		var channelDir = Path.Combine(parentDir, $"{Path.GetFileName(isoDir)} ({suffix})");

		ChannelDirState channelDirState = InspectChannelDir(channelDir, discName);
		if (channelDirState == ChannelDirState.Contaminated)
		{
			Telemetry.Warn(
				"Pipeline.ContaminatedDir dir={Dir} — collision-suffixed DFF files detected from a previous partial run; purging and re-extracting",
				channelDir
			);
			Directory.Delete(channelDir, recursive: true);
		}

		ErrorOr<List<string>> extractResult;
		if (channelDirState == ChannelDirState.Clean)
		{
			Telemetry.Info("Skipping extraction for {Disc} — clean DFFs already present", discName);
			List<string> existingDirs = [.. Directory.GetDirectories(channelDir)];
			if (existingDirs.Count == 0)
			{
				var dffFiles = Directory.GetFiles(channelDir, "*.dff", SearchOption.AllDirectories);
				var dir = dffFiles.Length > 0 ? Path.GetDirectoryName(dffFiles[0]) : null;
				existingDirs = dir is not null ? [dir] : [];
			}
			extractResult = existingDirs;
		}
		else
		{
			Telemetry.Info("Extracting {Channel} from {Disc}", suffix, discName);
			extractResult = await extractService.ExtractAsync(isoPath, channelDir, extractMch, ct);
		}
		if (extractResult.IsError)
			return extractResult.Errors;

		foreach (var dir in extractResult.Value)
		{
			dffDirsToClean.Add(dir);
			ErrorOr<Success> dirResult = await ProcessExtractedDirectoryAsync(dir, format, ct);
			if (dirResult.IsError)
				return dirResult.Errors;
		}

		return Result.Success;
	}

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

		// Sort by filename length ascending so the original (no collision suffix) is always first.
		// Collision copies from Windows auto-rename are longer: "Disc 10 (1).dff" > "Disc 10.dff"
		Array.Sort(
			dffFiles,
			(a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length)
		);
		Array.Sort(
			cueFiles,
			(a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length)
		);

		var dffFile = dffFiles[0];
		var cueFile = cueFiles[0];

		if (dffFiles.Length > 1)
			Telemetry.Warn(
				"Pipeline.MultipleDff dir={Dir} selected={Dff} ignored={Rest}",
				Path.GetFileName(dffDir),
				Path.GetFileName(dffFile),
				string.Join(", ", dffFiles.Skip(1).Select(Path.GetFileName))
			);
		if (cueFiles.Length > 1)
			Telemetry.Warn(
				"Pipeline.MultipleCue dir={Dir} selected={Cue} ignored={Rest}",
				Path.GetFileName(dffDir),
				Path.GetFileName(cueFile),
				string.Join(", ", cueFiles.Skip(1).Select(Path.GetFileName))
			);

		Telemetry.Debug(
			"Pipeline.ProcessDir dir={Dir} dff={Dff} cue={Cue}",
			Path.GetFileName(dffDir),
			Path.GetFileName(dffFile),
			Path.GetFileName(cueFile)
		);

		ErrorOr<DsdProbeResult> dsdProbe = await convertService.ProbeDsdAsync(dffFile, ct);
		if (dsdProbe.IsError)
			return dsdProbe.Errors;

		ErrorOr<double> gainResult = await convertService.CalculateGainAsync(dffFile, ct);
		if (gainResult.IsError)
			return gainResult.Errors;

		ErrorOr<CueSheet> cueResult = cueParser.Parse(cueFile);
		if (cueResult.IsError)
			return cueResult.Errors;

		(DsdConversionSettings primary, DsdConversionSettings? derived) =
			DsdConversionSettings.ForDsdRate(dsdProbe.Value.SampleRate, format, gainResult.Value);

		Telemetry.Debug(
			"Pipeline.ConversionSettings rate={Rate} primaryFormat={PrimaryFormat} primaryGain={PrimaryGain}dB derived={Derived}",
			dsdProbe.Value.SampleRate,
			primary.SampleRate,
			primary.GainDb,
			derived is not null ? $"{derived.SampleRate}" : "none"
		);

		ErrorOr<List<string>> convertResult = await convertService.ConvertAndSplitAsync(
			dffFile,
			dffDir,
			cueResult.Value,
			primary,
			ct
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
			Telemetry.Debug(
				"Pipeline.DeriveStart dir={Dir} rate={Rate}",
				Path.GetFileName(derivedDir),
				derived.SampleRate
			);
			await convertService.DeriveDirectoryAsync(dffDir, derivedDir, derived.SampleRate, ct);
		}

		return Result.Success;
	}

	private static void CleanupAll(List<string> dffDirs, string[] isoFiles, bool keepIso)
	{
		var extensions = new[] { "*.dff", "*.cue", "*.xml" };
		foreach (var dir in dffDirs)
		{
			foreach (var ext in extensions)
			{
				foreach (var file in Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
				{
					try
					{
						File.Delete(file);
					}
					catch (Exception ex)
					{
						Telemetry.Warn("Cleanup failed for {File}: {Error}", file, ex.Message);
					}
				}
			}
		}

		if (!keepIso)
		{
			foreach (var iso in isoFiles)
			{
				try
				{
					if (File.Exists(iso))
						File.Delete(iso);
				}
				catch (Exception ex)
				{
					Telemetry.Warn("Cleanup failed for {File}: {Error}", iso, ex.Message);
				}
			}
		}
	}
}
