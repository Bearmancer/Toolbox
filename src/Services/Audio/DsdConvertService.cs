using System.Buffers.Binary;
using System.Text;
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
		if (!DffMetadataStripper.HasId3Chunk(dffFilePath))
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
			await using FileStream stream = File.OpenRead(dffFilePath);
			using BinaryReader reader = new(stream);

			var magic = Encoding.ASCII.GetString(ReadExactly(reader, 4));
			if (magic != "FRM8")
				return Errors.Audio.ProbeFailed(dffFilePath, $"Not a DSDIFF file (magic: {magic})");

			reader.ReadBytes(8);
			var formType = Encoding.ASCII.GetString(ReadExactly(reader, 4));
			if (formType != "DSD ")
				return Errors.Audio.ProbeFailed(dffFilePath, $"Unexpected form type: {formType}");

			var sampleRate = 0;
			var channels = 0;

			while (stream.Position < stream.Length - 12)
			{
				var chunkId = Encoding.ASCII.GetString(ReadExactly(reader, 4));
				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));

				var chunkDataEnd = checked(stream.Position + (long)chunkSize);
				if (chunkDataEnd > stream.Length)
					return Errors.Audio.ProbeFailed(
						dffFilePath,
						$"Chunk {chunkId} size {chunkSize} exceeds stream length"
					);

				if (chunkId == "PROP")
				{
					if (chunkSize < 4)
						return Errors.Audio.ProbeFailed(
							dffFilePath,
							"PROP chunk is missing property type"
						);

					var propType = Encoding.ASCII.GetString(ReadExactly(reader, 4));
					var propEnd = stream.Position + (long)chunkSize - 4;

					if (propType == "SND ")
					{
						while (stream.Position < propEnd - 12)
						{
							var subId = Encoding.ASCII.GetString(ReadExactly(reader, 4));
							var subSize = BinaryPrimitives.ReadUInt64BigEndian(
								reader.ReadBytes(8)
							);

							var subDataEnd = checked(stream.Position + (long)subSize);
							if (subDataEnd > propEnd)
								return Errors.Audio.ProbeFailed(
									dffFilePath,
									$"Subchunk {subId} size {subSize} exceeds PROP boundary"
								);

							if (subId == "FS  ")
							{
								sampleRate =
									(int)BinaryPrimitives.ReadUInt32BigEndian(
										reader.ReadBytes(4)
									);
								if (subSize > 4)
									stream.Seek((long)subSize - 4, SeekOrigin.Current);
							}
							else if (subId == "CHNL")
							{
								channels = BinaryPrimitives.ReadUInt16BigEndian(
									reader.ReadBytes(2)
								);
								if (subSize > 2)
									stream.Seek((long)subSize - 2, SeekOrigin.Current);
							}
							else
							{
								stream.Seek((long)subSize, SeekOrigin.Current);
							}

							if (subSize % 2 != 0 && stream.Position < stream.Length)
								reader.ReadByte();
						}
					}
					else
					{
						stream.Seek((long)chunkSize - 4, SeekOrigin.Current);
					}
				}
				else
				{
					stream.Seek((long)chunkSize, SeekOrigin.Current);
				}

				if (chunkSize % 2 != 0 && stream.Position < stream.Length)
					reader.ReadByte();

				if (sampleRate > 0 && channels > 0)
					break;
			}

			if (sampleRate == 0 || channels == 0)
				return Errors.Audio.ProbeFailed(
					dffFilePath,
					"Could not parse FS or CHNL chunks from DFF header"
				);

			Telemetry.Debug(
				"DsdConvert.ProbeComplete file={File} rate={Rate} channels={Channels}",
				Path.GetFileName(dffFilePath),
				sampleRate,
				channels
			);

			return new DsdProbeResult(dffFilePath, "dsd", sampleRate, channels);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error("DsdConvert.ProbeFailed file={File}: {Error}", LogPaths.Format(dffFilePath), ex.Message);
			return Errors.Audio.ProbeFailed(dffFilePath, ex.Message);
		}
	}

	private static byte[] ReadExactly(BinaryReader reader, int count)
	{
		var buffer = new byte[count];
		var totalRead = 0;
		while (totalRead < count)
		{
			var read = reader.Read(buffer, totalRead, count - totalRead);
			if (read == 0)
				throw new EndOfStreamException(
					$"Expected {count} bytes, got {totalRead}"
				);
			totalRead += read;
		}

		return buffer;
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

		var tempDir = Path.Combine(Path.GetTempPath(), "toolbox-audio", $"gain_probe_{Guid.NewGuid():N}");

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
				Telemetry.Warn("DsdConvert.TempCleanupFailed dir={Dir} error={Error}", LogPaths.Format(tempDir), ex.Message);
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
					continue;

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
				List<int> missing = [.. cue.Tracks
					.Where(t => !outputFiles.Any(f =>
						Path.GetFileName(f).StartsWith(
							$"{t.TrackNumber:D2}. ",
							StringComparison.Ordinal
						)))
					.Select(t => t.TrackNumber)];
				return Errors.Audio.ConversionFailed(
					dffFile,
					$"Incomplete conversion: missing tracks {string.Join(", ", missing)}"
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
					Telemetry.Warn("DsdConvert.MasterCleanupFailed file={File} error={Error}", LogPaths.Format(masterPcm), ex.Message);
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
		var tempDir = Path.Combine(Path.GetTempPath(), "toolbox-audio", $"saracon_{Guid.NewGuid():N}");

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

			File.Move(tempFlac, outputFlac, overwrite: true);

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
				Telemetry.Warn("DsdConvert.TempCleanupFailed dir={Dir} error={Error}", LogPaths.Format(tempDir), ex.Message);
			}
		}
	}

	public async Task<ErrorOr<ConversionResult>> DeriveFlacAsync(
		string sourceFlac,
		string outputFlac,
		int targetSampleRate,
		CancellationToken ct = default
	)
	{
		ErrorOr<string> deriveResult = await sox.DeriveFlacAsync(
			sourceFlac,
			outputFlac,
			targetSampleRate,
			ct
		);
		if (deriveResult.IsError)
			return deriveResult.Errors;

		ErrorOr<TimeSpan> durationResult = await sox.GetDurationAsync(outputFlac, ct);
		if (durationResult.IsError)
			return durationResult.Errors;

		FileInfo fileInfo = new(outputFlac);
		fileInfo.Refresh();

		return new ConversionResult(outputFlac, durationResult.Value, fileInfo.Length);
	}

	private static string SanitizeFilename(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return string.Join("-", name.Split(invalid)).Trim();
	}
}
