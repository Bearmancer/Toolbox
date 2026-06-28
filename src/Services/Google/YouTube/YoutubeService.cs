using System.Xml;
using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Services.Google.YouTube.Models;
using GoogleRequests = Google.Apis.Requests;

namespace Services.Google.YouTube;

public class YoutubeService(YouTubeService yt, YouTubeQuotaTracker quota)
{
    public int QuotaUsed => quota.QuotaUsedToday;

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
            ct.ThrowIfCancellationRequested();

            var check = quota.CanMakeCall(estimatedCost: 1);
            if (!check.Allowed)
                throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall("Playlists.List", 1, $"mine=true parts={parts}");
            playlists.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylists returned {Count} playlists (quota: {Quota}/{Limit})",
            playlists.Count,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
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
            ct.ThrowIfCancellationRequested();

            var check = quota.CanMakeCall(estimatedCost: 1);
            if (!check.Allowed)
                throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall("PlaylistItems.List", 1, $"playlist={playlistId}");
            items.AddRange(response.Items ?? []);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistItems returned {Count} items for playlist {Id} (quota: {Quota}/{Limit})",
            items.Count,
            playlistId,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
        );
        return items;
    }

    public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
    {
        var check = quota.CanMakeCall(estimatedCost: 50);
        if (!check.Allowed)
            throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

        item.Snippet.Position = position;
        var request = yt.PlaylistItems.Update(item, "snippet");
        await request.ExecuteAsync(cancellationToken: ct);
        quota.RecordCall(
            "PlaylistItems.Update",
            50,
            $"position={position} video={item.Snippet.Title}"
        );
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
            ct.ThrowIfCancellationRequested();

            var check = quota.CanMakeCall(estimatedCost: 1);
            if (!check.Allowed)
                throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall("PlaylistItems.List", 1, $"playlist={playlistId} parts={parts}");
            pages.Add(item: response);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetPlaylistItemPagesRaw returned {Count} pages for {Id} (quota: {Quota}/{Limit})",
            pages.Count,
            playlistId,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
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
            ct.ThrowIfCancellationRequested();

            var check = quota.CanMakeCall(estimatedCost: 1);
            if (!check.Allowed)
                throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

            var request = yt.Videos.List(part: "contentDetails");
            request.Id = string.Join(",", batch);
            var response = await request.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall(
                "Videos.List",
                1,
                $"batch={batch.Length} ids={string.Join(",", batch.Take(3))}..."
            );

            foreach (var video in response.Items ?? [])
            {
                var duration = ParseIso8601Duration(iso: video.ContentDetails?.Duration);
                result[key: video.Id] = duration;
            }
        }

        activity.Complete();
        Telemetry.Debug(
            "YouTube.GetVideoDurations fetched {Count} durations (quota: {Quota}/{Limit})",
            result.Count,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
        );
        return result;
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
            ct.ThrowIfCancellationRequested();

            var check = quota.CanMakeCall(estimatedCost: 1);
            if (!check.Allowed)
                throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall("Playlists.List", 1, "mine=true parts=snippet,contentDetails");

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
            "YouTube.GetPlaylistSummaries returned {Count} playlists (quota: {Quota}/{Limit})",
            snapshots.Count,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
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

        var check = quota.CanMakeCall(estimatedCost: 1);
        if (!check.Allowed)
            throw new QuotaExceededException(check.Reason ?? "Daily quota limit reached");

        var request = yt.Playlists.List(part: "snippet,contentDetails");
        request.Id = playlistId;
        var response = await request.ExecuteAsync(cancellationToken: ct);
        quota.RecordCall("Playlists.List", 1, $"id={playlistId}");

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
                    "YouTube.GetPlaylistSummary: Playlist {Id} missing publishedAt — skipping",
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

        var sorted = items.OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase).ToList();

        var toUpdate = new List<PlaylistItem>();
        for (var i = 0; i < sorted.Count; i++)
            if (sorted[index: i].Snippet.Position != i)
                toUpdate.Add(sorted[index: i]);

        Telemetry.Info(
            "YouTube.SortPlaylist: {Total} items, {Delta} need repositioning (quota: {Quota}/{Limit})",
            items.Count,
            toUpdate.Count,
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
        );

        if (toUpdate.Count == 0)
        {
            activity.Complete();
            Telemetry.Info(
                template: "YouTube.SortPlaylist: already sorted — 0 repositioned, ETag unchanged"
            );
            return (0, "");
        }

        var updateCost = toUpdate.Count * 50;
        var updateCheck = quota.CanMakeCall(updateCost);
        if (!updateCheck.Allowed)
        {
            Telemetry.Warn(
                "YouTube.SortPlaylist: BLOCKED — would need {Cost} quota units for {Count} updates, only {Remaining} remaining",
                updateCost,
                toUpdate.Count,
                updateCheck.Remaining
            );
            throw new QuotaExceededException(
                $"Sort requires {updateCost} units ({toUpdate.Count} updates × 50), only {updateCheck.Remaining} remaining"
            );
        }

        var batchFailures = new List<(int Position, string ErrorMessage)>();
        const int batchSize = 25;
        var batches = toUpdate
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        Telemetry.Info(
            "YouTube.SortPlaylist: splitting {Total} updates into {BatchCount} batches of {BatchSize}",
            toUpdate.Count,
            batches.Count,
            batchSize
        );

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = new GoogleRequests.BatchRequest(service: yt);
            var batchItems = batches[batchIndex];

            foreach (var item in batchItems)
            {
                var position = sorted.IndexOf(item);
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

            Telemetry.Debug(
                "YouTube.SortPlaylist: executing batch {Index}/{Total} ({Count} items)",
                batchIndex + 1,
                batches.Count,
                batchItems.Count
            );

            await batch.ExecuteAsync(cancellationToken: ct);
            quota.RecordCall(
                "PlaylistItems.Update.Batch",
                batchItems.Count * 50,
                $"batch={batchIndex + 1}/{batches.Count} count={batchItems.Count}"
            );

            if (batchIndex < batches.Count - 1)
            {
                Telemetry.Debug("YouTube.SortPlaylist: waiting 2s before next batch...");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        if (batchFailures.Count > 0)
        {
            Telemetry.Error(
                "YouTube.SortPlaylist: {Failed}/{Total} batch updates FAILED",
                batchFailures.Count,
                toUpdate.Count
            );
            foreach (var (idx, msg) in batchFailures)
                Telemetry.Error("Batch failure at position {Position}: {Error}", idx, msg);
        }

        var summary = await GetPlaylistSummaryAsync(playlistId, ct);

        activity.Complete();
        Telemetry.Info(
            "YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag} (quota: {Quota}/{Limit})",
            toUpdate.Count,
            summary?.ETag ?? "unknown",
            quota.QuotaUsedToday,
            quota.DailyLimitDisplay
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

public sealed class QuotaExceededException(string message) : Exception(message);
