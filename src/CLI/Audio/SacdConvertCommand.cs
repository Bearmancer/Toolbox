using System.ComponentModel;
using Core;
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
		[CommandArgument(0, "[input]")]
		public string Input { get; init; } = string.Empty;

		[Description("Output format: 16 (only supported value)")]
		[CommandOption("-f|--format")]
		public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Bit16;

		[Description("Force multichannel extraction (auto-detected if omitted)")]
		[CommandOption("-m|--multichannel")]
		public bool? Multichannel { get; init; }

		[Description("Keep source ISO files (ISO deleted after conversion by default)")]
		[CommandOption("--keep-iso")]
		public bool KeepIso { get; init; }

		[Description("Clear all guard entries and exit")]
		[CommandOption("--reset-guard")]
		public bool ResetGuard { get; init; }
	}

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings settings,
		CancellationToken cancellationToken
	)
	{
		if (settings.ResetGuard)
		{
			ReprocessGuard guard = await ReprocessGuard.LoadAsync();
			await guard.ResetAllAsync(cancellationToken);
			await Console.Out.WriteLineAsync("Guard entries cleared.", cancellationToken);
			return 0;
		}

		if (string.IsNullOrWhiteSpace(settings.Input))
		{
			await Console.Error.WriteLineAsync(
				"Input path is required (or use --reset-guard).",
				cancellationToken
			);
			return 1;
		}

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
