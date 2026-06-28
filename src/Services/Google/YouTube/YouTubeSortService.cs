using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using GoogleRequests = Google.Apis.Requests;

namespace Services.Google.YouTube;

public class YouTubeSortService(YouTubeService yt, YouTubePlaylistService playlistService)
{
    public async Task<(int Repositioned, string NewETag)> SortPlaylistAsync(
        string playlistId,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.Google);
        using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.SortPlaylist");

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

        var toUpdate = new List<(PlaylistItem Item, int NewPosition)>();
        for (var i = 0; i < sorted.Count; i++)
        {
            if (!keptIds.Contains(sorted[i].Id))
            {
                toUpdate.Add((sorted[i], i));
            }
        }

        Telemetry.Info(
            "YouTube.SortPlaylist: {Total} items, LIS={LisSize}, {Delta} need repositioning",
            items.Count,
            keptIds.Count,
            toUpdate.Count
        );

        if (toUpdate.Count == 0)
        {
            activity.Complete();
            Telemetry.Info(
                template: "YouTube.SortPlaylist: already sorted — 0 repositioned, ETag unchanged"
            );
            return (0, "");
        }

        var batchFailures = new List<(int Position, string ErrorMessage)>();
        const int batchSize = 25;
        var batches = toUpdate
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        Telemetry.Info(
            "YouTube.SortPlaylist: splitting {Total} updates into {BatchCount} batches of {BatchSize}",
            toUpdate.Count,
            batches.Count,
            batchSize
        );

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = new GoogleRequests.BatchRequest(service: yt);
            var batchItems = batches[batchIndex];

            foreach (var (item, newPosition) in batchItems)
            {
                item.Snippet.Position = newPosition;
                var request = yt.PlaylistItems.Update(item, "snippet");
                var pos = newPosition;
                batch.Queue<PlaylistItem>(
                    request,
                    (_, error, _, _) =>
                    {
                        if (error is { })
                            batchFailures.Add((pos, error.Message ?? "unknown error"));
                    }
                );
            }

            Telemetry.Debug(
                "YouTube.SortPlaylist: executing batch {Index}/{Total} ({Count} items)",
                batchIndex + 1,
                batches.Count,
                batchItems.Count
            );

            await batch.ExecuteAsync(cancellationToken: ct);

            if (batchIndex >= batches.Count - 1)
                continue;

            Telemetry.Debug("YouTube.SortPlaylist: waiting 2s before next batch...");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        if (batchFailures.Count > 0)
        {
            Telemetry.Error(
                "YouTube.SortPlaylist: {Failed}/{Total} batch updates FAILED",
                batchFailures.Count,
                toUpdate.Count
            );
            foreach (var (idx, msg) in batchFailures)
                Telemetry.Error("Batch failure at position {Position}: {Error}", idx, msg);
        }

        var summary = await playlistService.GetPlaylistSummaryAsync(playlistId, ct);

        activity.Complete();
        Telemetry.Info(
            "YouTube.SortPlaylist complete — {Repositioned} repositioned, new ETag: {ETag}",
            toUpdate.Count,
            summary?.ETag ?? "unknown"
        );

        return (toUpdate.Count, summary?.ETag ?? "");
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
