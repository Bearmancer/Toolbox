using System.ComponentModel;
using Core;
using Services.Google;
using Services.Google.Models;
using Spectre.Console.Cli;

namespace CLI.Sync.YouTube;

[Description(
    "Sync YouTube playlist metadata to local JSON files then optionally sort "
    + "all items alphabetically by title. Syncing downloads video titles, "
    + "durations, channel info, and auto-translated titles. "
    + "Change detection uses YouTube ETags to avoid re-fetching unchanged playlists. "
    + "Sorting uses differential repositioning — only items whose position changed "
    + "are sent to the API, minimizing quota usage. "
    + "The stored ETag is updated after each sync so subsequent runs skip "
    + "playlists that haven't changed on YouTube."
)]
public class SyncYoutubeCommand(
    YouTubePlaylistOrchestrator orchestrator,
    YoutubeService youtubeService
) : AsyncCommand<SyncYoutubeCommand.Settings>
{
    private static readonly string ManifestFile = Path.Combine(
        PathResolver.RepoRoot,
        "state",
        "youtube",
        "manifest.json"
    );

    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        IReadOnlyList<string> syncedIds;

        if (!string.IsNullOrEmpty(s.Playlist))
        {
            var id = await orchestrator.ExecuteForPlaylistTitleAsync(s.Playlist, ct);
            syncedIds = id is { } ? [id] : [];
        }
        else
            syncedIds = await orchestrator.ExecuteAsync(ct);

        if (!s.NoSort && syncedIds.Count > 0)
        {
            Telemetry.Info("Sorting {Count} playlist(s) after sync", syncedIds.Count);

            var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
            var anySorted = false;

            foreach (var playlistId in syncedIds)
            {
                ct.ThrowIfCancellationRequested();

                if (!stored.PlaylistSnapshots.TryGetValue(playlistId, out var snapshot))
                    continue;

                var (repositioned, newETag) = await youtubeService.SortPlaylistAsync(
                    playlistId,
                    ct
                );

                if (!string.IsNullOrEmpty(newETag))
                {
                    stored.PlaylistSnapshots[playlistId] = snapshot with { ETag = newETag };
                    anySorted = true;
                }

                Telemetry.Info(
                    "  {Title}: {Repositioned} items repositioned",
                    snapshot.Title,
                    repositioned
                );
            }

            if (anySorted)
                await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);
        }

        Telemetry.Info("Sync complete.");
        return 0;
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
            + "Sorting only sends API calls for items whose position actually changed "
            + "(differential repositioning)."
        )]
        [CommandOption("--no-sort")]
        public bool NoSort { get; init; }
    }
}
