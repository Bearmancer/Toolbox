using System.Net;
using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.LastFm;

public class LastFmApiClient(HttpClient httpClient, string apiKey, string username)
{
    public readonly record struct FetchPageResult(List<LastFmScrobble> Scrobbles, int TotalPages);

    private const string ApiBase = "https://ws.audioscrobbler.com/2.0/";

    private readonly string ApiKey = apiKey;
    private readonly HttpClient Client = httpClient;
    private readonly string UserName = username;

    public async Task<ErrorOr<FetchPageResult>> FetchPageCoreAsync(
        DateTimeOffset? fetchAfter,
        int page,
        int limit,
        CancellationToken ct
    )
    {
        return await BuildFetchUrl(fetchAfter, page, limit)
            .ThenAsync(url => ExecuteHttpRequestAsync(url, ct))
            .ThenAsync(json => Task.FromResult(ParseJsonResponse(json)))
            .ThenAsync(root => Task.FromResult(ExtractTracks(root)));
    }

    private ErrorOr<string> BuildFetchUrl(DateTimeOffset? fetchAfter, int page, int limit)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["method"] = "user.getrecenttracks",
            ["user"] = UserName,
            ["api_key"] = ApiKey,
            ["format"] = "json",
            ["limit"] = limit.ToString(),
            ["page"] = page.ToString(),
        };

        if (fetchAfter.HasValue)
            queryParams["from"] = fetchAfter.Value.ToUnixTimeSeconds().ToString();

        return ApiBase + "?" + string.Join(
            "&",
            queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}")
        );
    }

    private async Task<ErrorOr<string>> ExecuteHttpRequestAsync(string url, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            throw new LastFmApiException(
                29,
                $"Rate limited. Retry-After: {retryAfter.TotalSeconds}s",
                LastFmErrorType.Retryable,
                retryAfter
            );
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken: ct);
    }

    private static ErrorOr<JsonElement> ParseJsonResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorCode = errorElement.GetInt32();
            var errorMessage = root.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? "Unknown"
                : "Unknown";
            var errorType = ClassifyError(errorCode);

            if (errorType is not LastFmErrorType.Permanent)
                throw new LastFmApiException(errorCode, errorMessage, errorType);

            return Errors.LastFm.ApiError(errorMessage);
        }

        return root.Clone();
    }

    private static ErrorOr<FetchPageResult> ExtractTracks(JsonElement root)
    {
        if (!root.TryGetProperty("recenttracks", out var recenttracks))
            return Errors.LastFm.MalformedResponse;

        if (!recenttracks.TryGetProperty("track", out var tracksElement))
            return Errors.LastFm.MalformedResponse;

        JsonElement[] tracks = tracksElement.ValueKind switch
        {
            JsonValueKind.Array => [.. tracksElement.EnumerateArray()],
            JsonValueKind.Object => [tracksElement],
            _ => [],
        };

        var scrobbles = tracks
            .Select(TryExtractTrack)
            .Where(o => !o.IsError)
            .Select(o => o.Value)
            .ToList();

        var totalPages = 1;
        if (
            recenttracks.TryGetProperty("@attr", out var attrElement)
            && attrElement.TryGetProperty("totalPages", out var totalPagesEl)
        )
            totalPages = int.Parse(totalPagesEl.GetString() ?? "1");

        return new FetchPageResult(scrobbles, totalPages);
    }

    private static ErrorOr<LastFmScrobble> TryExtractTrack(JsonElement track)
    {
        if (!track.TryGetProperty("date", out var dateElement))
            return Errors.LastFm.MalformedResponse;

        var uts = dateElement.GetProperty("uts").GetString();
        if (uts is "0" or null)
            return Errors.LastFm.MalformedResponse;

        var playedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(uts));

        var trackName = track.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null;
        if (string.IsNullOrEmpty(trackName))
            return Errors.LastFm.MalformedResponse;

        var artistName =
            track.TryGetProperty("artist", out var artistEl)
            && artistEl.TryGetProperty("#text", out var artistText)
                ? artistText.GetString() ?? ""
                : "";

        var albumName =
            track.TryGetProperty("album", out var albumEl)
            && albumEl.TryGetProperty("#text", out var albumText)
                ? albumText.GetString() ?? ""
                : "";

        return new LastFmScrobble
        {
            TrackTitle = trackName,
            Artist = artistName,
            Album = albumName,
            PlayedAt = playedAt,
        };
    }

    public static LastFmErrorType ClassifyError(int errorCode) =>
        errorCode switch
        {
            8 or 11 or 16 => LastFmErrorType.Retryable,
            29 => LastFmErrorType.Retryable,
            4 or 9 or 10 or 13 or 14 or 17 or 26 => LastFmErrorType.Fatal,
            _ => LastFmErrorType.Permanent,
        };
}

public enum LastFmErrorType
{
    Retryable,
    Fatal,
    Permanent,
}

public class LastFmApiException(
    int errorCode,
    string message,
    LastFmErrorType errorType,
    TimeSpan? retryAfter = null
) : Exception(message)
{
    public int ErrorCode { get; } = errorCode;
    public LastFmErrorType ErrorType { get; } = errorType;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
