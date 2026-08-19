using ErrorOr;
using Services.Pristine;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Pristine;

public sealed class PristineDownloadCommand(PristineOrchestrator orchestrator) : AsyncCommand<PristineDownloadCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[codes]")]
		public string[] Codes { get; init; } = [];

		[CommandOption("-o|--out-dir")]
		public string? OutDir { get; init; }

		[CommandOption("-H|--headless")]
		public bool Headless { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
	{
		if (settings.Headless)
		{
			Environment.SetEnvironmentVariable("PRISTINE_HEADLESS", "1");
		}

		var codes = settings.Codes.Length > 0 ? settings.Codes : null;
		ErrorOr<List<PristineAlbumResult>> result = await orchestrator.DownloadAsync(codes, settings.OutDir, settings.Headless, ct);
		return result.Match(
			results =>
			{
				foreach (PristineAlbumResult r in results)
				{
					AnsiConsole.MarkupLine($"[green]{r.Code}[/] {r.Title} {r.Downloaded}/{r.Expected} -> {r.OutPath}");
				}

				return 0;
			},
			errors =>
			{
				AnsiConsole.MarkupLine($"[red]{errors[0].Description}[/]");
				return 1;
			}
		);
	}
}
