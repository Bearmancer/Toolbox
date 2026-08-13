using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class DiskSpaceChecker
{
	private const long IsoExpansionFactor = 4;
	private const long FlacExpansionFactor = 8;
	private const long SafetyMarginBytes = 500L * 1_048_576;

	public ErrorOr<Success> CheckSpaceForExtraction(string path, long isoSizeBytes)
	{
		var requiredBytes = (isoSizeBytes * IsoExpansionFactor) + SafetyMarginBytes;
		return CheckAvailableSpace(path, requiredBytes);
	}

	public ErrorOr<Success> CheckSpaceForConversion(string path, long isoSizeBytes)
	{
		var requiredBytes = (isoSizeBytes * FlacExpansionFactor) + SafetyMarginBytes;
		return CheckAvailableSpace(path, requiredBytes);
	}

	private static ErrorOr<Success> CheckAvailableSpace(string path, long requiredBytes)
	{
		DriveInfo driveInfo = new(Path.GetPathRoot(Path.GetFullPath(path)) ?? path);
		var availableBytes = driveInfo.AvailableFreeSpace;

		if (availableBytes < requiredBytes)
			return Errors.Audio.InsufficientDiskSpace(path, requiredBytes, availableBytes);

		return Result.Success;
	}
}
