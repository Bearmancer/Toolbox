using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class PipelineOrchestrator(
	SacdExtractService extractService,
	DsdConvertService convertService,
	DiscOutputInspector inspector,
	CueParser cueParser,
	PathValidator pathValidator,
	DiskSpaceChecker diskSpaceChecker
)
{
	private static readonly Regex NaturalSortPad = new(
		@"\d+",
		RegexOptions.Compiled
	);

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

		Array.Sort(
			isoFiles,
			(a, b) =>
				string.Compare(
					NaturalSortPad.Replace(Path.GetFileName(a), m => m.Value.PadLeft(20, '0')),
					NaturalSortPad.Replace(Path.GetFileName(b), m => m.Value.PadLeft(20, '0')),
					StringComparison.OrdinalIgnoreCase
				)
		);

		var totalIsoSize = isoFiles.Sum(f => new FileInfo(f).Length);
		var baseDir = Path.GetDirectoryName(isoFiles[0]) ?? validatedPath.Value;
		ErrorOr<Success> spaceCheck = diskSpaceChecker.CheckSpaceForExtraction(
			baseDir,
			totalIsoSize
		);
		if (spaceCheck.IsError)
			return spaceCheck.Errors;

		var isoRoot = Directory.Exists(validatedPath.Value)
			? validatedPath.Value
			: Path.GetDirectoryName(Path.GetDirectoryName(validatedPath.Value))
				?? Path.GetDirectoryName(validatedPath.Value)
				?? validatedPath.Value;
		var suffix0 = multichannel == true ? "Multichannel" : "Stereo";
		var outputRoot = Path.Combine(
			Path.GetDirectoryName(isoRoot) ?? isoRoot,
			$"{Path.GetFileName(isoRoot)} ({suffix0})"
		);
		try
		{
			LogPaths.Setup(isoRoot, outputRoot);
			ReprocessGuard guard = await ReprocessGuard.LoadAsync();

			Telemetry.Info("SACD run: ISO root={IsoRoot}", isoRoot);
			Telemetry.Info("SACD run: output root={OutputRoot}", outputRoot);
			Telemetry.Info("Found {Count} SACD ISO(s) to process", isoFiles.Length);

			var succeeded = 0;
			var failed = 0;
			List<string> recoverableErrors = [];
			List<string> guardFailedDiscs = [];
			List<ProcessedDisc> succeededDiscs = [];

			foreach (var iso in isoFiles)
			{
				ct.ThrowIfCancellationRequested();

				if (guard.Get(iso)?.Verdict == DiscState.Failed)
				{
					failed++;
					guardFailedDiscs.Add(iso);
					Telemetry.Warn(
						"Guard: {Disc} is Failed — skipping",
						LogPaths.Format(iso)
					);
					continue;
				}

				ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
					iso,
					format,
					multichannel,
					guard,
					ct
				);
				if (result.IsError)
				{
					failed++;
					if (result.Errors.Any(error => error.Code == "Audio.GuardBlocked"))
						guardFailedDiscs.Add(iso);

					foreach (Error error in result.Errors)
					{
						Telemetry.Error(
							"ISO failed: iso={Iso} error={Error}",
							LogPaths.Format(iso),
							error.Description
						);
						recoverableErrors.Add(error.Description);
					}
				}
				else
				{
					succeededDiscs.Add(result.Value);
					succeeded++;
				}
			}

			CleanupSuccesses(succeededDiscs, keepIso);
			return new PipelineResult(succeeded, failed, recoverableErrors, guardFailedDiscs);
		}
		finally
		{
			LogPaths.Reset();
		}
	}

	private static string[] EnumerateIsoFiles(string validatedPath)
	{
		var isDirectory = File.GetAttributes(validatedPath).HasFlag(FileAttributes.Directory);
		return isDirectory
			? Directory.GetFiles(validatedPath, "*.iso", SearchOption.AllDirectories)
			: [validatedPath];
	}

	private async Task<ErrorOr<ProcessedDisc>> ProcessIsoAsync(
		string isoPath,
		AudioOutputFormat format,
		bool? multichannel,
		ReprocessGuard guard,
		CancellationToken ct
	)
	{
		var isoDir = Path.GetDirectoryName(isoPath) ?? isoPath;
		var discName = Path.GetFileNameWithoutExtension(isoPath);
		if (discName is "." or "..")
			return Error.Validation("Audio.InvalidDiscName", $"Invalid ISO filename: {discName}");

		ReprocessGuard.GuardEntry? existing = guard.Get(isoPath);
		if (existing?.Verdict == DiscState.Failed)
			return Error.Failure(
				"Audio.GuardBlocked",
				$"{discName} is Failed (stuck {existing.ConsecutiveCount}x) — no process started"
			);

		if (existing is { Verdict: var v, ConsecutiveCount: var c }
			&& c + 1 >= ReprocessGuard.MaxConsecutiveCount
			&& v != DiscState.Complete)
		{
			ct.ThrowIfCancellationRequested();
			await guard.RecordAsync(isoPath, v);
			Telemetry.Warn(
				"Guard: {Disc} reached {Count}x {Verdict} — transitioning Failed",
				discName,
				c + 1,
				v
			);
			return Error.Failure(
				"Audio.GuardBlocked",
				$"{discName} reached {c + 1}x {v} — transitioning Failed, no process started"
			);
		}

		Telemetry.Info("Probing {Disc}", discName);

		ErrorOr<SacdProbeResult> probe = await extractService.ProbeAsync(isoPath, ct);
		if (probe.IsError)
			return probe.Errors;

		var extractMch = multichannel ?? probe.Value.HasMultichannel;
		var sourceRoot = Path.GetDirectoryName(isoDir) ?? isoDir;
		var outputParent = Path.GetDirectoryName(sourceRoot) ?? sourceRoot;
		var suffix = extractMch ? "Multichannel" : "Stereo";
		var channelDir = Path.Combine(
			outputParent,
			$"{Path.GetFileName(sourceRoot)} ({suffix})",
			Path.GetFileName(isoDir)
		);

		DiscOutputInspector.DiscAssessment assessment = await inspector.EvaluateDiscAsync(
			channelDir,
			discName,
			ct
		);

		if (assessment.State == DiscState.Complete)
		{
			ct.ThrowIfCancellationRequested();
			await guard.RecordAsync(isoPath, DiscState.Complete);
			return new ProcessedDisc(isoPath, [assessment.DffDir]);
		}

		if (assessment.State == DiscState.NeedsPrimaryConversion)
		{
			ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
				assessment.DffDir,
				new FileInfo(isoPath).Length
			);
			if (conversionSpaceCheck.IsError)
			{
				ct.ThrowIfCancellationRequested();
				await guard.RecordAsync(isoPath, assessment.State);
				return conversionSpaceCheck.Errors;
			}

			DeletePartialFlacs(assessment.DffDir);

			Telemetry.Info(
				"Disc {Disc}: case B — DFF valid, {Flacs}/{Tracks} FLACs → converting",
				discName,
				assessment.PrimaryFlacCount,
				assessment.CueTrackCount
			);
			ErrorOr<Success> convertResult = await ConvertDiscAsync(
				assessment.DffDir,
				format,
				ct
			);
			if (convertResult.IsError)
			{
				ct.ThrowIfCancellationRequested();
				await guard.RecordAsync(isoPath, assessment.State);
				return convertResult.Errors;
			}

			ct.ThrowIfCancellationRequested();
			await guard.RecordAsync(isoPath, assessment.State);
			return new ProcessedDisc(isoPath, [assessment.DffDir]);
		}

		if (assessment.State == DiscState.InvalidArtifacts)
			DeleteStaleDff(assessment.DffDir);

		if (assessment.State == DiscState.NeedsExtraction)
			DeletePartialFlacs(assessment.DffDir);

		Telemetry.Info(
			"Disc {Disc}: case A — extracting from ISO",
			discName
		);

		ErrorOr<List<string>> extractResult = await extractService.ExtractAsync(
			isoPath,
			channelDir,
			extractMch,
			ct
		);
		if (extractResult.IsError)
		{
			ct.ThrowIfCancellationRequested();
			await guard.RecordAsync(isoPath, assessment.State);
			return extractResult.Errors;
		}

		if (extractResult.Value.Count > 0)
		{
			ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
				channelDir,
				new FileInfo(isoPath).Length
			);
			if (conversionSpaceCheck.IsError)
			{
				ct.ThrowIfCancellationRequested();
				await guard.RecordAsync(isoPath, assessment.State);
				return conversionSpaceCheck.Errors;
			}
		}

		foreach (var dffDir in extractResult.Value)
		{
			ErrorOr<Success> dirResult = await ConvertDiscAsync(dffDir, format, ct);
			if (dirResult.IsError)
			{
				ct.ThrowIfCancellationRequested();
				await guard.RecordAsync(isoPath, assessment.State);
				return dirResult.Errors;
			}
		}

		ct.ThrowIfCancellationRequested();
		await guard.RecordAsync(isoPath, assessment.State);
		return new ProcessedDisc(isoPath, extractResult.Value);
	}

	private static void DeleteStaleDff(string dffDir)
	{
		if (!Directory.Exists(dffDir))
			return;

		foreach (var dff in Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories))
		{
			try
			{
				Telemetry.Info("Pipeline.StaleDffDeleted file={File}", LogPaths.Format(dff));
				File.Delete(dff);
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"Pipeline.DffDeleteFailed file={File} error={Error}",
					LogPaths.Format(dff),
					ex.Message
				);
			}
		}
	}

	private static void DeletePartialFlacs(string dffDir) => DeleteFlacsInDir(dffDir);

	private static void DeleteFlacsInDir(string dir)
	{
		foreach (var flac in Directory.GetFiles(dir, "*.flac"))
		{
			try
			{
				Telemetry.Info("Pipeline.ResplitFlacDeleted file={File}", LogPaths.Format(flac));
				File.Delete(flac);
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"Pipeline.FlacDeleteFailed file={File} error={Error}",
					LogPaths.Format(flac),
					ex.Message
				);
			}
		}
	}

	private async Task<ErrorOr<Success>> ConvertDiscAsync(
		string dffDir,
		AudioOutputFormat format,
		CancellationToken ct
	)
	{
		var cueFiles = Directory.Exists(dffDir)
			? Directory.GetFiles(dffDir, "*.cue")
			: [];
		if (cueFiles.Length == 0)
			return Errors.Audio.NoCueFound(dffDir);

		var cueFile = cueFiles[0];
		ErrorOr<CueSheet> cueResult = cueParser.Parse(cueFile);
		if (cueResult.IsError)
			return cueResult.Errors;

		var dffFiles = Directory.Exists(dffDir)
			? Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories)
			: [];
		if (dffFiles.Length == 0)
			return Errors.Audio.NoDffFound(dffDir);

		Array.Sort(
			dffFiles,
			(a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length)
		);
		var dffFile = dffFiles[0];

		ErrorOr<DsdProbeResult> dsdProbe = await convertService.ProbeDsdAsync(dffFile, ct);
		if (dsdProbe.IsError)
			return dsdProbe.Errors;

		ErrorOr<string> preparedDff = await convertService.PrepareDffAsync(dffFile, dffDir, ct);
		if (preparedDff.IsError)
			return preparedDff.Errors;

		DsdConversionSettings gainSettings = DsdConversionSettings.ForDsdRate(
			dsdProbe.Value.SampleRate,
			format,
			0.0
		).Primary;

		ErrorOr<double> gainResult = await convertService.CalculateGainAsync(
			preparedDff.Value,
			dsdProbe.Value,
			gainSettings,
			ct
		);
		if (gainResult.IsError)
			return gainResult.Errors;

		DsdConversionSettings primary = DsdConversionSettings.ForDsdRate(
			dsdProbe.Value.SampleRate,
			format,
			gainResult.Value
		).Primary;

		ErrorOr<List<string>> convertResult = await convertService.ConvertAndSplitAsync(
			preparedDff.Value,
			dffDir,
			cueResult.Value,
			primary,
			dsdProbe.Value,
			ct
		);
		if (convertResult.IsError)
			return convertResult.Errors;

		return Result.Success;
	}

	private static void CleanupSuccesses(List<ProcessedDisc> succeededDiscs, bool keepIso)
	{
		foreach (ProcessedDisc disc in succeededDiscs)
		{
			var outputsValidated = true;
			foreach (var outputDir in disc.OutputDirectories)
			{
				if (!Directory.Exists(outputDir))
				{
					outputsValidated = false;
					Telemetry.Warn("Pipeline.OutputValidationFailed dir={Dir}", LogPaths.Format(outputDir));
					continue;
				}

				foreach (var file in Directory.GetFiles(outputDir, "*.dff", SearchOption.AllDirectories)
					.Concat(Directory.GetFiles(outputDir, "*.xml", SearchOption.AllDirectories)))
				{
					try
					{
						File.Delete(file);
					}
					catch (Exception ex)
					{
						Telemetry.Warn(
							"Pipeline.CleanupFailed file={File}: {Error}",
							LogPaths.Format(file),
							ex.Message
						);
					}
				}
			}

			if (!keepIso && outputsValidated)
			{
				try
				{
					if (File.Exists(disc.IsoPath))
						File.Delete(disc.IsoPath);
				}
				catch (Exception ex)
				{
					Telemetry.Warn(
						"Pipeline.CleanupFailed file={File}: {Error}",
						LogPaths.Format(disc.IsoPath),
						ex.Message
					);
				}
			}
		}
	}

	private sealed record ProcessedDisc(string IsoPath, IReadOnlyList<string> OutputDirectories);
}
