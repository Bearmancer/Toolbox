using System.Globalization;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SoxService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly Regex PeakLevelPattern = new(
		@"Pk lev dB\s+(-?\d+\.?\d*)",
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

		var result = await processRunner.RunAsync(binaryPath, [.. args], ct);
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
		var result = await processRunner.RunAsync(binaryPath, [filePath, "-n", "stats"], ct);
		if (result.IsError)
			return result.Errors;

		var output = result.Value.Stdout + "\n" + result.Value.Stderr;
		var match = PeakLevelPattern.Match(output);
		if (!match.Success)
			return Errors.Audio.GainDetectionFailed(filePath, "Could not parse sox stats output");

		return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	public async Task<ErrorOr<TimeSpan>> GetDurationAsync(
		string filePath,
		CancellationToken ct = default
	)
	{
		var result = await processRunner.RunAsync(binaryPath, ["--i", "-D", filePath], ct);
		if (result.IsError)
			return result.Errors;

		if (!double.TryParse(
			result.Value.Stdout.Trim(),
			CultureInfo.InvariantCulture,
			out var seconds
		))
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
		var result = await processRunner.RunAsync(
			binaryPath,
			[
				sourceFlac,
				"-b", "16",
				outputFlac,
				"rate", "-v",
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
