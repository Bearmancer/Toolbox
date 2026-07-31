using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SacdExtractService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly Regex StereoPattern = new(
		@"Speaker config:\s*(?:Stereo|2)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);
	private static readonly Regex MultichannelPattern = new(
		@"Speaker config:\s*(?:Multichannel|5|6)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	public async Task<ErrorOr<SacdProbeResult>> ProbeAsync(
		string isoPath,
		CancellationToken ct = default
	)
	{
		var result = await processRunner.RunAsync(binaryPath, ["-P", "-i", isoPath], ct);

		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ExtractionFailed(
				binaryPath,
				$"Exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		var output = result.Value.Stdout + "\n" + result.Value.Stderr;
		var hasStereo = StereoPattern.IsMatch(output);
		var hasMch = MultichannelPattern.IsMatch(output);

		if (!hasStereo && !hasMch)
			return Errors.Audio.ExtractionFailed(
				isoPath,
				"No stereo or multichannel tracks detected"
			);

		return new SacdProbeResult(isoPath, hasStereo, hasMch);
	}

	public async Task<ErrorOr<List<string>>> ExtractAsync(
		string isoPath,
		string outputDir,
		bool multichannel,
		CancellationToken ct = default
	)
	{
		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		var channelFlag = multichannel ? "-m" : "-2";
		var beforeDirs = Directory.GetDirectories(outputDir);

		var result = await processRunner.RunAsync(
			binaryPath,
			[channelFlag, "-e", "-c", "-C", "-i", isoPath],
			ct,
			outputDir
		);

		if (result.IsError)
			return result.Errors;

		if (result.Value.ExitCode != 0)
			return Errors.Audio.ExtractionFailed(
				binaryPath,
				$"Exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
			);

		var afterDirs = Directory.GetDirectories(outputDir);
		var newDirs = afterDirs.Except(beforeDirs).ToList();

		if (newDirs.Count == 0)
		{
			var dffFiles = Directory.GetFiles(outputDir, "*.dff", SearchOption.AllDirectories);
			if (dffFiles.Length > 0)
			{
				var dir = Path.GetDirectoryName(dffFiles[0]);
				if (dir is not null)
					newDirs = [dir];
			}
		}

		return newDirs;
	}
}

public sealed record SacdProbeResult(string IsoPath, bool HasStereo, bool HasMultichannel);
