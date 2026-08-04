using Google.Apis.YouTube.v3.Data;

namespace Services.Google.YouTube;

public readonly record struct DuplicatePlaylistGroup(
    string Key,
    IReadOnlyList<PlaylistSnapshot> Playlists
);

public readonly record struct TransferCandidateSet(
    IReadOnlyList<string> MissingVideoIds,
    bool HasInvalidItems
);

public static class YouTubeDuplicateMergePolicy
{
    public static IReadOnlyList<DuplicatePlaylistGroup> FindGroups(
        IReadOnlyList<PlaylistSnapshot> playlists
    ) =>
        [
            .. playlists
                .GroupBy(p => p.Title.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicatePlaylistGroup(g.Key, [.. g])),
        ];

    public static PlaylistSnapshot SelectWinner(IReadOnlyList<PlaylistSnapshot> group) =>
        group
            .OrderByDescending(p => p.ReportedVideoCount)
            .ThenBy(p => p.LastUpdated)
            .ThenBy(p => p.PlaylistId)
            .First();

    public static TransferCandidateSet GetTransferCandidates(
        IReadOnlySet<string> winnerVideoIds,
        IReadOnlyList<PlaylistItem> loserItems
    )
    {
        List<string> missingVideoIds = [];
        var hasInvalidItems = false;

        foreach (PlaylistItem item in loserItems)
        {
            var videoId = item.Snippet?.ResourceId?.VideoId;
            if (string.IsNullOrEmpty(videoId))
            {
                hasInvalidItems = true;
                continue;
            }

            if (winnerVideoIds.Contains(videoId))
                continue;

            if (!missingVideoIds.Contains(videoId))
                missingVideoIds.Add(videoId);
        }

        return new TransferCandidateSet(missingVideoIds, hasInvalidItems);
    }

    public static bool ContainsAll(
        IReadOnlySet<string> winnerVideoIds,
        IReadOnlySet<string> sourceVideoIds
    ) => sourceVideoIds.All(winnerVideoIds.Contains);
}
