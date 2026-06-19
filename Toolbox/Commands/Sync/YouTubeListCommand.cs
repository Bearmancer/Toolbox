using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Core.Screen;
using Toolbox.Sync;

namespace Toolbox.Commands.Sync;

public class YouTubeListCommand : CommandBase<YouTubeListCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(AppConfig.YouTubeApiKey))
        {
            Ui.Error("YouTube API key not configured");
            return 1;
        }

        if (string.IsNullOrEmpty(settings.Channel))
        {
            Ui.Error("Channel ID is required");
            return 1;
        }

        var playlists = await YouTubeService.GetPlaylistsAsync(
            AppConfig.YouTubeApiKey,
            settings.Channel,
            ct
        );

        if (playlists.Count == 0)
        {
            Ui.Warning("No playlists found");
            return 0;
        }

        foreach (var playlist in playlists)
            Ui.Info($"{playlist.Title} ({playlist.VideoCount} videos)");

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--channel <CHANNEL>")] public string? Channel { get; init; }
    }
}