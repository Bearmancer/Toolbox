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
}
