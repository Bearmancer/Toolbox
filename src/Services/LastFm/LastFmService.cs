using System.Globalization;
using System.Net;
using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.LastFm;

public sealed record LastFmScrobble
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);
    public required string TrackTitle { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required DateTimeOffset PlayedAt { get; init; }

    public string Date =>
        PlayedAt.ToOffset(IstOffset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

public class LastFmService(HttpClient httpClient, string apiKey, string username)
{
    private readonly record struct FetchPageResult(List<LastFmScrobble> Scrobbles, int TotalPages);

    private const string ApiBase = "https://ws.audioscrobbler.com/2.0/";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string ApiKey = apiKey;
    private readonly HttpClient Client = httpClient;
    private readonly string UserName = username;
    private DateTimeOffset LastRequestTime = DateTimeOffset.MinValue;

    public async Task<List<LastFmScrobble>> FetchRecentTracksAsync(
        DateTimeOffset? since,
        Action<int, int> onPage,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.LastFm);
        using var activity = Telemetry.StartActivity(messageTemplate: "LastFm.FetchRecentTracks");

        var scrobbles = new List<LastFmScrobble>();
        var page = 1;
        const int limit = 200;
        var hasMore = true;

        while (hasMore)
        {
            ct.ThrowIfCancellationRequested();

            var pageResult = await FetchPageAsync(since, page, limit, ct);

            if (pageResult.IsError)
            {
                Telemetry.Error(
                    "Failed to fetch page {Page}: {Errors}",
                    page,
                    string.Join(", ", pageResult.Errors.Select(e => e.Description))
                );
                break;
            }

            var (tracks, totalPages) = pageResult.Value;

            if (tracks.Count == 0)
                break;

            var batchCount = 0;
            foreach (var scrobble in tracks)
            {
                if (since.HasValue && scrobble.PlayedAt <= since.Value)
                {
                    hasMore = false;
                    break;
                }

                scrobbles.Add(item: scrobble);
                batchCount++;
            }

            onPage(page, batchCount);
            Telemetry.Debug("Page {Page}: {Count} tracks", page, batchCount);

            if (page >= totalPages)
                hasMore = false;
            else
                page++;
        }

        activity.Complete();
        Telemetry.Debug("LastFm.FetchRecentTracks returned {Count} scrobbles", scrobbles.Count);
        return scrobbles;
    }

    private async Task<ErrorOr<FetchPageResult>> FetchPageAsync(
        DateTimeOffset? fetchAfter,
        int page,
        int limit,
        CancellationToken ct
    )
    {
        const int maxRetries = 3;
        var delay = TimeSpan.FromSeconds(seconds: 1);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
            try
            {
                return await FetchPageCoreAsync(fetchAfter, page, limit, ct);
            }
            catch (LastFmApiException ex)
                when (ex.ErrorType == LastFmErrorType.Retryable && attempt < maxRetries)
            {
                var waitTime = ex.RetryAfter ?? delay;
                Telemetry.Warn(
                    "Last.fm API attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt,
                    ex.Message,
                    waitTime.TotalSeconds
                );
                await Task.Delay(waitTime, ct);
                delay *= 2;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                Telemetry.Warn(
                    "Last.fm HTTP attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt,
                    ex.Message,
                    delay.TotalSeconds
                );
                await Task.Delay(delay, ct);
                delay *= 2;
            }

        return await FetchPageCoreAsync(fetchAfter, page, limit, ct);
    }

    private async Task<ErrorOr<FetchPageResult>> FetchPageCoreAsync(
        DateTimeOffset? fetchAfter,
        int page,
        int limit,
        CancellationToken ct
    )
    {
        await WaitForRateLimit(ct);

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
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(seconds: 5);
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

        var uts = dateElement.GetProperty(propertyName: "uts").GetString();
        if (uts is "0" or null)
            return Errors.LastFm.MalformedResponse;

        var playedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(s: uts));

        var trackName = track.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null;
        if (string.IsNullOrEmpty(value: trackName))
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

    private async Task WaitForRateLimit(CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - LastRequestTime;
        if (elapsed < TimeSpan.FromMilliseconds(milliseconds: 200))
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds: 200) - elapsed, ct);
        LastRequestTime = DateTimeOffset.UtcNow;
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
) : Exception(message: message)
{
    public int ErrorCode { get; } = errorCode;
    public LastFmErrorType ErrorType { get; } = errorType;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
