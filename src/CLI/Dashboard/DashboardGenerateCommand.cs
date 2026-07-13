using System.ComponentModel;
using Core;
using ErrorOr;
using Services.Google.YouTube;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Dashboard;

[Description(
	"Generate an HTML dashboard from locally synced YouTube playlist data. "
		+ "Loads all playlists from the manifest and all videos from processed JSON files."
)]
public class DashboardGenerateCommand : AsyncCommand<DashboardGenerateCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken ct
	)
	{
		ErrorOr<DashboardService.DashboardResult> result =
			await DashboardService.GenerateDashboardDataAsync(ct);
		if (result.IsError)
		{
			AnsiConsole.MarkupLine($"[red]{result.FirstError.Description}[/]");
			return 1;
		}

		DashboardService.DashboardResult dashboardResult = result.Value;
		DashboardData data = DashboardDataBuilder.Build(
			dashboardResult.Playlists,
			dashboardResult.VideosByPlaylist
		);
		var html = DashboardHtmlGenerator.Generate(data);

		var dashboardDir = PathResolver.GetStatePath("dashboard");
		Directory.CreateDirectory(dashboardDir);

		var htmlPath = s.Output ?? Path.Combine(dashboardDir, "dashboard.html");
		var dataPath = Path.Combine(dashboardDir, "dashboard-data.js");

		await File.WriteAllTextAsync(htmlPath, html, ct);
		await File.WriteAllTextAsync(dataPath, data.DataJs, ct);

		var htmlSize = new FileInfo(htmlPath).Length;
		var dataSize = new FileInfo(dataPath).Length;
		AnsiConsole.MarkupLine(
			$"[green]Dashboard generated:[/] {htmlPath} ({htmlSize / 1024.0:F1} KB)"
		);
		AnsiConsole.MarkupLine(
			$"[green]Data file:[/]        {dataPath} ({dataSize / 1024.0:F1} KB)"
		);
		return 0;
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
