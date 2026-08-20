using System.ComponentModel;
using Core;
using ErrorOr;
using Services.Audio;
using Spectre.Console.Cli;

namespace CLI.Audio;

[Description("Downsample every FLAC in a directory to 16-bit in place.")]
internal sealed class FlacTranscodeCommand(FlacTranscodeService transcodeService)
	: AsyncCommand<FlacTranscodeCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[Description("Directory containing FLAC files to transcode")]
		[CommandArgument(0, "<directory>")]
		public required string Directory { get; init; }
	}

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings settings,
		CancellationToken cancellationToken
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Audio);

		var directory = Path.GetFullPath(settings.Directory);
		if (!System.IO.Directory.Exists(directory))
		{
			await Console.Error.WriteLineAsync(
				$"Directory not found: {directory}",
				cancellationToken
			);
			return 1;
		}

		ErrorOr<FlacTranscodeResult> result = await transcodeService.TranscodeDirectoryAsync(
			directory,
			cancellationToken
		);
		if (result.IsError)
		{
			await Console.Error.WriteLineAsync(result.Errors[0].Description, cancellationToken);
			return 1;
		}

		FlacTranscodeResult r = result.Value;
		await Console.Out.WriteLineAsync(
			$"Transcoded: {r.Converted} converted, {r.Skipped} skipped, {r.Failed} failed",
			cancellationToken
		);
		return r.Failed == 0 ? 0 : 1;
	}
}
