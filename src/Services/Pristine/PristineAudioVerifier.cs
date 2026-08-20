using System.Diagnostics;
using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Pristine;

public sealed class PristineAudioVerifier
{
	public async Task<ErrorOr<PristineProbeResult>> VerifyAsync(
		string filePath,
		string code,
		int trackNum,
		CancellationToken ct = default
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		if (File.Exists(filePath) is false)
		{
			Telemetry.Warn(
				"Pristine.Verify.Missing code={Code} track={Track} path={Path}",
				code,
				trackNum,
				Path.GetFileName(filePath)
			);
			return new PristineProbeResult(false, string.Empty, 0, 0, 0, "missing");
		}

		var bytes = new FileInfo(filePath).Length;
		Telemetry.Debug(
			"Pristine.Verify.Start code={Code} track={Track} path={Path} bytes={Bytes}",
			code,
			trackNum,
			Path.GetFileName(filePath),
			bytes
		);
		if (bytes == 0)
		{
			Telemetry.Warn(
				"Pristine.Verify.Empty code={Code} track={Track} path={Path}",
				code,
				trackNum,
				Path.GetFileName(filePath)
			);
			return new PristineProbeResult(false, string.Empty, 0, 0, 0, "empty");
		}

		if (IsFfprobeAvailable() is false)
		{
			Telemetry.Error(
				"Pristine.Verify.NoFfprobe code={Code} track={Track} path={Path} bytes={Bytes} — ffprobe missing, cannot verify",
				code,
				trackNum,
				Path.GetFileName(filePath),
				bytes
			);
			return Errors.Pristine.FfprobeMissing;
		}

		try
		{
			ProcessStartInfo psi = new()
			{
				FileName = "ffprobe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			psi.ArgumentList.Add("-v");
			psi.ArgumentList.Add("quiet");
			psi.ArgumentList.Add("-print_format");
			psi.ArgumentList.Add("json");
			psi.ArgumentList.Add("-show_streams");
			psi.ArgumentList.Add(filePath);
			using Process? process = Process.Start(psi);
			if (process is null)
			{
				Telemetry.Warn(
					"Pristine.Verify.StartFailed code={Code} track={Track}",
					code,
					trackNum
				);
				return new PristineProbeResult(false, string.Empty, 0, 0, 0, "start-failed");
			}

			var stdout = await process.StandardOutput.ReadToEndAsync(ct);
			var stderr = await process.StandardError.ReadToEndAsync(ct);
			await process.WaitForExitAsync(ct);
			if (process.ExitCode != 0)
			{
				Telemetry.Warn(
					"Pristine.Verify.FfprobeExit code={Code} track={Track} exit={Exit} stderr={Err}",
					code,
					trackNum,
					process.ExitCode,
					stderr.Length > 500 ? stderr[..500] : stderr
				);
				return new PristineProbeResult(
					false,
					string.Empty,
					0,
					0,
					0,
					$"exit-{process.ExitCode}"
				);
			}

			using JsonDocument doc = JsonDocument.Parse(stdout);
			if (
				doc.RootElement.TryGetProperty("streams", out JsonElement streamsEl) is false
				|| streamsEl.GetArrayLength() == 0
			)
			{
				Telemetry.Warn(
					"Pristine.Verify.NoStreams code={Code} track={Track}",
					code,
					trackNum
				);
				return new PristineProbeResult(false, string.Empty, 0, 0, 0, "no-streams");
			}

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
			var channels =
				first.TryGetProperty("channels", out JsonElement chEl)
				&& chEl.ValueKind is JsonValueKind.Number
					? chEl.GetInt32()
					: 0;
			var streamCount = streamsEl.GetArrayLength();
			var isFlac = codec.Equals("flac", StringComparison.OrdinalIgnoreCase);
			var is24 = bits == 24;
			Telemetry.Debug(
				"Pristine.Verify.Result code={Code} track={Track} streams={Streams} codec={Codec} bits={Bits} rate={Rate} channels={Channels} path={Path}",
				code,
				trackNum,
				streamCount,
				codec,
				bits,
				sampleRate,
				channels,
				Path.GetFileName(filePath)
			);
			if (isFlac is false)
				Telemetry.Debug(
					"Pristine.Verify.NotFlac code={Code} track={Track} codec={Codec}",
					code,
					trackNum,
					codec
				);
			if (is24 is false && bits != 0)
				Telemetry.Debug(
					"Pristine.Verify.Not24Bit code={Code} track={Track} bits={Bits}",
					code,
					trackNum,
					bits
				);
			return new PristineProbeResult(
				isFlac && is24,
				codec,
				bits,
				sampleRate,
				channels,
				$"streams:{streamCount}"
			);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn(
				"Pristine.Verify.Exception code={Code} track={Track}: {Error}",
				code,
				trackNum,
				ex.Message
			);
			return new PristineProbeResult(false, string.Empty, 0, 0, 0, ex.Message);
		}
	}

	public static bool IsFfprobeAvailable() =>
		IsOnPath("ffprobe") || File.Exists("ffprobe") || File.Exists("ffprobe.exe");

	private static bool IsOnPath(string binaryName)
	{
		var path = Environment.GetEnvironmentVariable("PATH");
		if (path is null)
			return false;
		var dirs = path.Split(Path.PathSeparator);
		return dirs.Any(d =>
			File.Exists(Path.Combine(d, binaryName))
			|| File.Exists(Path.Combine(d, binaryName + ".exe"))
		);
	}
}

public sealed record PristineProbeResult(
	bool Is24BitFlac,
	string Codec,
	int Bits,
	int SampleRate,
	int Channels,
	string Note
);
