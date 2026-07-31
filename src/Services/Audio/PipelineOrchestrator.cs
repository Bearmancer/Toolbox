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
		using var _ = Telemetry.ForService(ServiceName.Audio);

		var validatedPath = pathValidator.ValidateInputPath(inputPath);
		if (validatedPath.IsError)
			return validatedPath.Errors;

		var isoFiles = EnumerateIsoFiles(validatedPath.Value);
		if (isoFiles.Length == 0)
			return Errors.Audio.NoIsoFound(validatedPath.Value);

		Array.Sort(isoFiles, StringComparer.OrdinalIgnoreCase);

		var totalIsoSize = isoFiles.Sum(f => new FileInfo(f).Length);
		var baseDir = Path.GetDirectoryName(isoFiles[0]) ?? validatedPath.Value;
		var spaceCheck = diskSpaceChecker.CheckSpaceForExtraction(baseDir, totalIsoSize);
		if (spaceCheck.IsError)
			return spaceCheck.Errors;

		Telemetry.Info("Found {Count} SACD ISO(s) to process", isoFiles.Length);

		var succeeded = 0;
		var failed = 0;
		var recoverableErrors = new List<string>();
		var dffDirsToClean = new List<string>();

		foreach (var iso in isoFiles)
		{
			ct.ThrowIfCancellationRequested();

			var result = await ProcessIsoAsync(iso, format, multichannel, dffDirsToClean, ct);
			if (result.IsError)
			{
				failed++;
				foreach (var error in result.Errors)
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

		var probe = await extractService.ProbeAsync(isoPath, ct);
		if (probe.IsError)
			return probe.Errors;

		var extractMch = multichannel ?? probe.Value.HasMultichannel;
		var parentDir = Path.GetDirectoryName(isoDir) ?? isoDir;
		var suffix = extractMch ? "Multichannel" : "Stereo";
		var channelDir = Path.Combine(parentDir, $"{Path.GetFileName(isoDir)} ({suffix})");

		Telemetry.Info("Extracting {Channel} from {Disc}", suffix, discName);

		var extractResult = await extractService.ExtractAsync(isoPath, channelDir, extractMch, ct);
		if (extractResult.IsError)
			return extractResult.Errors;

		foreach (var dir in extractResult.Value)
		{
			dffDirsToClean.Add(dir);
			var dirResult = await ProcessExtractedDirectoryAsync(dir, format, ct);
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
}
