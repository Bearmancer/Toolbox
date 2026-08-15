using System.ComponentModel;
using Services.Audio;
using Spectre.Console.Cli;

namespace CLI.Audio;

using ErrorOr;

internal sealed class SacdConvertCommand(PipelineOrchestrator orchestrator)
	: AsyncCommand<SacdConvertCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[Description("Input SACD ISO file or directory containing .iso files")]
		[CommandArgument(0, "<input>")]
		public required string Input { get; init; }

		[Description("Output format: 16 (default), 24, both")]
		[CommandOption("-f|--format")]
		public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Bit16;

		[Description("Force multichannel extraction (auto-detected if omitted)")]
		[CommandOption("-m|--multichannel")]
		public bool? Multichannel { get; init; }

		[Description("Keep source ISO files (deleted by default)")]
		[CommandOption("--keep-iso")]
		public bool KeepIso { get; init; }
	}

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings settings,
		CancellationToken cancellationToken
	)
	{
		if (settings.Format != AudioOutputFormat.Bit16)
		{
			await Console.Error.WriteLineAsync(
				"SACD conversion supports only --format 16.",
				cancellationToken
			);
			return 1;
		}

		ErrorOr<PipelineResult> result = await orchestrator.RunAsync(
			settings.Input,
			settings.Format,
			settings.Multichannel,
			settings.KeepIso,
			cancellationToken
		);

		if (result.IsError)
		{
			foreach (Error error in result.Errors)
				await Console.Error.WriteLineAsync(error.Description, cancellationToken);
			return 1;
		}

		PipelineResult pipelineResult = result.Value;
			await Console.Out.WriteLineAsync(
				$"SACD processing completed: {pipelineResult.SucceededCount} succeeded, {pipelineResult.FailedCount} failed",
				cancellationToken
			);

		if (pipelineResult.GuardFailedDiscs.Count > 0)
		{
			await Console.Out.WriteLineAsync("Guard-failed discs:", cancellationToken);
			foreach (var disc in pipelineResult.GuardFailedDiscs)
				await Console.Out.WriteLineAsync($"  - {disc}", cancellationToken);
		}

		if (pipelineResult.RecoverableErrors.Count > 0)
		{
			await Console.Out.WriteLineAsync("Recoverable errors:", cancellationToken);
			foreach (var error in pipelineResult.RecoverableErrors)
				await Console.Out.WriteLineAsync($"  - {error}", cancellationToken);
		}

		return pipelineResult.FailedCount == 0 ? 0 : 1;
	}
}
