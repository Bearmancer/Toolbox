using System.Buffers.Binary;
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
	private const int ProbeSampleRate = 88200;
	private const int ProbeBitDepth = 24;

	public async Task<ErrorOr<DsdProbeResult>> ProbeDsdAsync(
		string dffFilePath,
		CancellationToken ct = default
	)
	{
		try
		{
			ct.ThrowIfCancellationRequested();
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

	private static string SanitizeFilename(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return string.Join("-", name.Split(invalid)).Trim();
	}
}
