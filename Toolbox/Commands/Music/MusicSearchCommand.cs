using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Music;

namespace Toolbox.Commands.Music;

[Description("Search for music releases across MusicBrainz and Discogs")]
public class MusicSearchCommand : CommandBase<MusicSearchCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var results = new List<SearchResult>();

        if (settings.Source is "all" or "musicbrainz")
        {
            var mbResults = await MusicBrainzService.SearchReleasesAsync(
                release: settings.Query,
                maxResults: settings.Max,
                ct: ct
            );
            results.AddRange(mbResults);
        }

        if (settings.Source is "all" or "discogs")
        {
            if (string.IsNullOrEmpty(AppConfig.DiscogsUserToken))
            {
                Ui.Warning("Discogs token not configured, skipping Discogs search");
            }
            else
            {
                var discogsResults = await DiscogsService.SearchAsync(
                    settings.Query,
                    AppConfig.DiscogsUserToken,
                    settings.Max,
                    ct
                );
                results.AddRange(discogsResults);
            }
        }

        if (results.Count == 0)
        {
            Ui.Warning("No results found");
            return 0;
        }

        foreach (var result in results.Take(settings.Max))
        {
            var year = result.Year.HasValue ? $" ({result.Year})" : "";
            var artist = result.Artist is not null ? $"{result.Artist} - " : "";
            Ui.Info($"{artist}{result.Title}{year} [{result.Source}]");
        }

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Search query (artist, album, or release name)")]
        public string Query { get; init; } = "";

        [CommandOption("--source <SOURCE>")]
        [DefaultValue("all")]
        [Description("Source to search: all, musicbrainz, or discogs")]
        public string Source { get; init; } = "all";

        [CommandOption("--max <MAX>")]
        [DefaultValue(25)]
        [Description("Maximum number of results to return")]
        public int Max { get; init; } = 25;
    }
}