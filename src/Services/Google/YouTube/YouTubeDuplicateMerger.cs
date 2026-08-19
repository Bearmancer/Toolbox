using System.Diagnostics;
using System.Text.Json;
using Core;
using ErrorOr;
using Google.Apis.YouTube.v3.Data;
using SerilogTracing;

namespace Services.Google.YouTube;

public readonly record struct DuplicateMergeOutcome(
	IReadOnlyList<PlaylistSnapshot> Survivors,
	IReadOnlyList<PlaylistSnapshot> RemovedLosers,
	IReadOnlySet<string> WinnersRequiringProcessing,
	int GroupsProcessed,
	int GroupsDeferred
);

public class YouTubeDuplicateMerger(YouTubePlaylistService playlistService)
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);
	private static readonly string MergeManifestDir = Path.Combine(StateRoot, "merge-manifests");
	private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");
	private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
	private static readonly string RawDir = Path.Combine(StateRoot, "raw");

	public async Task<DuplicateMergeOutcome> MergeDuplicateGroupsAsync(
		IReadOnlyList<PlaylistSnapshot> allCurrentPlaylists,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.MergeDuplicateGroups"
		);

		const int insertCap = 100;

		IReadOnlyList<DuplicatePlaylistGroup> groups = YouTubeDuplicateMergePolicy.FindGroups(
			allCurrentPlaylists
		);

		if (groups.Count == 0)
		{
			Telemetry.Debug(
				"Duplicate merge: 0 duplicate group(s) detected across {PlaylistCount} playlists",
				allCurrentPlaylists.Count
			);
			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			return new DuplicateMergeOutcome(
				Survivors: allCurrentPlaylists,
				RemovedLosers: [],
				WinnersRequiringProcessing: new HashSet<string>(),
				GroupsProcessed: 0,
				GroupsDeferred: 0
			);
		}

		Telemetry.Info(
			"Duplicate merge: {GroupCount} duplicate group(s) detected across {PlaylistCount} playlists",
			groups.Count,
			allCurrentPlaylists.Count
		);

		List<PlaylistSnapshot> survivors = [.. allCurrentPlaylists];
		List<PlaylistSnapshot> removedLosers = [];
		HashSet<string> winnersRequiringProcessing = [];
		var groupsProcessed = 0;
		var groupsDeferred = 0;

		foreach (DuplicatePlaylistGroup group in groups)
		{
			ct.ThrowIfCancellationRequested();

			Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
			Telemetry.Info(
				"Processing duplicate group '{Key}': {Count} playlist(s)",
				group.Key,
				group.Playlists.Count
			);

			PlaylistSnapshot winner = YouTubeDuplicateMergePolicy.SelectWinner(group.Playlists);
			List<PlaylistSnapshot> losers =
			[
				.. group.Playlists.Where(p => p.PlaylistId != winner.PlaylistId),
			];

			Telemetry.Debug(
				"Winner: '{Title}' ({Id}, {Count} videos), Losers: {LoserCount}",
				winner.Title,
				winner.PlaylistId,
				winner.ReportedVideoCount,
				losers.Count
			);

			var shouldDefer = false;

			List<PlaylistItem> winnerItems = await FetchItemsSafeAsync(
				winner.PlaylistId,
				winner.Title,
				ct
			);
			if (winnerItems is null)
			{
				groupsDeferred++;
				Telemetry.Warn("Deferred group '{Key}': failed to list winner items", group.Key);
				continue;
			}

			HashSet<string> winnerVideoIds = ExtractVideoIds(winnerItems);
			HashSet<string> allLoserVideoIds = [];

			foreach (PlaylistSnapshot loser in losers)
			{
				ct.ThrowIfCancellationRequested();

				List<PlaylistItem>? loserItems = await FetchItemsSafeAsync(
					loser.PlaylistId,
					loser.Title,
					ct
				);
				if (loserItems is null)
				{
					shouldDefer = true;
					Telemetry.Warn(
						"Deferred group '{Key}': failed to list loser items for '{LoserTitle}'",
						group.Key,
						loser.Title
					);
					break;
				}

				foreach (var id in ExtractVideoIds(loserItems))
					allLoserVideoIds.Add(id);

				TransferCandidateSet candidates = YouTubeDuplicateMergePolicy.GetTransferCandidates(
					winnerVideoIds,
					loserItems
				);

				if (candidates.HasInvalidItems)
				{
					shouldDefer = true;
					Telemetry.Warn(
						"Deferred group '{Key}': invalid items detected in loser '{LoserTitle}'",
						group.Key,
						loser.Title
					);
					break;
				}

				if (candidates.MissingVideoIds.Count > insertCap)
				{
					shouldDefer = true;
					Telemetry.Warn(
						"Deferred group '{Key}': {MissingCount} missing videos exceeds cap {Cap}",
						group.Key,
						candidates.MissingVideoIds.Count,
						insertCap
					);
					break;
				}

				if (candidates.MissingVideoIds.Count > 0)
				{
					Telemetry.Debug(
						"Inserting {Count} missing video(s) from '{LoserTitle}' into winner '{WinnerTitle}'",
						candidates.MissingVideoIds.Count,
						loser.Title,
						winner.Title
					);

					var insertFailed = false;
					foreach (var videoId in candidates.MissingVideoIds)
					{
						ct.ThrowIfCancellationRequested();

						ErrorOr<string> insertResult =
							await playlistService.InsertPlaylistItemAsync(
								winner.PlaylistId,
								videoId,
								ct
							);

						await Task.Delay(TimeSpan.FromMilliseconds(100), ct);

						if (insertResult.IsError)
						{
							Telemetry.Error(
								"Failed to insert video {VideoId} into playlist {PlaylistId}: {Error}",
								videoId,
								winner.PlaylistId,
								insertResult.FirstError.Description
							);
							insertFailed = true;
							break;
						}

						Telemetry.Debug(
							"Inserted video {VideoId} into playlist {PlaylistId}",
							videoId,
							winner.PlaylistId
						);
					}

					if (insertFailed)
					{
						shouldDefer = true;
						Telemetry.Warn(
							"Deferred group '{Key}': insert failure — skipping deletion",
							group.Key
						);
						break;
					}

					winnersRequiringProcessing.Add(winner.PlaylistId);
				}
			}

			if (shouldDefer)
			{
				groupsDeferred++;
				continue;
			}

			// Re-list winner and verify
			List<PlaylistItem>? reListedWinner = await FetchItemsSafeAsync(
				winner.PlaylistId,
				winner.Title,
				ct
			);
			if (reListedWinner is null)
			{
				groupsDeferred++;
				Telemetry.Warn(
					"Deferred group '{Key}': failed to re-list winner for verification",
					group.Key
				);
				continue;
			}

			HashSet<string> reListedVideoIds = ExtractVideoIds(reListedWinner);

			if (!YouTubeDuplicateMergePolicy.ContainsAll(reListedVideoIds, allLoserVideoIds))
			{
				groupsDeferred++;
				Telemetry.Warn(
					"Deferred group '{Key}': verification failed — winner does not contain all source videos",
					group.Key
				);
				continue;
			}

			// Delete losers
			var deleteFailed = false;
			foreach (PlaylistSnapshot loser in losers)
			{
				ct.ThrowIfCancellationRequested();

				ErrorOr<string> deleteResult = await playlistService.DeletePlaylistAsync(
					loser.PlaylistId,
					ct
				);

				if (deleteResult.IsError)
				{
					Telemetry.Error(
						"Failed to delete loser playlist {Title} ({Id}): {Error}",
						loser.Title,
						loser.PlaylistId,
						deleteResult.FirstError.Description
					);
					deleteFailed = true;
					break;
				}

				Telemetry.Info(
					"Deleted loser playlist: {Title} ({Id})",
					loser.Title,
					loser.PlaylistId
				);
			}

			if (deleteFailed)
			{
				groupsDeferred++;
				continue;
			}

			// Archive manifest after successful delete
			await ArchiveMergeManifestAsync(winner, losers, allLoserVideoIds, ct);

			// Archive local files only after successful delete
			foreach (PlaylistSnapshot loser in losers)
				ArchiveLocalFiles(loser);

			foreach (PlaylistSnapshot loser in losers)
			{
				survivors.Remove(losers.First(l => l.PlaylistId == loser.PlaylistId));
				removedLosers.Add(loser);
			}

			groupsProcessed++;
			stopwatch.Stop();
			Telemetry.Debug(
				"Group '{Key}' merged in {ElapsedMs}ms",
				group.Key,
				stopwatch.ElapsedMilliseconds
			);
		}

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
		Telemetry.Info(
			"Duplicate merge complete: {Processed} processed, {Deferred} deferred, {Removed} playlist(s) removed",
			groupsProcessed,
			groupsDeferred,
			removedLosers.Count
		);

		return new DuplicateMergeOutcome(
			Survivors: survivors,
			RemovedLosers: removedLosers,
			WinnersRequiringProcessing: winnersRequiringProcessing,
			GroupsProcessed: groupsProcessed,
			GroupsDeferred: groupsDeferred
		);
	}

	private async Task<List<PlaylistItem>> FetchItemsSafeAsync(
		string playlistId,
		string playlistTitle,
		CancellationToken ct
	)
	{
		try
		{
			return [.. await playlistService.GetPlaylistItemsAsync(playlistId, ct)];
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				"Failed to fetch items for playlist '{Title}' ({Id}): {Error}",
				playlistTitle,
				playlistId,
				ex.Message
			);
			return [];
		}
	}

	private static HashSet<string> ExtractVideoIds(IReadOnlyList<PlaylistItem> items)
	{
		HashSet<string> ids = [];
		foreach (PlaylistItem item in items)
		{
			var videoId = item.Snippet?.ResourceId?.VideoId;
			if (!string.IsNullOrEmpty(videoId))
				ids.Add(videoId);
		}
		return ids;
	}

	private async Task ArchiveMergeManifestAsync(
		PlaylistSnapshot winner,
		IReadOnlyList<PlaylistSnapshot> losers,
		IReadOnlySet<string> sourceVideoIds,
		CancellationToken ct
	)
	{
		MergeManifestRecord manifest = new()
		{
			WinnerId = winner.PlaylistId,
			WinnerTitle = winner.Title,
			Losers =
			[
				.. losers.Select(l => new LoserRecord
				{
					PlaylistId = l.PlaylistId,
					Title = l.Title,
					VideoCount = (int)l.ReportedVideoCount,
				}),
			],
			SourceVideoIds = [.. sourceVideoIds],
			SourceVideoCount = sourceVideoIds.Count,
			WinnerVideoCount = (int)winner.ReportedVideoCount,
			MergedAt = DateTimeOffset.UtcNow,
		};

		if (!Directory.Exists(MergeManifestDir))
			Directory.CreateDirectory(MergeManifestDir);

		var filename = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{winner.PlaylistId}.json";
		var path = Path.Combine(MergeManifestDir, filename);
		var json = JsonSerializer.Serialize(manifest, YouTubeFetchState.JsonOptions);
		await File.WriteAllTextAsync(path, json, ct);

		Telemetry.Debug("Archived merge manifest: {Path}", path);
	}

	private static void ArchiveLocalFiles(PlaylistSnapshot snapshot)
	{
		var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
		var filename = $"{sanitizedTitle}_{snapshot.PlaylistId}";

		if (!Directory.Exists(DeletedDir))
			Directory.CreateDirectory(DeletedDir);

		MoveFileIfExists(
			Path.Combine(ProcessedDir, $"{sanitizedTitle}.json"),
			Path.Combine(DeletedDir, $"{filename}.json")
		);

		MoveFileIfExists(
			Path.Combine(RawDir, $"{sanitizedTitle}.json"),
			Path.Combine(DeletedDir, $"{filename}-raw.json")
		);
	}

	private static void MoveFileIfExists(string source, string dest)
	{
		if (!File.Exists(source))
			return;

		File.Move(source, dest, true);
		Telemetry.Debug("Archived file: {Source} -> {Dest}", source, dest);
	}

	private sealed class MergeManifestRecord
	{
		public required string WinnerId { get; init; }
		public required string WinnerTitle { get; init; }
		public required List<LoserRecord> Losers { get; init; }
		public required HashSet<string> SourceVideoIds { get; init; }
		public required int SourceVideoCount { get; init; }
		public required int WinnerVideoCount { get; init; }
		public required DateTimeOffset MergedAt { get; init; }
	}

	private sealed class LoserRecord
	{
		public required string PlaylistId { get; init; }
		public required string Title { get; init; }
		public required int VideoCount { get; init; }
	}
}
