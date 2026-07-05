# Phase 4: YouTubePlaylistService — Generic Pagination

## Task 12: Extract PaginateAsync helper and refactor pagination methods

Replace entire contents of `src/Services/Google/YouTube/YouTubePlaylistService.cs` with:

```csharp
using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Services.Google.YouTube;

public class YouTubePlaylistService(YouTubeService yt)
{
    public async Task<IList<Playlist>> GetPlaylistsAsync(CancellationToken ct, string parts = "snippet")
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylists");

        var playlists = await PaginateAsync(
            async pageToken =>
            {
                var request = yt.Playlists.List(part: parts);
                request.Mine = true;
                request.MaxResults = 50;
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(cancellationToken: ct);
                return (response.Items ?? [], response.NextPageToken);
            }, ct);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylists returned {Count} playlists", playlists.Count);
        return playlists;
    }

    public async Task<IList<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, CancellationToken ct, string parts = "snippet")
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistItems");

        var items = await PaginateAsync(
            async pageToken =>
            {
                var request = yt.PlaylistItems.List(part: parts);
                request.PlaylistId = playlistId;
                request.MaxResults = 50;
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(cancellationToken: ct);
                return (response.Items ?? [], response.NextPageToken);
            }, ct);

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistItems returned {Count} items for playlist {Id}", items.Count, playlistId);
        return items;
    }

    public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
    {
        item.Snippet.Position = position;
        var request = yt.PlaylistItems.Update(item, "snippet");
        await request.ExecuteAsync(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<PlaylistItemListResponse>> GetPlaylistItemPagesRawAsync(string playlistId, string parts, CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistItemPagesRaw");

        var pages = new List<PlaylistItemListResponse>();
        string? pageToken = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var request = yt.PlaylistItems.List(part: parts);
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken: ct);
            pages.Add(response);
            pageToken = response.NextPageToken;
        } while (pageToken is { });

        activity.Complete();
        Telemetry.Debug("YouTube.GetPlaylistItemPagesRaw returned {Count} pages for {Id}", pages.Count, playlistId);
        return pages;
    }

    public async Task<IReadOnlyList<PlaylistSnapshot>> GetPlaylistSummariesAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistSummaries");

        var snapshots = await PaginateAsync(
            async pageToken =>
            {
                var request = yt.Playlists.List(part: "snippet,contentDetails");
                request.Mine = true;
                request.MaxResults = 50;
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(cancellationToken: ct);
                var items = (response.Items ?? [])
                    .Select(playlist => new PlaylistSnapshot
                    {
                        PlaylistId = playlist.Id!,
                        Title = playlist.Snippet!.Title!,
                        LastUpdated = ParsePublishedAt(playlist.Id!, playlist.Snippet?.PublishedAtRaw),
                        LastChecked = DateTimeOffset.UtcNow,
                        ETag = playlist.ETag!,
                        ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
                    });
                return (items, response.NextPageToken);
            }, ct);

        activity.Complete();
        return snapshots;
    }

    public async Task<PlaylistSnapshot?> GetPlaylistSummaryAsync(string playlistId, CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetPlaylistSummary");

        var request = yt.Playlists.List(part: "snippet,contentDetails");
        request.Id = playlistId;
        var response = await request.ExecuteAsync(cancellationToken: ct);
        var playlist = response.Items?.FirstOrDefault();
        activity.Complete();

        if (playlist is null) return null;
        return new PlaylistSnapshot
        {
            PlaylistId = playlist.Id!,
            Title = playlist.Snippet!.Title!,
            LastUpdated = ParsePublishedAt(playlistId, playlist.Snippet?.PublishedAtRaw),
            LastChecked = DateTimeOffset.UtcNow,
            ETag = playlist.ETag!,
            ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
        };
    }

    private static async Task<List<T>> PaginateAsync<T>(
        Func<string?, Task<(IList<T> Items, string? NextPageToken)>> fetchPage,
        CancellationToken ct)
    {
        var results = new List<T>();
        string? pageToken = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var (items, nextToken) = await fetchPage(pageToken);
            results.AddRange(items);
            pageToken = nextToken;
        } while (pageToken is { });
        return results;
    }

    private static DateTimeOffset ParsePublishedAt(string playlistId, string? raw)
    {
        if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;
        Telemetry.Warn("YouTube.GetPlaylistSummary: Playlist {Id} has missing or unparseable publishedAt '{Raw}' — using fallback", playlistId, raw ?? "null");
        return DateTimeOffset.UtcNow;
    }
}
```

**Key changes:**
- Added `PaginateAsync<T>` generic helper (12 lines)
- `GetPlaylistsAsync` uses `PaginateAsync` instead of manual do-while loop
- `GetPlaylistItemsAsync` uses `PaginateAsync` instead of manual do-while loop
- `GetPlaylistSummariesAsync` uses `PaginateAsync` instead of manual do-while loop
- `GetPlaylistItemPagesRawAsync` keeps manual loop (returns raw response pages, not extracted items — different contract)
- Net savings: ~40 lines of duplicated pagination logic

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Change `GetPlaylistItemPagesRawAsync` to use `PaginateAsync` — it returns raw API responses, not extracted items

**QA:**
```bash
dotnet build
```
Expected: Clean build.

**Commit:** `refactor(youtube): extract PaginateAsync<T> generic pagination helper`
