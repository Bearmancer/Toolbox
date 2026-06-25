using System.Xml;
using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Services.Google.Models;
using GoogleRequests = Google.Apis.Requests;

namespace Services.Google;

public class YoutubeService(YouTubeService yt)
{
    public int QuotaUsed { get; private set; }

    public async Task<IList<Playlist>> GetPlaylistsAsync(
        CancellationToken ct,
        string parts = "snippet"
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylists");

        var request = yt.Playlists.List(part: parts);
        request.Mine = true;
        request.MaxResults = 50;

        var playlists = new List<Playlist>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;
            playlists.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylists returned {Count} playlists",
            playlists.Count
        );
        return playlists;
    }

    public async Task<IList<PlaylistItem>> GetPlaylistItemsAsync(
        string playlistId,
        CancellationToken ct,
        string parts = "snippet"
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistItems");

        var request = yt.PlaylistItems.List(part: parts);
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var items = new List<PlaylistItem>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;
            items.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistItems returned {Count} items for playlist {Id}",
            items.Count,
            playlistId
        );
        return items;
    }

    public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
    {
        item.Snippet.Position = position;
        var request = yt.PlaylistItems.Update(item, "snippet");
        await request.ExecuteAsync(cancellationToken: ct);
        QuotaUsed++;
    }

    public async Task<IReadOnlyList<PlaylistItemListResponse>> GetPlaylistItemPagesRawAsync(
        string playlistId,
        string parts,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(
            messageTemplate: "YouTube.GetPlaylistItemPagesRaw"
        );

        var request = yt.PlaylistItems.List(part: parts);
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var pages = new List<PlaylistItemListResponse>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;
            pages.Add(item: response);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistItemPagesRaw returned {Count} pages for {Id}",
            pages.Count,
            playlistId
        );
        return pages;
    }

    public async Task<Dictionary<string, TimeSpan>> GetVideoDurationsAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetVideoDurations");

        if (videoIds.Count == 0)
            return [];

        var result = new Dictionary<string, TimeSpan>();

        foreach (var batch in videoIds.Chunk(size: 50))
        {
            var request = yt.Videos.List(part: "contentDetails");
            request.Id = string.Join(",", batch);
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;

            foreach (var video in response.Items ?? [])
            {
                var duration = ParseIso8601Duration(iso: video.ContentDetails?.Duration);
                result[key: video.Id] = duration;
            }
        }

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetVideoDurations fetched {Count} durations",
            result.Count
        );
        return result;
    }

    public async Task<IReadOnlyList<string>> GetPlaylistIdsAsync(
        string playlistId,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistIds");

        var request = yt.PlaylistItems.List(part: "id");
        request.PlaylistId = playlistId;
        request.MaxResults = 50;

        var ids = new List<string>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;
            foreach (var item in response.Items ?? [])
                if (item.ContentDetails?.VideoId is { } vid)
                    ids.Add(item: vid);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistIds returned {Count} IDs for {Id}",
            ids.Count,
            playlistId
        );
        return ids;
    }

    public async Task<IReadOnlyList<PlaylistSnapshot>> GetPlaylistSummariesAsync(
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(
            messageTemplate: "YouTube.GetPlaylistSummaries"
        );

        var request = yt.Playlists.List(part: "snippet,contentDetails");
        request.Mine = true;
        request.MaxResults = 50;

        var snapshots = new List<PlaylistSnapshot>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            QuotaUsed++;

            foreach (var playlist in response.Items ?? [])
            {
                var publishedAt = DateTimeOffset.Parse(playlist.Snippet!.PublishedAtRaw!);

                snapshots.Add(
                    new PlaylistSnapshot
                    {
                        PlaylistId = playlist.Id!,
                        Title = playlist.Snippet!.Title!,
                        LastUpdated = publishedAt,
                        LastChecked = DateTimeOffset.UtcNow,
                        ETag = playlist.ETag!,
                        ReportedVideoCount = playlist.ContentDetails!.ItemCount!.Value,
                    }
                );
            }

            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistSummaries returned {Count} playlists",
            snapshots.Count
        );
        return snapshots;
    }

    public async Task<PlaylistSnapshot?> GetPlaylistSummaryAsync(
        string playlistId,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistSummary");

        var request = yt.Playlists.List(part: "snippet,contentDetails");
        request.Id = playlistId;
        var response = await request.ExecuteAsync(cancellationToken: ct);
        QuotaUsed++;

        var playlist = response.Items?.FirstOrDefault();
        if (playlist is null)
        {
            activity.Complete();
            return null;
        }

        try
        {
            var publishedAt = playlist.Snippet?.PublishedAtDateTimeOffset;
            if (publishedAt is null)
            {
                Telemetry.Warn(
                    "YouTube.GetPlaylistSummary: playlist {Id} missing publishedAt — skipping",
                    playlistId
                );
                activity.Complete();
                return null;
            }

            activity.Complete();
            return new PlaylistSnapshot
            {
                PlaylistId = playlist.Id!,
                Title = playlist.Snippet!.Title!,
                LastUpdated = publishedAt.Value,
                LastChecked = DateTimeOffset.UtcNow,
                ETag = playlist.ETag!,
                ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
            };
        }
        catch (FormatException ex)
        {
            Telemetry.Error(
                "YouTube.GetPlaylistSummary: playlist {Id} has invalid publishedAt — skipping: {Error}",
                playlistId,
                ex.Message
            );
            activity.Complete();
            return null;
        }
    }

    public async Task<(int Repositioned, string NewETag)> SortPlaylistAsync(
        string playlistId,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(service: "Google");
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.SortPlaylist");

        var items = await GetPlaylistItemsAsync(playlistId, ct);

        var sorted = items
            .OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toUpdate = new List<PlaylistItem>();
        for (var i = 0; i < sorted.Count; i++)
            if (sorted[index: i].Snippet.Position != i)
                toUpdate.Add(sorted[index: i]);

        Telemetry.Info(
            "YouTube.SortPlaylist: {Total} items, {Delta} need repositioning",
            items.Count,
            toUpdate.Count
        );

        if (toUpdate.Count == 0)
        {
            activity.Complete();
            Telemetry.Info(
                template: "YouTube.SortPlaylist: already sorted — 0 repositioned, ETag unchanged"
            );
            return (0, "");
        }

        var batchFailures = new List<(int Position, string ErrorMessage)>();
        var batch = new GoogleRequests.BatchRequest(service: yt);
        foreach (var (position, item) in sorted.Index())
        {
            if (item.Snippet.Position == position)
                continue;

            item.Snippet.Position = position;
            var request = yt.PlaylistItems.Update(item, "snippet");
            var pos = position;
            batch.Queue<PlaylistItem>(
                request,
                (_, error, _, _) =>
                {
                    if (error is { })
                        batchFailures.Add((pos, error.Message ?? "unknown error"));
                }
            );
        }

        await batch.ExecuteAsync(cancellationToken: ct);
        QuotaUsed += toUpdate.Count;

        if (batchFailures.Count > 0)
        {
            Telemetry.Error(
                "YouTube.SortPlaylist: {Failed}/{Total} batch updates FAILED",
                batchFailures.Count,
                toUpdate.Count
            );
            foreach (var (idx, msg) in batchFailures)
                Telemetry.Error(
                    "  batch failure at position {Position}: {Error}",
                    idx,
                    msg
                );
        }

        var summary = await GetPlaylistSummaryAsync(playlistId, ct);

        activity.Complete();
        Telemetry.Info(
            "YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag}",
            toUpdate.Count,
            summary?.ETag ?? "unknown"
        );

        return (toUpdate.Count, summary?.ETag ?? "");
    }

    private static TimeSpan ParseIso8601Duration(string? iso)
    {
        if (string.IsNullOrEmpty(value: iso))
            throw new FormatException(message: "Duration is null or empty");

        return XmlConvert.ToTimeSpan(s: iso);
    }
}
