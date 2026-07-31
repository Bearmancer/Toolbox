using System.Globalization;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SaraconService(ProcessRunner processRunner, string binaryPath)
{
	public async Task<ErrorOr<string>> ConvertDsdToPcmAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "wav");
		return await RunConversionAsync(inputDff, outputDir, "wav", args, ct);
	}

	public async Task<ErrorOr<string>> ConvertDsdToFlacAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "flac");
		return await RunConversionAsync(inputDff, outputDir, "flac", args, ct);
	}

	private static string[] BuildD2pArgs(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		string format
	) =>
	[
		"-c", "d2p",
		"-r", sampleRate.ToString(CultureInfo.InvariantCulture),
		"-f", format,
		"-n", $"{bitDepth}bit",
		"-d", "tpdf",
		"-g", gainDb.ToString("F2", CultureInfo.InvariantCulture),
		"-T",
		"-V", "all",
		"-t", outputDir,
		inputDff,
	];

	private async Task<ErrorOr<string>> RunConversionAsync(
		string inputDff,
		string outputDir,
		string extension,
		string[] args,
		CancellationToken ct
	)
	{
		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		var result = await processRunner.RunAsync(binaryPath, args, ct);
		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		var expectedOutput = Path.Combine(
			outputDir,
			Path.GetFileNameWithoutExtension(inputDff) + $".{extension}"
		);

		if (!File.Exists(expectedOutput))
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon reported success but {expectedOutput} not found"
			);

		return expectedOutput;
	}
}
