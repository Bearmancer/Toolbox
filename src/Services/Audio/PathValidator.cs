using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class PathValidator
{
	public ErrorOr<string> ValidateInputPath(string inputPath)
	{
		if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
			return Errors.Audio.InvalidInputPath(inputPath);

		var fullPath = Path.GetFullPath(inputPath);
		return fullPath;
	}

	public ErrorOr<string> ValidateOutputDirectory(string outputDir)
	{
		var fullPath = Path.GetFullPath(outputDir);

		try
		{
			if (!Directory.Exists(fullPath))
				Directory.CreateDirectory(fullPath);

			var testFile = Path.Combine(fullPath, Guid.NewGuid().ToString());
			using FileStream _ = File.Create(testFile);
			File.Delete(testFile);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Error(
				"PathValidator.OutputUnwritable path={Path}: {Error}",
				fullPath,
				ex.Message
			);
			return Errors.Audio.OutputPathUnwritable(fullPath);
		}

		return fullPath;
	}
}
