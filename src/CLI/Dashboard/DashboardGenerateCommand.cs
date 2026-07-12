using System.ComponentModel;
using System.Text.Json;
using Core;
using Services.Google.YouTube;
using Spectre.Console;
using Spectre.Console.Cli;
using Text = Core.Text;

namespace CLI.Dashboard;

[Description(
	"Generate an HTML dashboard from locally synced YouTube playlist data. "
		+ "Loads all playlists from the manifest and all videos from processed JSON files."
)]
public class DashboardGenerateCommand : AsyncCommand<DashboardGenerateCommand.Settings>
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

		IReadOnlyList<PlaylistSnapshot> playlists = await LoadPlaylistsAsync(cancellationToken);
		Telemetry.Info("Loaded {Count} playlists from manifest", playlists.Count);

		Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist =
			await LoadVideosByPlaylistAsync(playlists, cancellationToken);
		var totalVideos = videosByPlaylist.Values.Sum(v => v.Count);
		Telemetry.Info(
			"Loaded {Count} videos across {PlaylistCount} playlists",
			totalVideos,
			videosByPlaylist.Count
		);

		DashboardData data = DashboardDataBuilder.Build(playlists, videosByPlaylist);
		var html = DashboardHtmlGenerator.Generate(data);

		var htmlPath =
			s.Output ?? Path.Combine(PathResolver.RepoRoot, "dashboard", "dashboard.html");
		var dataPath = Path.Combine(
			Path.GetDirectoryName(Path.GetFullPath(htmlPath))!,
			"dashboard-data.js"
		);

		await File.WriteAllTextAsync(htmlPath, html, cancellationToken);
		await File.WriteAllTextAsync(dataPath, data.DataJs, cancellationToken);

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

	private static async Task<IReadOnlyList<PlaylistSnapshot>> LoadPlaylistsAsync(
		CancellationToken ct
	)
	{
		if (!File.Exists(ManifestFile))
			return [];

		try
		{
			YouTubeFetchState state = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
			return [.. state.PlaylistSnapshots.Values];
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			Telemetry.Error("Failed to load manifest: {Error}", ex.Message);
			return [];
		}
	}

	private static async Task<
		Dictionary<string, IReadOnlyList<YouTubeVideo>>
	> LoadVideosByPlaylistAsync(IReadOnlyList<PlaylistSnapshot> playlists, CancellationToken ct)
	{
		var result = new Dictionary<string, IReadOnlyList<YouTubeVideo>>();

		if (!Directory.Exists(ProcessedDir))
			return result;

		var reverseLookup = playlists.ToDictionary(
			p => Text.SanitizeFileName(p.Title),
			p => p.Title
		);

		foreach (var file in Directory.GetFiles(ProcessedDir, "*.json"))
		{
			ct.ThrowIfCancellationRequested();

			try
			{
				await using FileStream stream = File.OpenRead(file);
				List<YouTubeVideo>? videos = await JsonSerializer.DeserializeAsync<
					List<YouTubeVideo>
				>(stream, YouTubeFetchState.JsonOptions, ct);
				if (
					videos is { Count: > 0 }
					&& reverseLookup.TryGetValue(
						Path.GetFileNameWithoutExtension(file),
						out var title
					)
				)
					result[title] = videos;
			}
			catch (Exception ex) when (ex is JsonException or IOException)
			{
				Telemetry.Warn(
					"Skipping corrupt file {File}: {Error}",
					Path.GetFileName(file),
					ex.Message
				);
			}
		}

		return result;
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Output file path for the generated HTML dashboard. "
				+ "(default: dashboard/dashboard.html)"
		)]
		[CommandOption("--output <PATH>")]
		public string? Output { get; init; }
	}
}
