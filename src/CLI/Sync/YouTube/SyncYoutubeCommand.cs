using System.ComponentModel;
using CLI.Dashboard;
using Core;
using ErrorOr;
using Services.Google.YouTube;
using Spectre.Console.Cli;

namespace CLI.Sync.YouTube;

[Description(
	"Sync YouTube playlist metadata to local JSON files then optionally sort "
		+ "all items alphabetically by title. Syncing downloads video titles, "
		+ "durations, channel info, and auto-translated titles. "
		+ "Change detection uses YouTube ETags to avoid re-fetching unchanged playlists. "
		+ "Sorting uses LIS-based differential repositioning — only items whose position "
		+ "actually changed are sent to the API, minimizing quota usage. "
		+ "The stored ETag is updated after each sync so subsequent runs skip "
		+ "playlists that haven't changed on YouTube."
)]
public class SyncYoutubeCommand(YouTubePlaylistOrchestrator orchestrator)
	: AsyncCommand<SyncYoutubeCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		try
		{
			using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

			if (!string.IsNullOrEmpty(s.Playlist))
			{
				var id = s.NoSort
					? await orchestrator.ExecuteForPlaylistTitleAsync(
						s.Playlist,
						s.NoTranslate,
						cancellationToken
					)
					: await orchestrator.ExecuteForPlaylistTitleWithSortAsync(
						s.Playlist,
						s.NoTranslate,
						cancellationToken
					);
			}
			else
			{
				IReadOnlyList<string> syncedIds = s.NoSort
					? await orchestrator.ExecuteAsync(s.NoTranslate, cancellationToken)
					: await orchestrator.ExecuteWithSortAsync(s.NoTranslate, cancellationToken);
			}

			await RegenerateDashboardAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
		{
			Telemetry.Error("API error: {Error}", ex.Message);
			return 1;
		}

		return 0;
	}

	private async Task RegenerateDashboardAsync(CancellationToken ct)
	{
		ErrorOr<DashboardService.DashboardResult> result =
			await DashboardService.GenerateDashboardDataAsync(ct);
		if (result.IsError)
		{
			Telemetry.Warn("Dashboard skipped: {Error}", result.FirstError.Description);
			return;
		}

		DashboardData data = DashboardDataBuilder.Build(
			result.Value.Playlists,
			result.Value.VideosByPlaylist
		);
		var html = DashboardHtmlGenerator.Generate(data);
		var dashboardDir = PathResolver.GetStatePath("dashboard");
		Directory.CreateDirectory(dashboardDir);
		var htmlPath = Path.Combine(dashboardDir, "dashboard.html");
		var dataPath = Path.Combine(dashboardDir, "dashboard-data.js");
		await File.WriteAllTextAsync(htmlPath, html, ct);
		await File.WriteAllTextAsync(dataPath, data.DataJs, ct);
		Telemetry.Info("Dashboard regenerated");

		await OciDashboardDeployer.DeployAsync(dashboardDir, ct);
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Optional playlist title (partial match) to sync. "
				+ "If omitted, all playlists that changed since the last run are synced. "
				+ "If the title matches multiple playlists the first hit is used."
		)]
		[CommandArgument(0, "[playlist]")]
		public string? Playlist { get; init; }

		[Description(
			"Skip sorting playlist items alphabetically by title on YouTube. "
				+ "By default the synced playlist is sorted after metadata is fetched. "
				+ "Sorting uses LIS optimization to minimize API calls."
		)]
		[CommandOption("--no-sort")]
		public bool NoSort { get; init; }

		[Description("Skip Azure translation of video titles and descriptions.")]
		[CommandOption("--no-translate")]
		public bool NoTranslate { get; init; }
	}
}
