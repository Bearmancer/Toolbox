using System.Globalization;
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
    private readonly LastFmApiClient _client = new(httpClient, apiKey, username);
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

                scrobbles.Add(scrobble);
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

    private async Task<ErrorOr<(List<LastFmScrobble> Tracks, int TotalPages)>> FetchPageAsync(
        DateTimeOffset? fetchAfter,
        int page,
        int limit,
        CancellationToken ct
    )
    {
        const int maxRetries = 3;
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; attempt < maxRetries; attempt++)
            try
            {
                await WaitForRateLimit(ct);
                var result = await _client.FetchPageCoreAsync(fetchAfter, page, limit, ct);
                if (result.IsError)
                    return result.FirstError;
                return (result.Value.Scrobbles, result.Value.TotalPages);
            }
            catch (LastFmApiException ex)
                when (ex.ErrorType == LastFmErrorType.Retryable)
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
            catch (HttpRequestException ex)
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

        await WaitForRateLimit(ct);
        var finalResult = await _client.FetchPageCoreAsync(fetchAfter, page, limit, ct);
        if (finalResult.IsError)
            return finalResult.FirstError;
        return (finalResult.Value.Scrobbles, finalResult.Value.TotalPages);
    }

    private async Task WaitForRateLimit(CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - LastRequestTime;
        if (elapsed < TimeSpan.FromMilliseconds(200))
            await Task.Delay(TimeSpan.FromMilliseconds(200) - elapsed, ct);
        LastRequestTime = DateTimeOffset.UtcNow;
    }
}
