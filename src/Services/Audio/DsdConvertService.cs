using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class DsdConvertService(
	SaraconService saracon,
	SoxService sox,
	AudioMetadataService metadata
)
{
	private const double TargetHeadroomDb = -0.5;

	public async Task<ErrorOr<string>> PrepareDffAsync(
		string dffFilePath,
		string outputDir,
		CancellationToken ct = default
	)
	{
		ErrorOr<bool> hasId3Result = DffMetadataStripper.HasId3Chunk(dffFilePath);
		if (hasId3Result.IsError)
			return hasId3Result.Errors;
		if (!hasId3Result.Value)
			return dffFilePath;

		Telemetry.Warn(
			"Saracon.Id3Detected input={Input} — stripping before conversion",
			Path.GetFileName(dffFilePath)
		);
		return await DffMetadataStripper.StripId3TagsAsync(dffFilePath, outputDir, ct);
	}

	public async Task<ErrorOr<DsdProbeResult>> ProbeDsdAsync(
		string dffFilePath,
		CancellationToken ct = default
	)
	{
		try
		{
			Telemetry.Debug(
				"DsdConvert.ProbeStart file={File} size={Size}MB",
				Path.GetFileName(dffFilePath),
				new FileInfo(dffFilePath).Length / 1_048_576.0
			);

			ct.ThrowIfCancellationRequested();

			ErrorOr<DffHeader> hdr = DffHeaderReader.Read(dffFilePath);
			if (hdr.IsError)
				return hdr.Errors;

			Telemetry.Debug(
				"DsdConvert.ProbeComplete file={File} rate={Rate} channels={Channels}",
				Path.GetFileName(dffFilePath),
				hdr.Value.SampleRate,
				hdr.Value.Channels
			);

			return new DsdProbeResult(dffFilePath, "dsd", hdr.Value.SampleRate, hdr.Value.Channels);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error(
				"DsdConvert.ProbeFailed file={File}: {Error}",
				Path.GetFileName(dffFilePath),
				ex.Message
			);
			return Errors.Audio.ProbeFailed(dffFilePath, ex.Message);
		}
	}

	public async Task<ErrorOr<double>> CalculateGainAsync(
		string dffFilePath,
		DsdProbeResult probe,
		DsdConversionSettings settings,
		CancellationToken ct = default
	)
	{
		Telemetry.Debug(
			"DsdConvert.GainCalcStart file={File} rate={Rate} bitDepth={BitDepth}",
			Path.GetFileName(dffFilePath),
			settings.SampleRate,
			settings.BitDepth
		);

		var tempDir = Path.Combine(
			Path.GetTempPath(),
			"toolbox-audio",
			$"gain_probe_{Guid.NewGuid():N}"
		);

		try
		{
			ErrorOr<string> convertResult = await saracon.ConvertDsdToPcmAsync(
				dffFilePath,
				tempDir,
				settings.SampleRate,
				settings.BitDepth,
				settings.GainDb,
				probe.SampleRate,
				probe.Channels,
				null,
				ct
			);
			if (convertResult.IsError)
				return convertResult.Errors;

			ErrorOr<double> peakResult = await sox.GetPeakLevelAsync(convertResult.Value, ct);
			if (peakResult.IsError)
				return peakResult.Errors;

			var gain = TargetHeadroomDb - peakResult.Value;
			var finalGain = Math.Min(gain, 6.0);

			Telemetry.Debug(
				"DsdConvert.GainCalcComplete file={File} rate={Rate} bitDepth={BitDepth} peak={Peak}dB gain={Gain}dB",
				Path.GetFileName(dffFilePath),
				settings.SampleRate,
				settings.BitDepth,
				peakResult.Value,
				finalGain
			);

			return finalGain;
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
					Directory.Delete(tempDir, recursive: true);
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"DsdConvert.TempCleanupFailed dir={Dir} error={Error}",
					Path.GetFileName(tempDir),
					ex.Message
				);
			}
		}
	}

	public async Task<ErrorOr<List<string>>> ConvertAndSplitAsync(
		string dffFile,
		string outputDir,
		CueSheet cue,
		DsdConversionSettings settings,
		DsdProbeResult probe,
		CancellationToken ct = default
	)
	{
		string? masterPcm = null;
		try
		{
			ErrorOr<string> masterResult = await saracon.ConvertDsdToPcmAsync(
				dffFile,
				outputDir,
				settings.SampleRate,
				settings.BitDepth,
				settings.GainDb,
				probe.SampleRate,
				probe.Channels,
				null,
				ct
			);
			if (masterResult.IsError)
				return masterResult.Errors;

			masterPcm = masterResult.Value;
			List<string> outputFiles = [];
			Dictionary<int, string> splitErrors = [];

			foreach (CueTrack track in cue.Tracks)
			{
				var trackNum = track.TrackNumber.ToString("D2");
				var safeTitle = SanitizeFilename(track.Title);
				var outputFlac = Path.Combine(outputDir, $"{trackNum}. {safeTitle}.flac");

				ErrorOr<string> splitResult = await sox.SplitTrackAsync(
					masterPcm,
					outputFlac,
					track.StartTime,
					track.Duration,
					ct
				);

				if (splitResult.IsError)
				{
					var reason = splitResult.Errors[0].Description;
					splitErrors[track.TrackNumber] = reason;
					Telemetry.Warn(
						"DsdConvert.SplitFailed track={Track} output={Output} error={Error}",
						track.TrackNumber,
						outputFlac,
						reason
					);
					continue;
				}

				outputFiles.Add(outputFlac);

				ErrorOr<Success> tagResult = metadata.CopyMetadataFromCue(outputFlac, cue, track);
				if (tagResult.IsError)
					Telemetry.Warn(
						"Tagging failed for {File}: {Error}",
						outputFlac,
						tagResult.Errors[0].Description
					);
			}

			if (outputFiles.Count < cue.Tracks.Count)
			{
				List<int> missing = [.. splitErrors.Keys.Order()];
				var reasons = string.Join(
					"; ",
					splitErrors.OrderBy(kv => kv.Key).Select(kv => $"track {kv.Key}: {kv.Value}")
				);
				return Errors.Audio.ConversionFailed(
					dffFile,
					$"Incomplete conversion: missing tracks {string.Join(", ", missing)}. {reasons}"
				);
			}

			return outputFiles;
		}
		finally
		{
			if (masterPcm is not null)
			{
				try
				{
					if (File.Exists(masterPcm))
						File.Delete(masterPcm);
				}
				catch (Exception ex)
				{
					Telemetry.Warn(
						"DsdConvert.MasterCleanupFailed file={File} error={Error}",
						Path.GetFileName(masterPcm),
						ex.Message
					);
				}
			}
		}
	}

	public async Task<ErrorOr<ConversionResult>> ConvertFullDffAsync(
		string inputDff,
		string outputFlac,
		DsdConversionSettings settings,
		DsdProbeResult probe,
		CancellationToken ct = default
	)
	{
		var tempDir = Path.Combine(
			Path.GetTempPath(),
			"toolbox-audio",
			$"saracon_{Guid.NewGuid():N}"
		);

		try
		{
			ErrorOr<string> convertResult = await saracon.ConvertDsdToFlacAsync(
				inputDff,
				tempDir,
				settings.SampleRate,
				settings.BitDepth,
				settings.GainDb,
				probe.SampleRate,
				probe.Channels,
				null,
				ct
			);
			if (convertResult.IsError)
				return convertResult.Errors;

			var tempFlac = convertResult.Value;
			var outputDir = Path.GetDirectoryName(outputFlac);
			if (!string.IsNullOrEmpty(outputDir))
				Directory.CreateDirectory(outputDir);

			try
			{
				File.Move(tempFlac, outputFlac, overwrite: true);
			}
			catch (Exception ex)
			{
				throw new TranscodePipelineException(
					$"could not move our own converted file into place: {tempFlac} -> {outputFlac}: {ex.Message}"
				);
			}

			ErrorOr<TimeSpan> durationResult = await sox.GetDurationAsync(outputFlac, ct);
			if (durationResult.IsError)
				return durationResult.Errors;

			FileInfo fileInfo = new(outputFlac);
			fileInfo.Refresh();

			return new ConversionResult(outputFlac, durationResult.Value, fileInfo.Length);
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
					Directory.Delete(tempDir, recursive: true);
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"DsdConvert.TempCleanupFailed dir={Dir} error={Error}",
					Path.GetFileName(tempDir),
					ex.Message
				);
			}
		}
	}

	private static string SanitizeFilename(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return string.Join("-", name.Split(invalid)).Trim();
	}
}
