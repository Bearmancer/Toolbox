using Core;

namespace Services.Google.YouTube;

public readonly record struct ChangeDetectionResult(
    IReadOnlyList<PlaylistSnapshot> NewPlaylists,
    IReadOnlyList<PlaylistSnapshot> ChangedPlaylists,
    IReadOnlyList<PlaylistSnapshot> DeletedPlaylists,
    IReadOnlyList<PlaylistSnapshot> UnchangedPlaylists
);

public static class YouTubeChangeDetector
{

    public static ChangeDetectionResult DetectChanges(IReadOnlyList<PlaylistSnapshot> current, YouTubeFetchState stored)
    {
        var currentDict = current.ToDictionary(p => p.PlaylistId);
        var storedDict = stored.PlaylistSnapshots;

        var newPlaylists = new List<PlaylistSnapshot>();
        var changedPlaylists = new List<PlaylistSnapshot>();
        var deletedPlaylists = new List<PlaylistSnapshot>();
        var unchangedPlaylists = new List<PlaylistSnapshot>();

        foreach (var snapshot in current)
        {
            if (!storedDict.TryGetValue(snapshot.PlaylistId, out var storedSnapshot))
            {
                newPlaylists.Add(snapshot);
                continue;
            }

            var etagChanged =
                !string.IsNullOrEmpty(storedSnapshot.ETag)
                && !string.IsNullOrEmpty(snapshot.ETag)
                && storedSnapshot.ETag != snapshot.ETag;

            var countChanged = storedSnapshot.ReportedVideoCount != snapshot.ReportedVideoCount;

            if (etagChanged || countChanged)
                changedPlaylists.Add(snapshot);
            else
                unchangedPlaylists.Add(snapshot);
        }

        foreach (var kvp in storedDict)
            if (!currentDict.ContainsKey(kvp.Key))
                deletedPlaylists.Add(kvp.Value);

        Telemetry.Info(
            "Change detection: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged",
            newPlaylists.Count,
            changedPlaylists.Count,
            deletedPlaylists.Count,
            unchangedPlaylists.Count
        );

        return new ChangeDetectionResult(newPlaylists, changedPlaylists, deletedPlaylists, unchangedPlaylists);
    }
}
