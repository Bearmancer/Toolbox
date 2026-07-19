using System.Globalization;
using Core;
using ErrorOr;
using SerilogTracing;

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
	private readonly LastFmApiClient Client = new(httpClient, apiKey, username);
	private DateTimeOffset LastRequestTime = DateTimeOffset.MinValue;

	public async Task<List<LastFmScrobble>> FetchRecentTracksAsync(
		DateTimeOffset? since,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.LastFm);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "LastFm.FetchRecentTracks"
		);

		var fetchSw = System.Diagnostics.Stopwatch.StartNew();
		Telemetry.Verbose("FetchRecentTracks started (since={Since})", since?.ToString("yyyy-MM-dd HH:mm") ?? "all");

		List<LastFmScrobble> scrobbles = [];
		var page = 1;
		const int limit = 200;
		var hasMore = true;

		while (hasMore)
		{
			ct.ThrowIfCancellationRequested();

			var pageSw = System.Diagnostics.Stopwatch.StartNew();
			ErrorOr<(List<LastFmScrobble> Tracks, int TotalPages)> pageResult =
				await FetchPageAsync(since, page, limit, ct);
			pageSw.Stop();

			if (pageResult.IsError)
			{
				Telemetry.Error(
					"Failed to fetch page {Page} in {ElapsedMs}ms: {Errors}",
					page,
					pageSw.ElapsedMilliseconds,
					string.Join(", ", pageResult.Errors.Select(e => e.Description))
				);
				break;
			}

			(List<LastFmScrobble> tracks, var totalPages) = pageResult.Value;

			if (tracks.Count == 0)
				break;

			var batchCount = 0;
			foreach (LastFmScrobble scrobble in tracks)
			{
				if (since.HasValue && scrobble.PlayedAt <= since.Value)
				{
					hasMore = false;
					break;
				}

				scrobbles.Add(scrobble);
				batchCount++;
			}

			Telemetry.Debug(
				"Page {Page}/{Total}: {Count} tracks in {ElapsedMs}ms",
				page,
				totalPages,
				batchCount,
				pageSw.ElapsedMilliseconds
			);

			if (page % 10 == 0 || page == totalPages)
			{
				Telemetry.Debug(
					"Fetch progress: {Page}/{TotalPages} pages ({Percent}%) — {Scrobbles} scrobbles so far",
					page,
					totalPages,
					page * 100 / totalPages,
					scrobbles.Count
				);
			}

			if (page >= totalPages)
				hasMore = false;
			else
				page++;
		}

		fetchSw.Stop();
		activity.Complete(Serilog.Events.LogEventLevel.Debug);
		Telemetry.Debug("LastFm.FetchRecentTracks returned {Count} scrobbles in {ElapsedMs}ms", scrobbles.Count, fetchSw.ElapsedMilliseconds);
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
				ErrorOr<LastFmApiClient.FetchPageResult> result = await Client.FetchPageCoreAsync(
					fetchAfter,
					page,
					limit,
					ct
				);
				return result.IsError
					? result.FirstError
					: (result.Value.Scrobbles, result.Value.TotalPages);
			}
			catch (LastFmApiException ex) when (ex.ErrorType == LastFmErrorType.Retryable)
			{
				TimeSpan waitTime = ex.RetryAfter ?? delay;
				Telemetry.Warn(
					"Last.fm API attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
					attempt,
					ex.Message,
					waitTime.TotalSeconds
				);
				await Task.Delay(waitTime, ct);
				delay *= 2;
			}
			catch (LastFmApiException ex)
			{
				Telemetry.Error("Last.fm API error {Code}: {Error}", ex.ErrorCode, ex.Message);
				return Errors.LastFm.ApiError(ex.Message);
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
		ErrorOr<LastFmApiClient.FetchPageResult> finalResult = await Client.FetchPageCoreAsync(
			fetchAfter,
			page,
			limit,
			ct
		);
		return finalResult.IsError
			? finalResult.FirstError
			: (finalResult.Value.Scrobbles, finalResult.Value.TotalPages);
	}

	private async Task WaitForRateLimit(CancellationToken ct)
	{
		TimeSpan elapsed = DateTimeOffset.UtcNow - LastRequestTime;
		if (elapsed < TimeSpan.FromMilliseconds(200))
		{
			var waitTime = TimeSpan.FromMilliseconds(200) - elapsed;
			Telemetry.Verbose("Rate limit: waiting {WaitMs}ms", waitTime.TotalMilliseconds);
			await Task.Delay(waitTime, ct);
		}
		LastRequestTime = DateTimeOffset.UtcNow;
	}
}
