using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Services.Google.YouTube;

public class YouTubeSortService(YouTubeService yt, YouTubePlaylistService playlistService)
{
    private readonly record struct PlaylistUpdate(PlaylistItem Item, int NewPosition);
    private readonly record struct BatchFailure(int Position, string ItemId, string ErrorMessage, int HttpStatusCode, string? FullError);
    public readonly record struct SortResult(int Repositioned, string NewETag);

    public async Task<SortResult> SortPlaylistAsync(
        string playlistId,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.SortPlaylist");

        const int MaxPasses = 3;
        var totalRepositioned = 0;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var items = await playlistService.GetPlaylistItemsAsync(playlistId, ct);

            var sorted = items.OrderBy(i => i.Snippet.Title, StringComparer.OrdinalIgnoreCase).ToList();

            var targetRank = sorted
                .Select((item, idx) => (item.Id, idx))
                .ToDictionary(x => x.Id, x => x.idx);

            var currentOrder = items.OrderBy(i => i.Snippet.Position ?? 0).ToList();
            var permutation = currentOrder
                .Select(item => targetRank[item.Id])
                .ToArray();

            var lisCurrentIndices = LongestIncreasingSubsequence(permutation);
            var keptIds = lisCurrentIndices.Select(i => currentOrder[i].Id).ToHashSet();

            var toUpdate = new List<PlaylistUpdate>();
            for (var i = 0; i < sorted.Count; i++)
                if (!keptIds.Contains(sorted[i].Id))
                    toUpdate.Add(new PlaylistUpdate(sorted[i], i));

            Telemetry.Info(
                "YouTube.SortPlaylist pass {Pass}: {Total} items, LIS={LisSize}, {Delta} need repositioning",
                pass + 1,
                items.Count,
                keptIds.Count,
                toUpdate.Count
            );

            if (toUpdate.Count == 0)
            {
                var summary = await playlistService.GetPlaylistSummaryAsync(playlistId, ct);
                activity.Complete();
                Telemetry.Info(
                    "YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag}",
                    totalRepositioned,
                    summary?.ETag ?? "unknown"
                );
                return new SortResult(totalRepositioned, summary?.ETag ?? "");
            }

            Telemetry.Info(
                "YouTube.SortPlaylist: updating {Count} items sequentially",
                toUpdate.Count
            );

            var failures = new List<BatchFailure>();
            var passSuccessCount = 0;

            for (var i = 0; i < toUpdate.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var update = toUpdate[i];
                var item = update.Item;
                var newPosition = update.NewPosition;
                var itemId = item.Id ?? "unknown";

                item.Snippet.Position = newPosition;

                Telemetry.Debug(
                    "Updating item {Index}/{Total}: ItemId={ItemId}, NewPos={NewPos}",
                    i + 1,
                    toUpdate.Count,
                    itemId,
                    newPosition
                );

                try
                {
                    await yt.PlaylistItems.Update(item, "snippet").ExecuteAsync(ct);
                    passSuccessCount++;
                    Telemetry.Debug(
                        "Successfully updated ItemId={ItemId} to position {Position}",
                        itemId,
                        newPosition
                    );
                }
                catch (Exception ex)
                {
                    Telemetry.Error(
                        "Failed to update ItemId={ItemId} to position {Position}: {Error}",
                        itemId,
                        newPosition,
                        ex.Message
                    );
                    failures.Add(new BatchFailure(newPosition, itemId, ex.Message, 0, ex.ToString()));
                }

                if (i < toUpdate.Count - 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            totalRepositioned += passSuccessCount;

            if (failures.Count > 0)
            {
                Telemetry.Error(
                    "YouTube.SortPlaylist: {Failed}/{Total} updates FAILED — aborting",
                    failures.Count,
                    toUpdate.Count
                );
                foreach (var f in failures)
                    Telemetry.Error(
                        "Update failure at position {Position} (ItemId={ItemId}): {Error}",
                        f.Position,
                        f.ItemId,
                        f.ErrorMessage
                    );
                break;
            }

            Telemetry.Info(
                "YouTube.SortPlaylist: all {Count} updates succeeded",
                passSuccessCount
            );
        }

        var finalSummary = await playlistService.GetPlaylistSummaryAsync(playlistId, ct);
        activity.Complete();
        Telemetry.Info(
            "YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag}",
            totalRepositioned,
            finalSummary?.ETag ?? "unknown"
        );
        return new SortResult(totalRepositioned, finalSummary?.ETag ?? "");
    }

    private static List<int> LongestIncreasingSubsequence(int[] arr)
    {
        var n = arr.Length;
        if (n == 0) return [];

        var tails = new List<int>();
        var tailsIdx = new List<int>();
        var predecessor = new int[n];
        Array.Fill(predecessor, -1);

        for (var i = 0; i < n; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (tails[mid] < arr[i]) lo = mid + 1;
                else hi = mid;
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
}
