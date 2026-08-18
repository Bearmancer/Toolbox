using System.ComponentModel;
using Core;
using ErrorOr;
using Serilog.Events;
using Services.Google.Dashboard;
using Spectre.Console.Cli;

namespace CLI.Dashboard;

[Description(
	"Generate an HTML dashboard from locally synced YouTube playlist data. "
		+ "Loads all playlists from the manifest and all videos from processed JSON files."
)]
public sealed class DashboardGenerateCommand(DashboardOrchestrator orchestrator)
	: AsyncCommand<DashboardGenerateCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken ct
	)
	{
		ErrorOr<string> result = await orchestrator.GenerateAndDeployAsync(s.Output, ct);
		return result.Match(
			htmlPath =>
			{
				Telemetry.Log(
					ServiceName.YouTube,
					LogEventLevel.Information,
					"Dashboard generated: {Path}",
					htmlPath
				);
				return 0;
			},
			errors =>
			{
				Telemetry.Log(
					ServiceName.YouTube,
					LogEventLevel.Error,
					"Dashboard generation failed: {Error}",
					errors[0].Description
				);
				return 1;
			}
		);
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Output file path for the generated HTML dashboard. "
				+ "(default: state/dashboard/dashboard.html)"
		)]
		[CommandOption("--output <PATH>")]
		public string? Output { get; init; }
	}
}
