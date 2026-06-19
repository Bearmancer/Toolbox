using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Core.Screen;
using Toolbox.Music;

namespace Toolbox.Commands.Music;

public class MusicReleaseCommand : CommandBase<MusicReleaseCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        ReleaseData? release = null;

        if (settings.Source == "musicbrainz")
        {
            release = await MusicBrainzService.GetReleaseAsync(settings.Id, ct);
        }
        else if (settings.Source == "discogs")
        {
            if (string.IsNullOrEmpty(AppConfig.DiscogsUserToken))
            {
                Ui.Error("Discogs token not configured");
                return 1;
            }

            release = await DiscogsService.GetReleaseAsync(
                settings.Id,
                AppConfig.DiscogsUserToken,
                ct
            );
        }
        else
        {
            Ui.Error($"Unknown source: {settings.Source}");
            return 1;
        }

        if (release is null)
        {
            Ui.Error("Release not found");
            return 1;
        }

        Ui.Info($"Title: {release.Info.Title}");
        if (release.Info.Artist is not null)
            Ui.Info($"Artist: {release.Info.Artist}");
        if (release.Info.Year.HasValue)
            Ui.Info($"Year: {release.Info.Year}");
        if (release.Info.Label is not null)
            Ui.Info($"Label: {release.Info.Label}");

        Ui.Info($"Tracks: {release.Info.TrackCount}");
        Ui.Info($"Duration: {release.Info.TotalDuration:mm\\:ss}");

        if (release.Tracks.Count > 0)
        {
            Ui.NewLine();
            Ui.Info("Tracklist:");
            foreach (var track in release.Tracks)
            {
                var duration = track.Duration.HasValue ? $" [{track.Duration:mm\\:ss}]" : "";
                Ui.Info($"  {track.DiscNumber}.{track.TrackNumber:D2} {track.Title}{duration}");
            }
        }

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<id>")] public string Id { get; init; } = "";

        [CommandOption("--source <SOURCE>")]
        [DefaultValue("musicbrainz")]
        public string Source { get; init; } = "musicbrainz";
    }
}