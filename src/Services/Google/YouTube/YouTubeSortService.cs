using System.Diagnostics;
using System.Net;
using Core;
using ErrorOr;
using Google;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using SerilogTracing;

namespace Services.Google.YouTube;

public class YouTubeSortService(YouTubeService yt, YouTubePlaylistService playlistService)
{
	public async Task<ErrorOr<SortResult>> SortPlaylistAsync(
		string playlistId,
		IReadOnlyDictionary<string, string> translatedTitles,
		int remainingBudget,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.SortPlaylist"
		);

		Stopwatch sortSw = System.Diagnostics.Stopwatch.StartNew();
		Telemetry.Verbose("SortPlaylist started for {PlaylistId}", playlistId);

		const int maxPasses = 3;
		var totalRepositioned = 0;
		var totalWritesConsumed = 0;
		var totalDistinctMoved = 0;

		for (var pass = 0; pass < maxPasses; pass++)
		{
			Stopwatch passSw = System.Diagnostics.Stopwatch.StartNew();

			var budgetForPass = remainingBudget - totalWritesConsumed;
			if (budgetForPass <= 0)
			{
				Telemetry.Info(
					"Quota budget reached ({Writes}/{Max} writes). Stopping sort.",
					totalWritesConsumed,
					remainingBudget
				);
				break;
			}

			ErrorOr<SortPassResult> passResult = await FetchPlaylistItemsAsync(playlistId, ct)
				.Then(items => ComputeSortPlan(items, translatedTitles))
				.ThenAsync(plan => ExecuteSortPlanAsync(plan, budgetForPass, ct));

			passSw.Stop();
			Telemetry.Verbose(
				"Pass {Pass} completed in {ElapsedMs}ms",
				pass + 1,
				passSw.ElapsedMilliseconds
			);

			if (passResult.IsError)
			{
				Telemetry.Error(
					"YouTube.SortPlaylist pass {Pass} aborted: {Error}",
					pass + 1,
					passResult.FirstError.Description
				);
				break;
			}

			SortPassResult result = passResult.Value;
			totalRepositioned += result.Successes;
			totalWritesConsumed += result.WritesConsumed;
			totalDistinctMoved += result.DistinctItemsMoved;

			Telemetry.Debug(
				"YouTube.SortPlaylist pass {Pass}: {Successes} updated, {Failures} failed in {ElapsedMs}ms",
				pass + 1,
				result.Successes,
				result.Failures,
				passSw.ElapsedMilliseconds
			);

			if (result.Failures > 0)
			{
				Telemetry.Error(
					"YouTube.SortPlaylist: {Failures} updates failed — aborting",
					result.Failures
				);
				break;
			}

			if (result.Successes == 0)
				break;

			Telemetry.Debug(
				"YouTube.SortPlaylist: all {Count} updates succeeded",
				result.Successes
			);
		}

		PlaylistSnapshot? finalSummary = await playlistService.GetPlaylistSummaryAsync(
			playlistId,
			ct
		);
		activity.Complete();
		sortSw.Stop();

		var playlistName = finalSummary?.Title ?? playlistId;
		var itemCount = finalSummary?.ReportedVideoCount ?? 0;
		if (totalRepositioned == 0)
		{
			Telemetry.Debug(
				"{PlaylistName} already sorted ({ItemCount} items, {ElapsedMs}ms)",
				playlistName,
				itemCount,
				sortSw.ElapsedMilliseconds
			);
		}
		else
		{
			Telemetry.Info(
				"{PlaylistName} — {Distinct}/{Total} items sorted ({ApiCalls} API calls)",
				playlistName,
				totalDistinctMoved,
				itemCount,
				totalRepositioned
			);
		}

		Telemetry.Debug("YouTube.SortPlaylist ETag: {ETag}", finalSummary?.ETag ?? "unknown");
		Telemetry.Debug(
			"Sort completed: {Writes} writes ({Units} quota units)",
			totalWritesConsumed,
			totalWritesConsumed * 50
		);

