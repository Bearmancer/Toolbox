using System.Globalization;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SaraconService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
	private const int MaxRetries = 2;

	public async Task<ErrorOr<string>> ConvertDsdToPcmAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "wav");
		return await RunConversionWithRetryAsync(inputDff, outputDir, "wav", args, sampleRate, bitDepth, gainDb, onOutputLine, ct);
	}

	public async Task<ErrorOr<string>> ConvertDsdToFlacAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "flac");
		return await RunConversionWithRetryAsync(inputDff, outputDir, "flac", args, sampleRate, bitDepth, gainDb, onOutputLine, ct);
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

	private async Task<ErrorOr<string>> RunConversionWithRetryAsync(
		string inputDff,
		string outputDir,
		string extension,
		string[] args,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine,
		CancellationToken ct
	)
	{
		Telemetry.Debug("Saracon.ConvertStart input={Input} outputDir={OutputDir} format={Format} rate={Rate} bitDepth={BitDepth} gain={Gain}dB",
			Path.GetFileName(inputDff), outputDir, extension, sampleRate, bitDepth, gainDb);

		var hasId3 = DffMetadataStripper.HasId3Chunk(inputDff);
		if (hasId3)
			Telemetry.Warn("Saracon.Id3Detected input={Input} — ID3 chunk found, stripping before conversion",
				Path.GetFileName(inputDff));

		for (var attempt = 0; attempt <= MaxRetries; attempt++)
		{
			ct.ThrowIfCancellationRequested();

			if (!Directory.Exists(outputDir))
				Directory.CreateDirectory(outputDir);

			var effectiveArgs = args;
			if (hasId3)
			{
				var stripResult = await DffMetadataStripper.StripId3TagsAsync(inputDff, outputDir, ct);
				if (!stripResult.IsError)
				{
					effectiveArgs = BuildD2pArgs(stripResult.Value, outputDir, sampleRate, bitDepth, gainDb, extension);
				}
				else
				{
					Telemetry.Warn("Saracon.Id3StripFailed input={Input} error={Error}",
						Path.GetFileName(inputDff), stripResult.Errors[0].Description);
				}
			}

			var result = await processRunner.RunAsync(binaryPath, effectiveArgs, ct, timeout: DefaultTimeout, onOutputLine: onOutputLine);
			if (result.IsError)
			{
				var error = result.Errors[0];
				Telemetry.Debug("Saracon.AttemptFailed attempt={Attempt} error={Error}", attempt + 1, error.Description);
				if (attempt < MaxRetries && IsTransientError(error.Description))
				{
					CleanupLockedFiles(outputDir, Path.GetFileNameWithoutExtension(inputDff));
					await Task.Delay(TimeSpan.FromSeconds(2), ct);
					continue;
				}

				return result.Errors;
			}

			if (result.Value.ExitCode != 0)
			{
				var stderr = result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)];
				Telemetry.Debug("Saracon.ExitCodeNonZero attempt={Attempt} exitCode={ExitCode} stderr={Stderr}",
					attempt + 1, result.Value.ExitCode, stderr);
				if (attempt < MaxRetries && IsCharsetError(stderr))
				{
					CleanupLockedFiles(outputDir, Path.GetFileNameWithoutExtension(inputDff));
					await Task.Delay(TimeSpan.FromSeconds(2), ct);
					continue;
				}

				return Errors.Audio.ConversionFailed(inputDff, $"saracon exit code {result.Value.ExitCode}: {stderr}");
			}

			var expectedOutput = FindSaraconOutput(outputDir, Path.GetFileNameWithoutExtension(inputDff), extension);
			if (expectedOutput is null)
				return Errors.Audio.ConversionFailed(
					inputDff,
					$"saracon reported success but no .{extension} output found in {outputDir}"
				);

			Telemetry.Debug("Saracon.ConvertComplete output={Output} size={Size}MB",
				Path.GetFileName(expectedOutput), new FileInfo(expectedOutput).Length / 1_048_576.0);

			return expectedOutput;
		}

		return Errors.Audio.ConversionFailed(inputDff, "All retry attempts exhausted");
	}

	private static string? FindSaraconOutput(string outputDir, string baseName, string extension)
	{
		foreach (var file in Directory.GetFiles(outputDir, $"*.{extension}", SearchOption.TopDirectoryOnly))
		{
			var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
			if (nameWithoutExt.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
				return file;
		}

		return null;
	}

	private static bool IsCharsetError(string stderr) =>
		stderr.Contains("charset", StringComparison.OrdinalIgnoreCase)
		|| stderr.Contains("encoding", StringComparison.OrdinalIgnoreCase);

	private static bool IsTransientError(string error) =>
		error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
		|| error.Contains("charset", StringComparison.OrdinalIgnoreCase)
		|| error.Contains("encoding", StringComparison.OrdinalIgnoreCase);

	private static void CleanupLockedFiles(string outputDir, string baseName)
	{
		try
		{
			foreach (var ext in new[] { "*.wav", "*.flac" })
			{
				foreach (var file in Directory.GetFiles(outputDir, ext, SearchOption.TopDirectoryOnly))
				{
					if (Path.GetFileNameWithoutExtension(file).StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
					{
						try
						{
							File.Delete(file);
						}
						catch (Exception ex)
						{
							Telemetry.Warn("Saracon.CleanupLockedFile failed for {File}: {Error}", file, ex.Message);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Telemetry.Warn("Saracon.CleanupLockedFiles failed for dir={Dir}: {Error}", outputDir, ex.Message);
		}
	}
}
