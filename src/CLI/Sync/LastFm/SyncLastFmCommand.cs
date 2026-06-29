using System.ComponentModel;
using Core;
using Services.LastFm;
using Spectre.Console.Cli;

namespace CLI.Sync.LastFm;

[Description(
    "Sync Last.fm scrobble history to local JSON files. "
        + "Fetches recent tracks from Last.fm API and stores them "
        + "as structured JSON under state/lastfm/. Supports incremental sync "
        + "and forced resync from a specific date."
)]
public class SyncLastFmCommand(LastFmService service) : AsyncCommand<SyncLastFmCommand.Settings>
{
    private static readonly string StateDir = Path.Combine(
        PathResolver.RepoRoot,
        "state",
        "lastfm"
    );

    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(StateDir);

        var existing = await LastFmState.LoadScrobblesAsync(StateDir);

        DateTimeOffset? fetchAfter = null;
        if (s.Since is { } sinceStr)
        {
            if (DateTimeOffset.TryParse(sinceStr, out var sinceDate))
            {
                existing.RemoveAll(sc => sc.PlayedAt >= sinceDate);
                fetchAfter = sinceDate;
                Telemetry.Info("Force resync from {Date}", sinceDate.ToString("yyyy-MM-dd HH:mm"));
            }
            else
            {
                Telemetry.Error(
                    "Invalid --since format: {Value}. Use ISO 8601 (e.g., 2024-01-01)",
                    sinceStr
                );
                return 1;
            }
        }
        else if (existing.Count > 0)
        {
            fetchAfter = existing[0].PlayedAt;
            Telemetry.Info(
                "Incremental sync after {Date}",
                fetchAfter.Value.ToString("yyyy-MM-dd HH:mm")
            );
        }

        var newScrobbles = await service.FetchRecentTracksAsync(
            fetchAfter,
            (page, count) => Telemetry.Info("Page {Page}: {Count} tracks", page, count),
            ct
        );

        if (newScrobbles.Count == 0)
        {
            Telemetry.Info("No new scrobbles found.");
            return 0;
        }

        var merged = LastFmState.MergeScrobbles(existing, newScrobbles);

        await LastFmState.SaveScrobblesAsync(StateDir, merged);

        Telemetry.Info(
            "Sync complete. {Total} total scrobbles ({New} new)",
            merged.Count,
            newScrobbles.Count
        );
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description(
            "Force resync from date (ISO 8601, e.g. 2024-01-01). Deletes existing data on/after this date."
        )]
        [CommandOption("--since")]
        public string? Since { get; init; }
    }
}
