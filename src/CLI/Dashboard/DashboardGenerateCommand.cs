using System.ComponentModel;
using System.Text.Json;
using Core;
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
    private static readonly string StateRoot = Path.Combine(
        PathResolver.RepoRoot,
        "state",
        "youtube"
    );

    private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

    private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");

    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.YouTube);

        var playlists = await LoadPlaylistsAsync(ct);
        Telemetry.Info("Loaded {Count} playlists from manifest", playlists.Count);

        var videosByPlaylist = await LoadVideosByPlaylistAsync(ct);
        var totalVideos = videosByPlaylist.Values.Sum(v => v.Count);
        Telemetry.Info("Loaded {Count} videos across {PlaylistCount} playlists", totalVideos, videosByPlaylist.Count);

        var html = DashboardHtmlGenerator.Generate(playlists, videosByPlaylist);

        var outputPath = s.Output
            ?? Path.Combine(Directory.GetCurrentDirectory(), "dashboard.html");
        await File.WriteAllTextAsync(outputPath, html, ct);

        var size = new FileInfo(outputPath).Length;
        AnsiConsole.MarkupLine(
            $"[green]Dashboard generated:[/] {outputPath} ({size / 1024.0:F1} KB)"
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
            var state = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
            return [.. state.PlaylistSnapshots.Values];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Telemetry.Error("Failed to load manifest: {Error}", ex.Message);
            return [];
        }
    }

    private static async Task<Dictionary<string, IReadOnlyList<YouTubeVideo>>> LoadVideosByPlaylistAsync(
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, IReadOnlyList<YouTubeVideo>>();

        if (!Directory.Exists(ProcessedDir))
            return result;

        var files = Directory.GetFiles(ProcessedDir, "*.json");

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var videos = await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
                    stream,
                    YouTubeFetchState.JsonOptions,
                    ct
                );
                if (videos is { Count: > 0 })
                {
                    var playlistName = Path.GetFileNameWithoutExtension(file);
                    result[playlistName] = videos;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Telemetry.Warn("Skipping corrupt file {File}: {Error}",
                    Path.GetFileName(file), ex.Message);
            }
        }

        return result;
    }

    public sealed class Settings : CommandSettings
    {
        [Description(
            "Output file path for the generated HTML dashboard. "
                + "(default: dashboard.html in current directory)"
        )]
        [CommandOption("--output <PATH>")]
        public string? Output { get; init; }
    }
}
