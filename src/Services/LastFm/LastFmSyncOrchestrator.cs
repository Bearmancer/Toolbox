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

		Directory.CreateDirectory(StateDir);

		List<LastFmScrobble> existing =
		[
			.. (await LastFmState.LoadScrobblesAsync(StateDir)).OrderByDescending(scrobble =>
				scrobble.PlayedAt
			),
		];

		if (fetchAfter is { } since)
			existing.RemoveAll(sc => sc.PlayedAt >= since);

		List<LastFmScrobble> newScrobbles = await service.FetchRecentTracksAsync(
			fetchAfter,
			(page, count) => Telemetry.Info("Page {Page}: {Count} tracks", page, count),
			ct
		);

		if (newScrobbles.Count == 0)
		{
			Telemetry.Info("No new scrobbles found.");
			return new SyncResult(0, existing.Count, null);
		}

		List<LastFmScrobble> merged = LastFmState.MergeScrobbles(existing, newScrobbles);
		await LastFmState.SaveScrobblesAsync(StateDir, merged);

		DateTimeOffset? lastScrobbleDate = merged.Count > 0 ? merged[0].PlayedAt : null;

		Telemetry.Info(
			"Sync complete. {Total} total scrobbles ({New} new)",
			merged.Count,
			newScrobbles.Count
		);

		return new SyncResult(newScrobbles.Count, merged.Count, lastScrobbleDate);
	}

	public readonly record struct SyncResult(
		int NewCount,
		int TotalCount,
		DateTimeOffset? LastScrobbleDate
	);
}
