using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Audio;

public sealed class FlacTranscodeService(ProcessRunner processRunner, SoxService sox)
{
	public async Task<ErrorOr<FlacTranscodeResult>> TranscodeDirectoryAsync(
		string directory,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Audio);
		if (Directory.Exists(directory) is false)
			return Errors.Audio.InvalidInputPath(directory);

		var flacFiles = Directory.GetFiles(directory, "*.flac", SearchOption.TopDirectoryOnly);
		var converted = 0;
		var skippedNotFlac = 0;
		var skippedAlreadyMp3 = 0;
		var skippedAlready16Bit = 0;
		var failed = 0;
		foreach (var file in flacFiles)
		{
			ct.ThrowIfCancellationRequested();
			TranscodeOutcome outcome;
			try
			{
				outcome = await TranscodeFileAsync(file, ct);
			}
			catch (Exception ex)
				when (ex is not OperationCanceledException and not TranscodePipelineException)
			{
				Telemetry.Warn(
					"Audio.Transcode.UnexpectedException file={File}: {Error}",
					Path.GetFileName(file),
					ex.Message
				);
				outcome = TranscodeOutcome.Failed;
			}
			switch (outcome)
			{
				case TranscodeOutcome.Converted:
					converted++;
					break;
				case TranscodeOutcome.SkippedNotFlac:
					skippedNotFlac++;
					break;
				case TranscodeOutcome.SkippedAlreadyMp3:
					skippedAlreadyMp3++;
					break;
				case TranscodeOutcome.SkippedAlready16Bit:
					skippedAlready16Bit++;
					break;
				default:
					failed++;
					break;
			}
		}

