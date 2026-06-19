using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Music;

public static class MusicBrainzService
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("https://musicbrainz.org/ws/2/") };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<SearchResult>> SearchReleasesAsync(
        string? artist = null,
        string? release = null,
        int? year = null,
        int maxResults = 25,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Music);
        using var op = Log.BeginOperation("MusicBrainz.SearchReleases");

        var query = BuildQuery(artist, release, year);
        if (string.IsNullOrEmpty(query))
        {
            op.Complete();
            return [];
        }

        Log.Emit(new ApiRequested("MusicBrainz", "SearchReleases", query));
        var startTime = DateTime.UtcNow;

        var url = $"release/?query={Uri.EscapeDataString(query)}&limit={maxResults}&fmt=json";
        var response = await Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MusicBrainzSearchResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("MusicBrainz", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var searchResults =
            result
                ?.Releases?.Select(r => new SearchResult(
                    MusicSource.MusicBrainz,
                    r.Id,
                    r.Title,
                    r.ArtistCredit is [var firstCredit, ..] ? firstCredit?.Name?.Name : null,
                    r.Date?.Split('-').FirstOrDefault() is string y
                    && int.TryParse(y, out var yearVal)
                        ? yearVal
                        : null,
                    r.Media is [var firstMedia, ..] ? firstMedia?.Format : null,
                    r.LabelInfo is [var firstLabel, ..] ? firstLabel?.Label?.Name : null,
                    r.ReleaseGroup?.PrimaryType,
                    r.Score,
                    r.Country,
                    r.LabelInfo is [var firstLabelInfo, ..] ? firstLabelInfo?.CatalogNumber : null,
                    r.Status,
                    r.Disambiguation,
                    r.Tags?.Select(t => t.Name).ToList() ?? []
                ))
                .ToList()
            ?? [];

        op.Complete();
        return searchResults;
    }

    public static async Task<ReleaseData?> GetReleaseAsync(
        string releaseId,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Music);
        using var op = Log.BeginOperation("MusicBrainz.GetRelease");

        Log.Emit(new ApiRequested("MusicBrainz", "GetRelease", releaseId));
        var startTime = DateTime.UtcNow;

        var url = $"release/{releaseId}?inc=artist-credits+recordings+labels+tags+genres&fmt=json";
        var response = await Client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            op.Fail();
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MusicBrainzReleaseResponse>(JsonOptions, ct);
        Log.Emit(new ApiResponded("MusicBrainz", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

        if (result is null)
        {
            op.Fail();
            return null;
        }

        var tracks =
            result
                .Media?.SelectMany((m, discIndex) =>
                    m.Tracks?.Select((t, trackIndex) =>
                        new TrackInfo(
                            discIndex + 1,
                            trackIndex + 1,
                            t.Title,
                            t.Length > 0
                                ? TimeSpan.FromMilliseconds(t.Length)
                                : null,
                            null,
                            result.ArtistCredit is [var credit, ..] ? credit?.Name?.Name : null,
                            null,
                            t.Recording?.Id
                        )
                    )
                    ?? []
                )
                .ToList()
            ?? [];

        var totalDuration = tracks
            .Where(t => t.Duration.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration!.Value);

        var first = result.LabelInfo is [var labelInfo, ..] ? labelInfo : null;
        var info = new ReleaseInfo(
            MusicSource.MusicBrainz,
            result.Id,
            result.Title,
            result.ArtistCredit is [var firstCredit, ..] ? firstCredit?.Name?.Name : null,
            first?.Label?.Name,
            result.LabelInfo is [var firstLabel, ..] ? firstLabel?.CatalogNumber : null,
            result.Date?.Split('-').FirstOrDefault() is { } y
            && int.TryParse(y, out var year)
                ? year
                : null,
            result.Annotation,
            result.Media?.Count ?? 0,
            tracks.Count,
            totalDuration
        );

        op.Complete();
        return new ReleaseData(info, tracks);
    }

    private static string BuildQuery(string? artist, string? release, int? year)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artist))
            parts.Add($"artist:\"{artist}\"");
        if (!string.IsNullOrWhiteSpace(release))
            parts.Add($"release:\"{release}\"");
        if (year.HasValue)
            parts.Add($"date:{year}");
        return string.Join(" AND ", parts);
    }

    private record MusicBrainzSearchResponse(IReadOnlyList<MusicBrainzRelease>? Releases);

    private record MusicBrainzRelease(
        string Id,
        string Title,
        IReadOnlyList<MusicBrainzArtistCredit>? ArtistCredit,
        IReadOnlyList<MusicBrainzLabelInfo>? LabelInfo,
        string? Date,
        string? Annotation,
        IReadOnlyList<MusicBrainzMedium>? Media,
        MusicBrainzReleaseGroup? ReleaseGroup,
        int? Score,
        string? Country,
        string? Status,
        string? Disambiguation,
        IReadOnlyList<MusicBrainzTag>? Tags
    );

    private record MusicBrainzReleaseResponse(
        string Id,
        string Title,
        IReadOnlyList<MusicBrainzArtistCredit>? ArtistCredit,
        IReadOnlyList<MusicBrainzLabelInfo>? LabelInfo,
        string? Date,
        string? Annotation,
        IReadOnlyList<MusicBrainzMedium>? Media,
        MusicBrainzReleaseGroup? ReleaseGroup,
        int? Score,
        string? Country,
        string? Status,
        string? Disambiguation,
        IReadOnlyList<MusicBrainzTag>? Tags
    );

    private record MusicBrainzArtistCredit(MusicBrainzName? Name);

    private record MusicBrainzName(string Name);

    private record MusicBrainzLabelInfo(MusicBrainzLabel? Label, string? CatalogNumber);

    private record MusicBrainzLabel(string Name);

    private record MusicBrainzMedium(IReadOnlyList<MusicBrainzTrack>? Tracks, string? Format);

    private record MusicBrainzTrack(string Title, long Length, MusicBrainzRecording? Recording);

    private record MusicBrainzRecording(string? Id);

    private record MusicBrainzReleaseGroup(string? PrimaryType);

    private record MusicBrainzTag(string Name);
}