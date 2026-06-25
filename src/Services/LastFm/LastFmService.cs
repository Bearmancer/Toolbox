using System.Net;
using System.Text.Json;
using Core;
using Services.LastFm.Models;

namespace Services.LastFm;

public class LastFmService(HttpClient httpClient, string apiKey, string username)
{
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
        using var _ = Telemetry.ForService(service: "LastFm");
        using var activity = Telemetry.StartActivity(messageTemplate: "LastFm.FetchRecentTracks");

        var scrobbles = new List<LastFmScrobble>();
        var page = 1;
        const int limit = 200;
        var hasMore = true;

        while (hasMore)
        {
            ct.ThrowIfCancellationRequested();

            var (tracks, totalPages) = await FetchPageAsync(
                since,
                page,
                limit,
                ct
            );

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
        Telemetry.Debug(
            "LastFm.FetchRecentTracks returned {Count} scrobbles",
            scrobbles.Count
        );
        return scrobbles;
    }

    private async Task<(List<LastFmScrobble> Scrobbles, int TotalPages)> FetchPageAsync(
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
                return await FetchPageCoreAsync(
                    fetchAfter,
                    page,
                    limit,
                    ct
                );
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

    private async Task<(List<LastFmScrobble> Scrobbles, int TotalPages)> FetchPageCoreAsync(
        DateTimeOffset? fetchAfter,
        int page,
        int limit,
        CancellationToken ct
    )
    {
        await WaitForRateLimit(ct: ct);

        var queryParams = new Dictionary<string, string>
        {
            [key: "method"] = "user.getrecenttracks",
            [key: "user"] = UserName,
            [key: "api_key"] = ApiKey,
            [key: "format"] = "json",
            [key: "limit"] = limit.ToString(),
            [key: "page"] = page.ToString(),
        };

        if (fetchAfter.HasValue)
            queryParams[key: "from"] = fetchAfter.Value.ToUnixTimeSeconds().ToString();

        var url =
            ApiBase
            + "?"
            + string.Join(
                "&",
                queryParams.Select(kvp =>
                    $"{kvp.Key}={Uri.EscapeDataString(stringToEscape: kvp.Value)}"
                )
            );

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

        var json = await response.Content.ReadAsStringAsync(cancellationToken: ct);
        using var doc = JsonDocument.Parse(json: json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorCode = errorElement.GetInt32();
            var errorMessage = root.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? "Unknown"
                : "Unknown";
            var errorType = ClassifyError(errorCode: errorCode);
            throw new LastFmApiException(
                errorCode,
                errorMessage,
                errorType
            );
        }

        var recenttracks = root.GetProperty(propertyName: "recenttracks");
        var tracksElement = recenttracks.GetProperty(propertyName: "track");

        JsonElement[] tracks = tracksElement.ValueKind switch
        {
            JsonValueKind.Array => [.. tracksElement.EnumerateArray()],
            JsonValueKind.Object => [tracksElement],
            _ => [],
        };

        var scrobbles = new List<LastFmScrobble>();
        foreach (var track in tracks)
        {
            if (!track.TryGetProperty("date", out var dateElement))
                continue;

            var uts = dateElement.GetProperty(propertyName: "uts").GetString();
            if (uts is "0" or null)
                continue;

            var playedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(s: uts));

            var trackName = track.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString()
                : null;
            if (string.IsNullOrEmpty(value: trackName))
                continue;

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

            scrobbles.Add(
                new LastFmScrobble
                {
                    TrackTitle = trackName,
                    Artist = artistName,
                    Album = albumName,
                    PlayedAt = playedAt,
                }
            );
        }

        var totalPages = 1;
        if (
            recenttracks.TryGetProperty("@attr", out var attrElement)
            && attrElement.TryGetProperty("totalPages", out var totalPagesEl)
        )
            totalPages = int.Parse(totalPagesEl.GetString() ?? "1");

        return (scrobbles, totalPages);
    }

    private async Task WaitForRateLimit(CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - LastRequestTime;
        if (elapsed < TimeSpan.FromMilliseconds(milliseconds: 200))
            await Task.Delay(
                TimeSpan.FromMilliseconds(milliseconds: 200) - elapsed,
                ct
            );
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

    public static List<LastFmScrobble> MergeScrobbles(
        List<LastFmScrobble> existing,
        List<LastFmScrobble> newScrobbles
    )
    {
        var merged = existing
            .Concat(second: newScrobbles)
            .GroupBy(s => s.PlayedAt)
            .Select(g => g.First())
            .OrderByDescending(s => s.PlayedAt)
            .ToList();

        return merged;
    }

    public static async Task<List<LastFmScrobble>> LoadScrobblesAsync(string stateDir)
    {
        var path = Path.Combine(stateDir, "scrobbles.json");

        if (!File.Exists(path: path))
            return [];

        try
        {
            await using var stream = File.OpenRead(path: path);
            return await JsonSerializer.DeserializeAsync<List<LastFmScrobble>>(
                stream,
                JsonOptions
            ) ?? [];
        }
        catch (JsonException ex)
        {
            Telemetry.Warn(
                "Corrupt scrobbles at {Path}, resetting: {Error}",
                path,
                ex.Message
            );
            return [];
        }
    }

    public static async Task SaveScrobblesAsync(string stateDir, List<LastFmScrobble> scrobbles)
    {
        if (!Directory.Exists(path: stateDir))
            Directory.CreateDirectory(path: stateDir);

        var path = Path.Combine(stateDir, "scrobbles.json");

        try
        {
            await using var stream = File.Create(path: path);
            await JsonSerializer.SerializeAsync(
                stream,
                scrobbles,
                JsonOptions
            );
        }
        catch (IOException ex)
        {
            Telemetry.Error(
                "Failed to save scrobbles to {Path}: {Error}",
                path,
                ex.Message
            );
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Telemetry.Error(
                "Permission denied saving scrobbles to {Path}: {Error}",
                path,
                ex.Message
            );
            throw;
        }
    }
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
