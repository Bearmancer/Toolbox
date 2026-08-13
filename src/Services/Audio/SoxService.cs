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

		ErrorOr<ProcessResult> result = await processRunner.RunAsync(binaryPath, [.. args], ct);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourcePcm,
				$"sox split exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
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
			ct
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

	public async Task<ErrorOr<TimeSpan>> GetDurationAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			["--i", "-D", filePath],
			ct
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

	public async Task<ErrorOr<string>> DeriveFlacAsync(
		string sourceFlac,
		string outputFlac,
		int targetSampleRate,
		CancellationToken ct = default
	)
	{
		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			[
				sourceFlac,
				"-b",
				"16",
				outputFlac,
				"rate",
				"-v",
				targetSampleRate.ToString(CultureInfo.InvariantCulture),
			],
			ct
		);

		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				sourceFlac,
				$"sox derive exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		return outputFlac;
	}

	private static string FormatSeconds(TimeSpan t) =>
		t.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
}
