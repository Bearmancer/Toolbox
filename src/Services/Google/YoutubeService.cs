using System.Xml;
using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using GoogleRequests = Google.Apis.Requests;
using Services.Google.Models;

namespace Services.Google;

public class YoutubeService(YouTubeService yt)
{
    private static readonly string StateRoot = Path.Combine(Directory.GetCurrentDirectory(), "state", "youtube");
    private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");
    private static readonly string RawDir = Path.Combine(StateRoot, "raw");
    private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
    private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");

    public int QuotaUsed { get; private set; }

    public async Task<IList<Playlist>> GetPlaylistsAsync(CancellationToken ct, string parts = "snippet")
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylists");

        var request = yt.Playlists.List(parts);
        request.Mine = true;
        request.MaxResults = 50;

        var playlists = new List<Playlist>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;
            playlists.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylists returned {Count} playlists", playlists.Count);
        return playlists;
    }

    public async Task<IList<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, CancellationToken ct, string parts = "snippet")
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistItems");

        var request = yt.PlaylistItems.List(parts);
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var items = new List<PlaylistItem>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;
            items.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistItems returned {Count} items for playlist {Id}", items.Count, playlistId);
        return items;
    }

    public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
    {
        item.Snippet.Position = position;
        var request = yt.PlaylistItems.Update(item, "snippet");
        await request.ExecuteAsync(ct);
        QuotaUsed++;
    }

    public async Task<IReadOnlyList<PlaylistItemListResponse>> GetPlaylistItemPagesRawAsync(
        string playlistId,
        string parts,
        CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistItemPagesRaw");

        var request = yt.PlaylistItems.List(parts);
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var pages = new List<PlaylistItemListResponse>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;
            pages.Add(response);
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistItemPagesRaw returned {Count} pages for {Id}", pages.Count, playlistId);
        return pages;
    }

    public async Task<Dictionary<string, TimeSpan>> GetVideoDurationsAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetVideoDurations");

        if (videoIds.Count == 0)
            return [];

        var result = new Dictionary<string, TimeSpan>();

        foreach (var batch in videoIds.Chunk(50))
        {
            var request = yt.Videos.List("contentDetails");
            request.Id = string.Join(",", batch);
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;

            foreach (var video in response.Items ?? [])
            {
                var duration = ParseIso8601Duration(video.ContentDetails?.Duration);
                result[video.Id] = duration;
            }
        }

        activity.Complete();
        Telemetry.Debug("YouTube.GetVideoDurations fetched {Count} durations", result.Count);
        return result;
    }

    public async Task<IReadOnlyList<string>> GetPlaylistIdsAsync(
        string playlistId,
        CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistIds");

        var request = yt.PlaylistItems.List("id");
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var ids = new List<string>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;
            foreach (var item in response.Items ?? [])
            {
                if (item.ContentDetails?.VideoId is { } vid)
                    ids.Add(vid);
            }
            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistIds returned {Count} IDs for {Id}", ids.Count, playlistId);
        return ids;
    }

    public async Task<IReadOnlyList<PlaylistSnapshot>> GetPlaylistSummariesAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistSummaries");

        var request = yt.Playlists.List("snippet,contentDetails");
        request.Mine = true;
        request.MaxResults = 50;

        var snapshots = new List<PlaylistSnapshot>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);
            QuotaUsed++;

            foreach (var playlist in response.Items ?? [])
            {
                var publishedAt = DateTimeOffset.Parse(playlist.Snippet!.PublishedAtRaw!);

                snapshots.Add(new PlaylistSnapshot
                {
                    PlaylistId = playlist.Id!,
                    Title = playlist.Snippet!.Title!,
                    LastUpdated = publishedAt,
                    LastChecked = DateTimeOffset.UtcNow,
                    ETag = playlist.ETag!,
                    ReportedVideoCount = playlist.ContentDetails!.ItemCount!.Value,
                });
            }

            pageToken = response.NextPageToken;
        }
        while (pageToken is not null);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistSummaries returned {Count} playlists", snapshots.Count);
        return snapshots;
    }

    public async Task<PlaylistSnapshot?> GetPlaylistSummaryAsync(string playlistId, CancellationToken ct)
    {
        using var _ = Telemetry.ForService("YouTube");
        using var activity = Telemetry.StartActivity("YouTube.GetPlaylistSummary");

        var request = yt.Playlists.List("snippet,contentDetails");
        request.Id = playlistId;
        var response = await request.ExecuteAsync(ct);
        QuotaUsed++;

        var playlist = response.Items?.FirstOrDefault();
        if (playlist is null)
            return null;

        activity.Complete();
        return new PlaylistSnapshot
        {
            PlaylistId = playlist.Id!,
            Title = playlist.Snippet!.Title!,
            LastUpdated = playlist.Snippet?.PublishedAtDateTimeOffset
                ?? throw new InvalidOperationException("Missing publishedAt"),
            LastChecked = DateTimeOffset.UtcNow,
            ETag = playlist.ETag!,
            ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
        };
    }

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

        var batch = new GoogleRequests.BatchRequest(yt);

        foreach (var (position, item) in sorted.Index())
        {
            item.Snippet.Position = position;
            var request = yt.PlaylistItems.Update(item, "snippet");
            batch.Queue<PlaylistItem>(request, (content, error, idx, message) => { });
        }

        await batch.ExecuteAsync(ct);
        QuotaUsed += sorted.Count;

        activity.Complete();
        Telemetry.Info("YouTube.SortPlaylistAlpha complete — {Count} items repositioned", sorted.Count);
    }

    private static TimeSpan ParseIso8601Duration(string? iso)
    {
        if (string.IsNullOrEmpty(iso))
            throw new FormatException("Duration is null or empty");

        return XmlConvert.ToTimeSpan(iso);
    }
}
