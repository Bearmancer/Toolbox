using Core;
using ErrorOr;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using SerilogTracing;

namespace Services.Google.YouTube;

public class YouTubeSortService(YouTubeService yt, YouTubePlaylistService playlistService)
{
	public async Task<ErrorOr<SortResult>> SortPlaylistAsync(
		string playlistId,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.SortPlaylist"
		);

		const int maxPasses = 3;
		var totalRepositioned = 0;

		for (var pass = 0; pass < maxPasses; pass++)
		{
			ErrorOr<SortPassResult> passResult = await FetchPlaylistItemsAsync(playlistId, ct)
				.Then(ComputeSortPlan)
				.ThenAsync(plan => ExecuteSortPlanAsync(plan, ct));

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

			Telemetry.Debug(
				"YouTube.SortPlaylist pass {Pass}: {Successes} updated, {Failures} failed",
				pass + 1,
				result.Successes,
				result.Failures
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

			Telemetry.Debug("YouTube.SortPlaylist: all {Count} updates succeeded", result.Successes);
		}

		PlaylistSnapshot? finalSummary = await playlistService.GetPlaylistSummaryAsync(
			playlistId,
			ct
		);
		activity.Complete();
		Telemetry.Info(
			"YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag}",
			totalRepositioned,
			finalSummary?.ETag ?? "unknown"
		);
		var etag = finalSummary?.ETag ?? "";
		return new SortResult(totalRepositioned, etag);
	}

	private async Task<ErrorOr<List<PlaylistItem>>> FetchPlaylistItemsAsync(
		string playlistId,
		CancellationToken ct
	)
	{
		try
		{
			IList<PlaylistItem> items = await playlistService.GetPlaylistItemsAsync(playlistId, ct);
			return items.ToList();
		}
		catch (Exception ex)
		{
			return Errors.YouTube.ApiError(ex.Message);
		}
	}

	public static SortPlan ComputeSortPlan(IList<PlaylistItem> items)
	{
		var sorted = items.OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase).ToList();

		var targetRank = sorted
			.Select((item, idx) => (item.Id, idx))
			.ToDictionary(x => x.Id, x => x.idx);

		var currentOrder = items.OrderBy(i => i.Snippet.Position ?? 0).ToList();
		var permutation = currentOrder.Select(item => targetRank[item.Id]).ToArray();

		List<int> lisCurrentIndices = LongestIncreasingSubsequence(permutation);
		var keptIds = lisCurrentIndices.Select(i => currentOrder[i].Id).ToHashSet();

		var updates = new List<PlaylistUpdate>();
		for (var i = 0; i < sorted.Count; i++)
			if (!keptIds.Contains(sorted[i].Id))
				updates.Add(new PlaylistUpdate(sorted[i], i));

		Telemetry.Debug(
			"ComputeSortPlan: {Total} items, LIS={LisSize}, {Delta} need repositioning",
			items.Count,
			keptIds.Count,
			updates.Count
		);

		return new SortPlan(items.Count, keptIds.Count, updates);
	}

	private async Task<ErrorOr<SortPassResult>> ExecuteSortPlanAsync(
		SortPlan plan,
		CancellationToken ct
	)
	{
		if (plan.Updates.Count == 0)
			return new SortPassResult(0, 0);

		Telemetry.Debug("ExecuteSortPlan: updating {Count} items sequentially", plan.Updates.Count);

		var successes = 0;
		var failures = 0;

		for (var i = 0; i < plan.Updates.Count; i++)
		{
			ct.ThrowIfCancellationRequested();

			PlaylistUpdate update = plan.Updates[i];
			PlaylistItem item = update.Item;
			var newPosition = update.NewPosition;
			var itemId = item.Id ?? "unknown";

			item.Snippet.Position = newPosition;

			Telemetry.Debug(
				"Updating item {Index}/{Total}: ItemId={ItemId}, NewPos={NewPos}",
				i + 1,
				plan.Updates.Count,
				itemId,
				newPosition
			);

			try
			{
				await yt.PlaylistItems.Update(item, "snippet").ExecuteAsync(ct);
				successes++;
				Telemetry.Debug(
					"Successfully updated ItemId={ItemId} to position {Position}",
					itemId,
					newPosition
				);
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

			if (i < plan.Updates.Count - 1)
				await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
		}

		return failures > 0
			? Errors.YouTube.ApiError($"{failures}/{plan.Updates.Count} updates failed")
			: new SortPassResult(successes, failures);
	}

	private static List<int> LongestIncreasingSubsequence(int[] arr)
	{
		var n = arr.Length;
		if (n == 0)
			return [];

		var tails = new List<int>();
		var tailsIdx = new List<int>();
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

		var result = new List<int>();
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

	public readonly record struct SortResult(int Repositioned, string NewETag);

	public readonly record struct SortPlan(
		int TotalItems,
		int LisSize,
		IReadOnlyList<PlaylistUpdate> Updates
	);

	public readonly record struct SortPassResult(int Successes, int Failures);
}
