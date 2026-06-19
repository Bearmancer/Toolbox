using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Music;

public static class DiscogsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<SearchResult>> SearchAsync(
        string query,
        string? token = null,
        int maxResults = 50,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Music);
        using var op = Log.BeginOperation("Discogs.Search");

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Emit(new ErrorOccurred("Discogs token is required", "Discogs.Search"));
            return [];
        }

        using var client = CreateClient(token);

        Log.Emit(new ApiRequested("Discogs", "Search", query));
        var startTime = DateTime.UtcNow;

        var url = $"database/search?q={Uri.EscapeDataString(query)}&per_page={maxResults}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DiscogsSearchResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("Discogs", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var searchResults =
            result
                ?.Results?.Select(r => new SearchResult(
                    MusicSource.Discogs,
                    (r.Id > 0 ? r.Id : r.MasterId).ToString(),
                    r.Title,
                    ExtractArtist(r.Title),
                    r.Year,
                    r.Format is not null ? string.Join(", ", r.Format) : null,
                    r.Label?.FirstOrDefault(),
                    r.Type,
                    null,
                    r.Country,
                    r.Catno,
                    null,
                    null,
                    r.Genre?.ToList() ?? [],
                    r.Style?.ToList() ?? []
                ))
                .ToList()
            ?? [];

        op.Complete();
        return searchResults;
    }

    public static async Task<ReleaseData?> GetReleaseAsync(
        string releaseId,
        string? token = null,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Music);
        using var op = Log.BeginOperation("Discogs.GetRelease");

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Emit(new ErrorOccurred("Discogs token is required", "Discogs.GetRelease"));
            return null;
        }

        if (!int.TryParse(releaseId, out var id))
        {
            Log.Emit(new ErrorOccurred("Invalid release ID", "Discogs.GetRelease"));
            return null;
        }

        using var client = CreateClient(token);

        Log.Emit(new ApiRequested("Discogs", "GetRelease", releaseId));
        var startTime = DateTime.UtcNow;

        var response = await client.GetAsync($"releases/{id}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            op.Fail();
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DiscogsReleaseResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("Discogs", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        if (result is null)
        {
            op.Fail();
            return null;
        }

        var tracks = BuildTracks(result);
        var totalDuration = tracks
            .Where(t => t.Duration.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration!.Value);

        var info = new ReleaseInfo(
            MusicSource.Discogs,
            releaseId,
            result.Title,
            result.Artists is [var firstArtist, ..] ? firstArtist?.Name : null,
            result.Labels is [var firstLabel, ..] ? firstLabel?.Name : null,
            result.Labels is [var firstLabel2, ..] ? firstLabel2?.Catno : null,
            result.Year,
            result.Notes,
            GetDiscCount(result.Tracklist),
            tracks.Count,
            totalDuration
        );

        op.Complete();
        return new ReleaseData(info, tracks);
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.discogs.com/") };
        client.DefaultRequestHeaders.Add("Authorization", $"Discogs token={token}");
        client.DefaultRequestHeaders.Add("User-Agent", "AzureAIConsole/1.0");
        return client;
    }

    private static string? ExtractArtist(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;
        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        return dashIndex > 0 ? title[..dashIndex] : null;
    }

    private static int? ParseYear(string? year) => int.TryParse(year, out var y) ? y : null;

    private static List<TrackInfo> BuildTracks(DiscogsReleaseResponse release)
    {
        var tracks = new List<TrackInfo>();
        var discNum = 1;
        var trackNum = 0;

        foreach (var track in release.Tracklist ?? [])
        {
            if (
                track.Position?.StartsWith($"{discNum + 1}-", StringComparison.Ordinal) == true
                || (discNum == 1 && track.Position?.StartsWith("1-", StringComparison.Ordinal) == true && trackNum > 0)
            )
            {
                discNum++;
                trackNum = 0;
            }

            trackNum++;

            tracks.Add(
                new TrackInfo(
                    discNum,
                    trackNum,
                    track.Title,
                    ParseDuration(track.Duration),
                    null,
                    ExtractExtraArtist(release.Extraartists, "Composed By"),
                    null,
                    ExtractExtraArtist(release.Extraartists, "Conductor"),
                    ExtractExtraArtist(release.Extraartists, "Orchestra"),
                    ExtractExtraArtists(release.Extraartists, ["Soloist", "Performer"]),
                    release.Artists is [var releaseArtist, ..] ? releaseArtist?.Name : null
                )
            );
        }

        return tracks;
    }

    private static int GetDiscCount(IReadOnlyList<DiscogsTrack>? tracklist)
    {
        if (tracklist is null || tracklist.Count == 0)
            return 0;
        var maxDisc = 1;
        foreach (var track in tracklist)
            if (track.Position?.Contains('-') == true)
            {
                var discPart = track.Position.Split('-')[0];
                if (int.TryParse(discPart, out var disc) && disc > maxDisc)
                    maxDisc = disc;
            }

        return maxDisc;
    }

    private static string? ExtractExtraArtist(IReadOnlyList<DiscogsArtist>? artists, string role)
    {
        return artists
            ?.FirstOrDefault(a =>
                a.Role?.Contains(role, StringComparison.OrdinalIgnoreCase) == true
            )
            ?.Name;
    }

    private static List<string>? ExtractExtraArtists(
        IReadOnlyList<DiscogsArtist>? artists,
        string[] roles
    )
    {
        return artists
            ?.Where(a =>
                roles.Any(r => a.Role?.Contains(r, StringComparison.OrdinalIgnoreCase) == true)
            )
            .Select(a => a.Name)
            .Distinct()
            .ToList();
    }

    private static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration))
            return null;

        var parts = duration.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], out var minutes)
            && int.TryParse(parts[1], out var seconds)
            ? TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds)
            : null;
    }

    private record DiscogsSearchResponse(IReadOnlyList<DiscogsSearchResult>? Results);

    private record DiscogsSearchResult(
        int Id,
        int MasterId,
        string Title,
        int? Year,
        string[]? Format,
        string[]? Label,
        string? Type,
        string? Country,
        string? Catno,
        string[]? Genre,
        string[]? Style
    );

    private record DiscogsReleaseResponse(
        string Title,
        int? Year,
        string? Notes,
        IReadOnlyList<DiscogsArtist>? Artists,
        IReadOnlyList<DiscogsArtist>? Extraartists,
        IReadOnlyList<DiscogsLabel>? Labels,
        IReadOnlyList<DiscogsTrack>? Tracklist
    );

    private record DiscogsArtist(string Name, string? Role);

    private record DiscogsLabel(string Name, string? Catno);

    private record DiscogsTrack(string Title, string? Position, string? Duration);
}