		var etag = finalSummary?.ETag ?? "";
		return new SortResult(
			totalRepositioned,
			etag,
			totalWritesConsumed,
			totalDistinctMoved,
			DateTimeOffset.UtcNow,
			totalRepositioned == 0
		);
	}

	private async Task<ErrorOr<List<PlaylistItem>>> FetchPlaylistItemsAsync(
		string playlistId,
		CancellationToken ct
	)
	{
		Stopwatch fetchSw = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			IList<PlaylistItem> items = await playlistService.GetPlaylistItemsAsync(playlistId, ct);
			fetchSw.Stop();
			Telemetry.Verbose(
				"Fetched {Count} items in {ElapsedMs}ms",
				items.Count,
				fetchSw.ElapsedMilliseconds
			);
			return items.ToList();
		}
		catch (Exception ex)
		{
			fetchSw.Stop();
			Telemetry.Warn(
				"YouTube.FetchFailed in {ElapsedMs}ms: {Error}",
				fetchSw.ElapsedMilliseconds,
				ex.Message
			);
			return Errors.YouTube.ApiError(ex.Message);
		}
	}

	public static SortPlan ComputeSortPlan(
		IList<PlaylistItem> items,
		IReadOnlyDictionary<string, string> translatedTitles
	)
	{
		List<PlaylistItem> sorted =
		[
			.. items.OrderBy(
				i => SortKeyFor(i, translatedTitles),
				StringComparer.OrdinalIgnoreCase
			),
		];

		Dictionary<string, int> targetRank = sorted
			.Select((item, idx) => (item.Id, idx))
			.ToDictionary(x => x.Id, x => x.idx);

		List<PlaylistItem> currentOrder = [.. items];
		var permutation = currentOrder.Select(item => targetRank[item.Id]).ToArray();

		Stopwatch lisSw = System.Diagnostics.Stopwatch.StartNew();
		List<int> lisCurrentIndices = LongestIncreasingSubsequence(permutation);
		lisSw.Stop();
		Telemetry.Verbose(
			"LIS computed in {ElapsedMs}ms for {Count} items",
			lisSw.ElapsedMilliseconds,
			permutation.Length
		);

		HashSet<string> keptIds = [.. lisCurrentIndices.Select(i => currentOrder[i].Id)];

		List<PlaylistUpdate> updates = [];
		for (var i = 0; i < sorted.Count; i++)
			if (!keptIds.Contains(sorted[i].Id))
				updates.Add(new(sorted[i], i));

		Telemetry.Debug(
			"ComputeSortPlan: {Total} items, LIS={LisSize}, {Delta} need repositioning",
			items.Count,
			keptIds.Count,
			updates.Count
		);

		return new(items.Count, keptIds.Count, updates);
	}

	private static string SortKeyFor(
		PlaylistItem item,
		IReadOnlyDictionary<string, string> translatedTitles
	)
	{
		var videoId = item.Snippet?.ResourceId?.VideoId ?? "";
		return translatedTitles.GetValueOrDefault(videoId, item.Snippet?.Title ?? "");
	}

	private async Task<ErrorOr<SortPassResult>> ExecuteSortPlanAsync(
		SortPlan plan,
		int remainingBudget,
		CancellationToken ct
	)
	{
		if (plan.Updates.Count == 0)
			return new SortPassResult(0, 0, 0, 0);

		var maxUpdatesThisPass = Math.Min(plan.Updates.Count, remainingBudget);
		Telemetry.Debug(
			"ExecuteSortPlan: updating {Count} items sequentially (budget: {Budget})",
			maxUpdatesThisPass,
			remainingBudget
		);

		var successes = 0;
		var failures = 0;
		var writesConsumed = 0;
		HashSet<string> movedItemIds = [];

		for (var i = 0; i < maxUpdatesThisPass; i++)
		{
			ct.ThrowIfCancellationRequested();

			PlaylistUpdate update = plan.Updates[i];
			PlaylistItem item = update.Item;
			var newPosition = update.NewPosition;
			var itemId = item.Id ?? "unknown";

			item.Snippet.Position = newPosition;

			Telemetry.Verbose(
				"Updating item {Index}/{Total}: ItemId={ItemId}, NewPos={NewPos}",
				i + 1,
				maxUpdatesThisPass,
				itemId,
				newPosition
			);

			if ((i + 1) % 25 == 0 || i == maxUpdatesThisPass - 1)
			{
				var avgMs = (double)(successes + failures) / (i + 1) * 1000;
				Telemetry.Debug(
					"Sort progress: {Current}/{Total} ({Percent}%) — avg {AvgMs:F0}ms/item",
					i + 1,
					maxUpdatesThisPass,
					(i + 1) * 100 / maxUpdatesThisPass,
					avgMs
				);
			}

			try
			{
				Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
				await yt.PlaylistItems.Update(item, "snippet").ExecuteAsync(ct);
				sw.Stop();
				Telemetry.Verbose("API call completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
				successes++;
				writesConsumed++;
				movedItemIds.Add(itemId);
			}
			catch (GoogleApiException ex) when (IsQuotaOrRateLimit(ex))
			{
				Telemetry.Error(
					"Quota/rate-limit exhausted at item {Index}/{Total}. Stopping sort.",
					i + 1,
					maxUpdatesThisPass
				);
				return Errors.YouTube.QuotaExceeded($"Quota exhausted after {successes} updates");
			}
			catch (Exception ex)
			{
				failures++;
				Telemetry.Error(
					"Failed to update ItemId={ItemId} to position {Position}: {Error}",
					itemId,
					newPosition,
					ex.Message
				);
			}

			if (writesConsumed >= remainingBudget)
			{
				Telemetry.Warn(
					"Quota budget exhausted mid-playlist. Stopping after {Count} writes.",
					writesConsumed
				);
				return new SortPassResult(successes, failures, writesConsumed, movedItemIds.Count);
			}

			if (i < maxUpdatesThisPass - 1)
				await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
		}

		return failures > 0
			? Errors.YouTube.ApiError($"{failures}/{maxUpdatesThisPass} updates failed")
			: new SortPassResult(successes, failures, writesConsumed, movedItemIds.Count);
	}

	private static bool IsQuotaOrRateLimit(GoogleApiException ex) =>
		ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
		&& ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);

	private static List<int> LongestIncreasingSubsequence(int[] arr)
	{
		var n = arr.Length;
		if (n == 0)
			return [];

		List<int> tails = [];
		List<int> tailsIdx = [];
		var predecessor = new int[n];
		Array.Fill(predecessor, -1);

		for (var i = 0; i < n; i++)
		{
			int lo = 0,
				hi = tails.Count;
			while (lo < hi)
			{
				var mid = (lo + hi) / 2;
				if (tails[mid] < arr[i])
					lo = mid + 1;
				else
					hi = mid;
			}

			if (lo == tails.Count)
			{
				tails.Add(arr[i]);
				tailsIdx.Add(i);
			}
			else
			{
				tails[lo] = arr[i];
				tailsIdx[lo] = i;
			}

			if (lo > 0)
				predecessor[i] = tailsIdx[lo - 1];
		}

		List<int> result = [];
		var cur = tailsIdx[^1];
		while (cur >= 0)
		{
			result.Add(cur);
			cur = predecessor[cur];
		}

		result.Reverse();
		return result;
	}

	public readonly record struct PlaylistUpdate(PlaylistItem Item, int NewPosition);

	public readonly record struct SortResult(
		int Repositioned,
		string NewETag,
		int WritesConsumed,
		int DistinctItemsMoved,
		DateTimeOffset? LastSortAttempted,
		bool LastSortCompleted
	);

	public readonly record struct SortPlan(
		int TotalItems,
		int LisSize,
		IReadOnlyList<PlaylistUpdate> Updates
	);

	public readonly record struct SortPassResult(
		int Successes,
		int Failures,
		int WritesConsumed,
		int DistinctItemsMoved
	);
}
