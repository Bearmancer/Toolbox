using Core;
using ErrorOr;
using Services.Google.YouTube;

namespace Services.Google.Dashboard;

public sealed class DashboardOrchestrator
{
	public async Task<ErrorOr<DashboardService.DashboardResult>> GenerateDataAsync(
		CancellationToken ct
	) => await DashboardService.GenerateDashboardDataAsync(ct);

	public async Task<ErrorOr<string>> GenerateAndDeployAsync(
		string? outputPath,
		CancellationToken ct
	)
	{
		ErrorOr<DashboardService.DashboardResult> result =
			await DashboardService.GenerateDashboardDataAsync(ct);
		if (result.IsError)
			return result.Errors;

		DashboardService.DashboardResult dashboardResult = result.Value;
		DashboardData data = DashboardDataBuilder.Build(
			dashboardResult.Playlists,
			dashboardResult.VideosByPlaylist
		);
		var html = DashboardHtmlGenerator.Generate(data);
		var dashboardDir = PathResolver.GetStatePath("dashboard");
		Directory.CreateDirectory(dashboardDir);
		var htmlPath = outputPath ?? Path.Combine(dashboardDir, "dashboard.html");
		var dataPath = Path.Combine(dashboardDir, "dashboard-data.js");
		await File.WriteAllTextAsync(htmlPath, html, ct);
		await File.WriteAllTextAsync(dataPath, data.DataJs, ct);
		await OciDashboardDeployer.DeployAsync(dashboardDir, ct);
		return htmlPath;
	}
}
