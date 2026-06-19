using System.Net.Http.Json;
using System.Text.Json;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Sync;

public static class LastFmService
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<LastFmScrobble>> GetRecentTracksAsync(
        string apiKey,
        string username,
        int page = 1,
        int limit = 200,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Sync);
        using var op = Log.BeginOperation("LastFm.GetRecentTracks");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(username))
        {
            Log.Emit(new ErrorOccurred("Last.fm API key and username are required", "LastFm.GetRecentTracks"));
            return [];
        }

        using var client = new HttpClient { BaseAddress = new Uri("https://ws.audioscrobbler.com/2.0/") };

        Log.Emit(new ApiRequested("LastFm", "GetRecentTracks", username));
        var startTime = DateTime.UtcNow;

        var url = $"?method=user.getrecenttracks&user={username}&api_key={apiKey}&format=json&page={page}&limit={limit}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LastFmRecentTracksResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("LastFm", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var scrobbles =
            result
                ?.RecentTracks?.Track?.Where(t => t.Date is not null)
                .Select(t => new LastFmScrobble(
                    t.Artist?.Text ?? "",
                    t.Name ?? "",
                    t.Album?.Text,
                    t.Date?.Uts is not null
                        ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(t.Date.Uts))
                        : null,
                    t.Mbid
                ))
                .ToList()
            ?? [];

        op.Complete();
        return scrobbles;
    }

    public static async Task<SyncResult> SyncScrobblesAsync(
        string apiKey,
        string username,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Sync);
        using var op = Log.BeginOperation("LastFm.SyncScrobbles");

        var allScrobbles = new List<LastFmScrobble>();
        var page = 1;
        var hasMore = true;

        while (hasMore && !ct.IsCancellationRequested)
        {
            var scrobbles = await GetRecentTracksAsync(apiKey, username, page, 200, ct);
            if (scrobbles.Count == 0)
            {
                hasMore = false;
            }
            else
            {
                allScrobbles.AddRange(scrobbles);
                page++;
                hasMore = scrobbles.Count == 200;
            }
        }

        var oldest = allScrobbles
            .Where(s => s.PlayedAt.HasValue)
            .MinBy(s => s.PlayedAt)
            ?.PlayedAt;
        var newest = allScrobbles
            .Where(s => s.PlayedAt.HasValue)
            .MaxBy(s => s.PlayedAt)
            ?.PlayedAt;

        var result = new SyncResult(
            allScrobbles.Count,
            allScrobbles.Count,
            0,
            oldest,
            newest,
            TimeSpan.Zero
        );

        op.Complete();
        return result;
    }

    record LastFmRecentTracksResponse(LastFmRecentTracks? RecentTracks);

    record LastFmRecentTracks(IReadOnlyList<LastFmTrack>? Track);

    record LastFmTrack(
        string? Name,
        LastFmArtist? Artist,
        LastFmAlbum? Album,
        string? Mbid,
        LastFmDate? Date
    );

    record LastFmArtist(string? Text);

    record LastFmAlbum(string? Text);

    record LastFmDate(string? Uts);
}