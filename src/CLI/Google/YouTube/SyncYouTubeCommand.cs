using System.ComponentModel;
using Services.Google;
using Spectre.Console.Cli;

namespace CLI.Google.YouTube;

[Description("Sync YouTube playlist(s) to local JSON files")]
public class SyncYouTubeCommand(YouTubePlaylistOrchestrator orchestrator) : AsyncCommand<SyncYouTubeCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(s.Playlist))
            await orchestrator.ExecuteForPlaylistTitleAsync(s.Playlist, ct);
        else
            await orchestrator.ExecuteAsync(ct);

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Optional playlist title to sync. If omitted, syncs all playlists.")]
        [CommandArgument(0, "[playlist]")]
        public string? Playlist { get; init; }
    }
}
