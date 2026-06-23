using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Services.Google;

public class YoutubeService(YouTubeService yt)
{
    /// <summary>Returns all playlists owned by the authenticated user.</summary>
    public async Task<IList<Playlist>> GetPlaylistsAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylists");

        var request = yt.Playlists.List("snippet");
        request.Mine = true;
        request.MaxResults = 50;

        var playlists = new List<Playlist>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            playlists.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylists returned {Count} playlists", playlists.Count);
        return playlists;
    }

    /// <summary>Returns all items in a playlist, handling pagination.</summary>
    public async Task<IList<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistItems");

        var request = yt.PlaylistItems.List("snippet");
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var items = new List<PlaylistItem>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            items.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistItems returned {Count} items for playlist {Id}", items.Count, playlistId);
        return items;
    }

    /// <summary>Updates the position of a single playlist item. Quota cost: 50 units.</summary>
    public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
    {
        item.Snippet.Position = position;
        var request = yt.PlaylistItems.Update(item, "snippet");
        await request.ExecuteAsync(ct);
    }

    /// <summary>
    /// Sorts all items in a playlist alphabetically by video title (case-insensitive).
    /// Quota cost: 50 units × item count. Log item count before running on large playlists.
    /// </summary>
    public async Task SortPlaylistAlphaAsync(string playlistId, CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.SortPlaylistAlpha");

        var items = await GetPlaylistItemsAsync(playlistId, ct);

        Telemetry.Info(
            "YouTube.SortPlaylistAlpha sorting {Count} items (quota cost: {Cost} units)",
            items.Count,
            items.Count * 50);

        var sorted = items
            .OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
            await UpdateItemPositionAsync(sorted[i], i, ct);

        activity.Complete();
        Telemetry.Info("YouTube.SortPlaylistAlpha complete — {Count} items repositioned", sorted.Count);
    }
}