		var totalSkipped = skippedNotFlac + skippedAlreadyMp3 + skippedAlready16Bit;
		Telemetry.Info(
			"Transcode: {Converted} converted, {Skipped} skipped, {Failed} failed",
			converted,
			totalSkipped,
			failed
		);
		return new FlacTranscodeResult(
			converted,
			skippedNotFlac,
			skippedAlreadyMp3,
			skippedAlready16Bit,
			failed
		);
	}

	public async Task<TranscodeOutcome> TranscodeFileAsync(
		string file,
		CancellationToken ct = default
	)
	{
		var fileName = Path.GetFileName(file);
		ErrorOr<FlacProbeResult> probeOr = await ProbeAsync(file, ct);
		if (probeOr.IsError)
		{
			Telemetry.Warn(
				"Audio.Transcode.ProbeFailed file={File} err={Err}",
				fileName,
				probeOr.FirstError.Description
			);
			return TranscodeOutcome.Failed;
		}

		FlacProbeResult probe = probeOr.Value;
		if (probe.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase) is false)
		{
			if (probe.Codec.Equals("mp3", StringComparison.OrdinalIgnoreCase))
			{
				Telemetry.Info("{File:l}: already MP3, skipping", fileName);
				return TranscodeOutcome.SkippedAlreadyMp3;
			}

			Telemetry.Info("{File:l}: not FLAC ({Codec:l}), skipping", fileName, probe.Codec);
			return TranscodeOutcome.SkippedNotFlac;
		}

		if (probe.Bits is > 0 and <= 16)
		{
			Telemetry.Info("{File:l}: already {Bits}-bit, skipping", fileName, probe.Bits);
			return TranscodeOutcome.SkippedAlready16Bit;
		}

		var targetSampleRate = ResolveTargetSampleRate(probe.SampleRate);
		Telemetry.Info("Transcoding: {File:l}", fileName);
		var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.flac");
		ErrorOr<string> convertOr = await sox.DownsampleTo16BitAsync(
			file,
			tempDest,
			targetSampleRate,
			ct
		);
		if (convertOr.IsError)
		{
			Telemetry.Warn(
				"Audio.Transcode.SoxFailed file={File} err={Err}",
				fileName,
				convertOr.FirstError.Description
			);
			DeleteIfExists(tempDest);
			return TranscodeOutcome.Failed;
		}

		ErrorOr<FlacProbeResult> verifyOr = await ProbeAsync(tempDest, ct);
		if (
			verifyOr.IsError
			|| verifyOr.Value.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase) is false
			|| verifyOr.Value.Bits != 16
			|| verifyOr.Value.SampleRate != targetSampleRate
		)
		{
			var got = verifyOr.IsError
				? verifyOr.FirstError.Description
				: $"bits={verifyOr.Value.Bits} rate={verifyOr.Value.SampleRate}";
			DeleteIfExists(tempDest);
			DeleteIfExists(file);
			throw new TranscodePipelineException(
				$"sox reported success but its own output for {fileName} failed verification (wanted 16-bit/{targetSampleRate}Hz flac, got {got}) — our transcode pipeline is broken, not the source file"
			);
		}

		try
		{
			File.Move(tempDest, file, overwrite: true);
		}
		catch (Exception ex)
		{
			throw new TranscodePipelineException(
				$"could not move our own verified-good transcode into place for {fileName}: {ex.Message}"
			);
		}

		Telemetry.Info("{File:l}: {FromBits}-bit → 16-bit", fileName, probe.Bits);
		return TranscodeOutcome.Converted;
	}

	private async Task<ErrorOr<FlacProbeResult>> ProbeAsync(string filePath, CancellationToken ct)
	{
		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			"ffprobe",
			["-v", "quiet", "-print_format", "json", "-show_streams", filePath],
			ct
		);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ProbeFailed(filePath, $"ffprobe exit code {result.Value.ExitCode}");

		try
		{
			using JsonDocument doc = JsonDocument.Parse(result.Value.Stdout);
			if (
				doc.RootElement.TryGetProperty("streams", out JsonElement streamsEl) is false
				|| streamsEl.GetArrayLength() == 0
			)
				return Errors.Audio.ProbeFailed(filePath, "no streams");

			JsonElement first = streamsEl[0];
			var codec =
				first.TryGetProperty("codec_name", out JsonElement codecEl)
				&& codecEl.GetString() is string codecVal
					? codecVal
					: string.Empty;
			var bits = 0;
			if (
				first.TryGetProperty("bits_per_raw_sample", out JsonElement bitsEl)
				&& bitsEl.GetString() is string bitsStr
				&& int.TryParse(bitsStr, out var parsedBits)
			)
				bits = parsedBits;
			else if (
				first.TryGetProperty("bits_per_sample", out JsonElement bpsEl)
				&& bpsEl.ValueKind is JsonValueKind.Number
			)
				bits = bpsEl.GetInt32();
			var sampleRate = 0;
			if (
				first.TryGetProperty("sample_rate", out JsonElement srEl)
				&& srEl.GetString() is string srStr
				&& int.TryParse(srStr, out var parsedSr)
			)
				sampleRate = parsedSr;

			return new FlacProbeResult(codec, bits, sampleRate);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Audio.Transcode.ProbeParseFailed file={File}: {Error}",
				Path.GetFileName(filePath),
				ex.Message
			);
			return Errors.Audio.ProbeFailed(filePath, ex.Message);
		}
	}

	private static int ResolveTargetSampleRate(int sourceSampleRate)
	{
		var target = sourceSampleRate;
		while (target > 48000)
			target /= 2;
		return target;
	}

	private static void DeleteIfExists(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Audio.Transcode.DeleteFailed file={File}: {Error}",
				Path.GetFileName(path),
				ex.Message
			);
		}
	}
}

public enum TranscodeOutcome
{
	Converted,
	SkippedNotFlac,
	SkippedAlreadyMp3,
	SkippedAlready16Bit,
	Failed,
}

public sealed record FlacProbeResult(string Codec, int Bits, int SampleRate);

public sealed record FlacTranscodeResult(
	int Converted,
	int SkippedNotFlac,
	int SkippedAlreadyMp3,
	int SkippedAlready16Bit,
	int Failed
)
{
	public int Skipped => SkippedNotFlac + SkippedAlreadyMp3 + SkippedAlready16Bit;
}

public sealed class TranscodePipelineException(string message) : Exception(message);
