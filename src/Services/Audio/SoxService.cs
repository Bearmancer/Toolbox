using System.Globalization;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SoxService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly Regex PeakLevelPattern = new(
		@"Pk lev dB\s+(-?\d+\.?\d*|-inf)",
		RegexOptions.Compiled
	);
	private static readonly Regex SoxProgressPattern = new(
		@"In:(\d+(?:\.\d+)?)%",
		RegexOptions.Compiled
	);

	public async Task<ErrorOr<string>> SplitTrackAsync(
		string sourcePcm,
		string outputFlac,
		TimeSpan start,
		TimeSpan? duration,
		CancellationToken ct = default
	)
	{
		List<string> args = [sourcePcm, outputFlac, "trim", FormatSeconds(start)];
		if (duration is { } d && d > TimeSpan.Zero)
			args.Add(FormatSeconds(d));

		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			[.. args],
			ct,
			onOutputLine: line => Telemetry.Debug("Sox.Output {Line}", line)
		);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourcePcm,
				$"sox split exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		if (!File.Exists(outputFlac) || new FileInfo(outputFlac).Length == 0)
			return Errors.Audio.ConversionFailed(
				outputFlac,
				"sox split exited 0 but produced no output file"
			);

		return outputFlac;
	}

	public async Task<ErrorOr<double>> GetPeakLevelAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		Telemetry.Debug("Sox.StatsStart file={File}", Path.GetFileName(filePath));

		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			[filePath, "-n", "stats"],
			ct,
			onOutputLine: line => Telemetry.Debug("Sox.Output {Line}", line)
		);
		if (result.IsError)
			return result.Errors;

		var output = result.Value.Stdout + "\n" + result.Value.Stderr;
		Match match = PeakLevelPattern.Match(output);
		if (!match.Success)
		{
			Telemetry.Warn(
				"Sox.StatsParseFailed file={File} stdoutLen={StdoutLen} stderrLen={StderrLen} output={Output}",
				Path.GetFileName(filePath),
				result.Value.Stdout.Length,
				result.Value.Stderr.Length,
				output[..Math.Min(output.Length, 500)]
			);
			return Errors.Audio.GainDetectionFailed(filePath, "Could not parse sox stats output");
		}

		var peak = match.Groups[1].Value.Equals("-inf", StringComparison.OrdinalIgnoreCase)
			? -120.0
			: double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		Telemetry.Debug(
			"Sox.StatsComplete file={File} peak={Peak}dB",
			Path.GetFileName(filePath),
			peak
		);

		return peak;
	}

	public async Task<ErrorOr<string>> DownsampleTo16BitAsync(
		string sourceFlac,
		string destFlac,
		int sampleRate,
		CancellationToken ct = default
	)
	{
		var fileName = Path.GetFileName(sourceFlac);
		Telemetry.Debug("Transcoding {File} → 16-bit", fileName);

		var wroteProgress = false;
		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			[
				"-S",
				sourceFlac,
				"-b",
				"16",
				"-R",
				"-G",
				destFlac,
				"rate",
				"-v",
				"-L",
				sampleRate.ToString(CultureInfo.InvariantCulture),
				"dither",
				"-s",
			],
			ct,
			onOutputLine: line =>
			{
				Match match = SoxProgressPattern.Match(line);
				if (match.Success is false || Console.IsOutputRedirected)
					return;
				wroteProgress = true;
				Console.Write($"\r  {match.Groups[1].Value, 5}%   ");
			}
		);
		if (wroteProgress)
			Console.Write("\r                \r");
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourceFlac,
				$"sox downsample exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		if (!File.Exists(destFlac) || new FileInfo(destFlac).Length == 0)
			return Errors.Audio.ConversionFailed(
				destFlac,
				"sox downsample exited 0 but produced no output file"
			);

		return destFlac;
	}

	public async Task<ErrorOr<TimeSpan>> GetDurationAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			["--i", "-D", filePath],
			ct,
			onOutputLine: line => Telemetry.Debug("Sox.Output {Line}", line)
		);
		if (result.IsError)
			return result.Errors;

		if (
			!double.TryParse(
				result.Value.Stdout.Trim(),
				CultureInfo.InvariantCulture,
				out var seconds
			)
		)
			return Errors.Audio.ProbeFailed(filePath, "Could not parse sox duration output");

		return TimeSpan.FromSeconds(seconds);
	}

	private static string FormatSeconds(TimeSpan t) =>
		t.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
}
