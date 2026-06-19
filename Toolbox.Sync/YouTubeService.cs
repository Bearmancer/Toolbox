using System.Net.Http.Json;
using System.Text.Json;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Sync;

public static class YouTubeService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<YouTubePlaylist>> GetPlaylistsAsync(
        string apiKey,
        string channelId,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Sync);
        using var op = Log.BeginOperation("YouTube.GetPlaylists");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(channelId))
        {
            Log.Emit(new ErrorOccurred("YouTube API key and channel ID are required", "YouTube.GetPlaylists"));
            return [];
        }

        using var client = new HttpClient { BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/") };

        Log.Emit(new ApiRequested("YouTube", "GetPlaylists", channelId));
        var startTime = DateTime.UtcNow;

        var url = $"playlists?part=snippet,contentDetails&channelId={channelId}&maxResults=50&key={apiKey}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<YouTubePlaylistResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("YouTube", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var playlists =
            result
                ?.Items?.Select(p => new YouTubePlaylist(
                    p.Id,
                    p.Snippet?.Title ?? "",
                    p.Snippet?.Description,
                    p.ContentDetails?.ItemCount ?? 0,
                    p.Snippet?.ChannelId,
                    p.Snippet?.ChannelTitle
                ))
                .ToList()
            ?? [];

        op.Complete();
        return playlists;
    }

    public static async Task<List<YouTubeVideo>> GetPlaylistVideosAsync(
        string apiKey,
        string playlistId,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Sync);
        using var op = Log.BeginOperation("YouTube.GetPlaylistVideos");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(playlistId))
        {
            Log.Emit(new ErrorOccurred("YouTube API key and playlist ID are required", "YouTube.GetPlaylistVideos"));
            return [];
        }

        using var client = new HttpClient { BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/") };

        Log.Emit(new ApiRequested("YouTube", "GetPlaylistVideos", playlistId));
        var startTime = DateTime.UtcNow;

        var url = $"playlistItems?part=snippet,contentDetails&playlistId={playlistId}&maxResults=50&key={apiKey}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<YouTubePlaylistItemResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("YouTube", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var videos =
            result
                ?.Items?.Select(i => new YouTubeVideo(
                    i.ContentDetails?.VideoId ?? "",
                    i.Snippet?.Title ?? "",
                    i.Snippet?.Description,
                    i.Snippet?.ChannelId,
                    i.Snippet?.ChannelTitle,
                    null,
                    i.Snippet?.PublishedAt
                ))
                .ToList()
            ?? [];

        op.Complete();
        return videos;
    }

    private record YouTubePlaylistResponse(IReadOnlyList<YouTubePlaylistItem>? Items);

    private record YouTubePlaylistItem(
        string Id,
        YouTubePlaylistSnippet? Snippet,
        YouTubePlaylistContentDetails? ContentDetails
    );

    private record YouTubePlaylistSnippet(
        string Title,
        string? Description,
        string? ChannelId,
        string? ChannelTitle
    );

    private record YouTubePlaylistContentDetails(int ItemCount);

    private record YouTubePlaylistItemResponse(IReadOnlyList<YouTubePlaylistItemDetail>? Items);

    private record YouTubePlaylistItemDetail(
        string Id,
        YouTubePlaylistItemSnippet? Snippet,
        YouTubePlaylistItemContentDetails? ContentDetails
    );

    private record YouTubePlaylistItemSnippet(
        string Title,
        string? Description,
        string? ChannelId,
        string? ChannelTitle,
        DateTimeOffset? PublishedAt
    );

    private record YouTubePlaylistItemContentDetails(string? VideoId);
}