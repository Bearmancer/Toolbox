using Core;
using ErrorOr;

namespace Services.LastFm;

public class LastFmSyncOrchestrator(LastFmService service)
{
	private static readonly string StateDir = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"lastfm"
	);

	public async Task<ErrorOr<SyncResult>> SyncAsync(
		DateTimeOffset? fetchAfter,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.LastFm);

		if (fetchAfter.HasValue)
			Telemetry.Info("Last.fm sync starting (from {Date})", fetchAfter.Value.ToString("yyyy-MM-dd HH:mm"));
		else
			Telemetry.Info("Last.fm sync starting");

		Directory.CreateDirectory(StateDir);

		List<LastFmScrobble> existing =
		[
			.. (await LastFmState.LoadScrobblesAsync(StateDir)).OrderByDescending(scrobble =>
				scrobble.PlayedAt
			),
		];

		var removedCount = 0;

		if (fetchAfter is { } since)
		{
			removedCount = existing.Count(sc => sc.PlayedAt >= since);
			existing.RemoveAll(sc => sc.PlayedAt >= since);
		}
		else if (existing.Count > 0)
			fetchAfter = existing[0].PlayedAt;

		List<LastFmScrobble> newScrobbles = await service.FetchRecentTracksAsync(fetchAfter, ct);

		if (newScrobbles.Count == 0)
		{
			Telemetry.Info("Last.fm sync: 0 new, {Removed} removed, {Total} total", removedCount, existing.Count);
			return new SyncResult(0, removedCount, existing.Count, null);
		}

		List<LastFmScrobble> merged = LastFmState.MergeScrobbles(existing, newScrobbles);
		await LastFmState.SaveScrobblesAsync(StateDir, merged);

		DateTimeOffset? lastScrobbleDate = merged.Count > 0 ? merged[0].PlayedAt : null;

		Telemetry.Info(
			"Last.fm sync: {New} new, {Removed} removed, {Total} total",
			newScrobbles.Count,
			removedCount,
			merged.Count
		);

		return new SyncResult(newScrobbles.Count, removedCount, merged.Count, lastScrobbleDate);
	}

	public readonly record struct SyncResult(
		int NewCount,
		int RemovedCount,
		int TotalCount,
		DateTimeOffset? LastScrobbleDate
	);
}
