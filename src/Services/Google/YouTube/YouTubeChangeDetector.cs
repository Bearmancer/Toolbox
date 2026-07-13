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
	public static ChangeDetectionResult DetectChanges(
		IReadOnlyList<PlaylistSnapshot> current,
		YouTubeFetchState stored
	)
	{
		var currentDict = current.ToDictionary(p => p.PlaylistId);
		Dictionary<string, PlaylistSnapshot> storedDict = stored.PlaylistSnapshots;

		List<PlaylistSnapshot> newPlaylists = [];
		List<PlaylistSnapshot> changedPlaylists = [];
		List<PlaylistSnapshot> deletedPlaylists = [];
		List<PlaylistSnapshot> unchangedPlaylists = [];

		foreach (PlaylistSnapshot snapshot in current)
		{
			if (!storedDict.TryGetValue(snapshot.PlaylistId, out PlaylistSnapshot? storedSnapshot))
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

		foreach (KeyValuePair<string, PlaylistSnapshot> kvp in storedDict)
			if (!currentDict.ContainsKey(kvp.Key))
				deletedPlaylists.Add(kvp.Value);

		Telemetry.Info(
			"Change detection: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged",
			newPlaylists.Count,
			changedPlaylists.Count,
			deletedPlaylists.Count,
			unchangedPlaylists.Count
		);

		return new(newPlaylists, changedPlaylists, deletedPlaylists, unchangedPlaylists);
	}
}
