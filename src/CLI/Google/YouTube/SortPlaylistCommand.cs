using System.ComponentModel;
using Services.Google;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Google.YouTube;

[Description("Sort all items in a YouTube playlist alphabetically by title")]
public class SortPlaylistCommand(YoutubeService service) : AsyncCommand<SortPlaylistCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        await service.SortPlaylistAlphaAsync(s.PlaylistId, ct);
        AnsiConsole.MarkupLine("[green]Playlist sorted alphabetically.[/]");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The YouTube playlist ID to sort (found in the playlist URL).")]
        [CommandArgument(0, "<playlistId>")]
        public required string PlaylistId { get; init; }
    }
}
