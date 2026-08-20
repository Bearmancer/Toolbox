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
		var skipped = 0;
		var failed = 0;
		foreach (var file in flacFiles)
		{
			ct.ThrowIfCancellationRequested();
			TranscodeOutcome outcome = await TranscodeFileAsync(file, ct);
			switch (outcome)
			{
				case TranscodeOutcome.Converted:
					converted++;
					break;
				case TranscodeOutcome.Skipped:
					skipped++;
					break;
				default:
					failed++;
					break;
			}
		}

		Telemetry.Info(
			"Transcode: {Converted} converted, {Skipped} skipped, {Failed} failed",
			converted,
			skipped,
			failed
		);
		return new FlacTranscodeResult(converted, skipped, failed);
	}

	public async Task<TranscodeOutcome> TranscodeFileAsync(
		string file,
		CancellationToken ct = default
	)
	{
		ErrorOr<FlacProbeResult> probeOr = await ProbeAsync(file, ct);
		if (probeOr.IsError)
		{
			Telemetry.Warn(
				"Audio.Transcode.ProbeFailed file={File} err={Err}",
				Path.GetFileName(file),
				probeOr.FirstError.Description
			);
			return TranscodeOutcome.Failed;
		}

		FlacProbeResult probe = probeOr.Value;
		if (probe.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase) is false)
		{
			if (probe.Codec.Equals("mp3", StringComparison.OrdinalIgnoreCase))
				Telemetry.Info("{File}: already MP3, skipping", Path.GetFileName(file));
			else
				Telemetry.Info(
					"{File}: not FLAC ({Codec}), skipping",
					Path.GetFileName(file),
					probe.Codec
				);
			return TranscodeOutcome.Skipped;
		}

		if (probe.Bits is > 0 and <= 16)
		{
			Telemetry.Info(
				"{File}: already {Bits}-bit, skipping",
				Path.GetFileName(file),
				probe.Bits
			);
			return TranscodeOutcome.Skipped;
		}

		var targetSampleRate = ResolveTargetSampleRate(probe.SampleRate);
		Telemetry.Debug(
			"Audio.Transcode.Target file={File} fromRate={FromRate} toRate={ToRate}",
			Path.GetFileName(file),
			probe.SampleRate,
			targetSampleRate
		);
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
				Path.GetFileName(file),
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
			Telemetry.Warn(
				"Audio.Transcode.VerifyFailed file={File} bits={Bits} rate={Rate} wantRate={WantRate}",
				Path.GetFileName(file),
				verifyOr.IsError ? -1 : verifyOr.Value.Bits,
				verifyOr.IsError ? -1 : verifyOr.Value.SampleRate,
				targetSampleRate
			);
			DeleteIfExists(tempDest);
			DeleteIfExists(file);
			return TranscodeOutcome.Failed;
		}

		try
		{
			File.Delete(file);
			File.Move(tempDest, file);
			Telemetry.Info(
				"{File}: {FromBits}-bit/{FromRate} → 16-bit/{ToRate}",
				Path.GetFileName(file),
				probe.Bits,
				probe.SampleRate,
				targetSampleRate
			);
			return TranscodeOutcome.Converted;
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Audio.Transcode.ReplaceFailed file={File}: {Error}",
				Path.GetFileName(file),
				ex.Message
			);
			return TranscodeOutcome.Failed;
		}
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
	Skipped,
	Failed,
}

public sealed record FlacProbeResult(string Codec, int Bits, int SampleRate);

public sealed record FlacTranscodeResult(int Converted, int Skipped, int Failed);
