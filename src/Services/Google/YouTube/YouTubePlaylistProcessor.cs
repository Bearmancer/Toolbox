using System.Diagnostics;
using System.Text.Json;
using Core;
using ErrorOr;
using Google.Apis.YouTube.v3.Data;

namespace Services.Google.YouTube;

public class YouTubePlaylistProcessor(
	YouTubePlaylistService playlistService,
	YouTubeVideoService videoService,
	YouTubeTranslationService translationService
)
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);
	private static readonly string RawDir = Path.Combine(StateRoot, "raw");
	private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");

	public async Task<ErrorOr<ProcessResult>> ProcessPlaylistAsync(
		PlaylistSnapshot snapshot,
		CancellationToken ct
	)
	{
		var stopwatch = Stopwatch.StartNew();
		PlaylistProcessContext ctx = new(
			snapshot,
			Text.SanitizeFileName(snapshot.Title),
			Path.Combine(RawDir, $"{Text.SanitizeFileName(snapshot.Title)}.json"),
			Path.Combine(ProcessedDir, $"{Text.SanitizeFileName(snapshot.Title)}.json")
		);

		Telemetry.Debug("Processing playlist: {Title} ({Id})", snapshot.Title, snapshot.PlaylistId);

		ErrorOr<ProcessResult> result = await FetchItemsAsync(ctx, ct)
			.ThenAsync(items => BuildVideoListAsync(items, ct))
			.ThenAsync(videoCtx =>
				MergeWithSkippedCountAsync(videoCtx.Videos, videoCtx.Skipped, ctx, ct)
			)
			.ThenAsync(async state =>
			{
				return await translationService
					.TranslateVideosAsync(
						state.Videos,
						ct,
						async (currentVideos, checkpointCt) =>
						{
							Telemetry.Debug(
								"Checkpointing processed file: {Path} ({Count} videos)",
								ctx.ProcessedPath,
								currentVideos.Count
							);
							await WriteJsonAsync(ctx.ProcessedPath, currentVideos, checkpointCt);
						}
					)
					.Then(r =>
						new ProcessResult(r.Videos.Count, state.Skipped, r.AzureChars, state.NewVideoCount)
					);
			});

		if (result.IsSuccess)
			Telemetry.Debug(
				"Done — {Count} videos, {Skipped} skipped in {Elapsed}s",
				result.Value.Videos,
				result.Value.Skipped,
				stopwatch.Elapsed.TotalSeconds
			);

		return result;
	}

	public async Task<ErrorOr<int>> RefreshLocalStateAsync(
		PlaylistSnapshot snapshot,
		CancellationToken ct
	)
	{
		PlaylistProcessContext ctx = new(
			snapshot,
			Text.SanitizeFileName(snapshot.Title),
			Path.Combine(RawDir, $"{Text.SanitizeFileName(snapshot.Title)}.json"),
			Path.Combine(ProcessedDir, $"{Text.SanitizeFileName(snapshot.Title)}.json")
		);

		Telemetry.Debug(
			"Refreshing local state for {Title} to reflect sorted order...",
			snapshot.Title
		);

		ErrorOr<int> result = await FetchItemsAsync(ctx, ct)
			.ThenAsync(items => BuildVideoListAsync(items, ct))
			.ThenAsync(videoCtx => MergeAndWriteAsync(videoCtx.Videos, ctx, ct));

		if (result.IsSuccess)
			Telemetry.Debug(
				"Local state refreshed for {Title}: {Videos} videos",
				snapshot.Title,
				result.Value
			);

		return result;
	}

	private async Task<ErrorOr<List<PlaylistItem>>> FetchItemsAsync(
		PlaylistProcessContext ctx,
		CancellationToken ct
	)
	{
		try
		{
			IReadOnlyList<PlaylistItemListResponse> rawPages =
				await playlistService.GetPlaylistItemPagesRawAsync(
					ctx.Snapshot.PlaylistId,
					"snippet,contentDetails",
					ct
				);
			List<PlaylistItem> items = [.. rawPages.SelectMany(p => p.Items ?? [])];
			await WriteJsonAsync(ctx.RawPath, items, ct);
			Telemetry.Debug(
				"Saved {Count} items to raw/{Title}.json",
				items.Count,
				ctx.SanitizedTitle
			);
			return items;
		}
		catch (Exception ex)
		{
			return Errors.YouTube.ApiError(ex.Message);
		}
	}

	private async Task<ErrorOr<BuildVideoResult>> BuildVideoListAsync(
		List<PlaylistItem> items,
		CancellationToken ct
	)
	{
		var videoIds = items
			.Select(i => i.ContentDetails.VideoId ?? i.Snippet.ResourceId.VideoId)
			.Where(id => !string.IsNullOrEmpty(id))
			.ToList();

		ErrorOr<Dictionary<string, TimeSpan>> durationsResult =
			await videoService.GetVideoDurationsAsync(videoIds, ct);
		if (durationsResult.IsError)
			return durationsResult.FirstError;

		Dictionary<string, TimeSpan> durations = durationsResult.Value;
		List<YouTubeVideo> videos = [];
		var skipped = 0;

		foreach (PlaylistItem item in items)
		{
			var videoId = item.ContentDetails.VideoId ?? item.Snippet.ResourceId.VideoId;
			if (!durations.TryGetValue(videoId, out TimeSpan duration))
			{
				Telemetry.Debug("Skipping video {VideoId} — no duration available", videoId);
				skipped++;
				continue;
			}

			videos.Add(
				new YouTubeVideo
				{
					Title = item.Snippet.Title,
					Description = item.Snippet.Description ?? "",
					Duration = duration,
					VideoId = videoId,
					ChannelName = item.Snippet.VideoOwnerChannelTitle ?? item.Snippet.ChannelTitle,
					ChannelId = item.Snippet.VideoOwnerChannelId ?? item.Snippet.ChannelId,
				}
			);
		}

		return new BuildVideoResult(videos, skipped);
	}

	private async Task<ErrorOr<MergeResult>> MergeWithSkippedCountAsync(
		List<YouTubeVideo> videos,
		int skipped,
		PlaylistProcessContext ctx,
		CancellationToken ct
	)
	{
		ErrorOr<MergeCacheResult> mergeResult = await MergeCacheAsync(videos, ctx, ct);
		return mergeResult.IsError
			? mergeResult.FirstError
			: new MergeResult(mergeResult.Value.Videos, skipped, mergeResult.Value.NewVideoCount);
	}

	private async Task<ErrorOr<int>> MergeAndWriteAsync(
		List<YouTubeVideo> videos,
		PlaylistProcessContext ctx,
		CancellationToken ct
	)
	{
		ErrorOr<MergeCacheResult> mergeResult = await MergeCacheAsync(videos, ctx, ct);
		if (mergeResult.IsError)
			return mergeResult.FirstError;

		await WriteJsonAsync(ctx.ProcessedPath, mergeResult.Value.Videos, ct);
		return mergeResult.Value.Videos.Count;
	}

	private static async Task<ErrorOr<MergeCacheResult>> MergeCacheAsync(
		List<YouTubeVideo> videos,
		PlaylistProcessContext ctx,
		CancellationToken ct
	)
	{
		List<YouTubeVideo> existingVideos = await LoadExistingVideosAsync(ctx.ProcessedPath, ct);
		Dictionary<string, YouTubeVideo> existingDict = [];
		foreach (YouTubeVideo video in existingVideos)
			existingDict.TryAdd(video.VideoId, video);

		var incomingIds = videos.Select(v => v.VideoId).ToHashSet();
		var added = incomingIds.Except(existingDict.Keys).Count();

		if (existingVideos.Count > 0)
		{
			var removed = existingDict.Keys.Except(incomingIds).Count();
			var net = added - removed;
			Telemetry.Debug(
				"Update sync: {Added} added, {Removed} removed ({Net}), {Total} total",
				added,
				removed,
				net switch
				{
					> 0 => $"+{net}",
					0 => "net 0",
					_ => $"{net}",
				},
				videos.Count
			);
		}
		else
			Telemetry.Debug("Fresh sync: {Count} videos", videos.Count);

		for (var i = 0; i < videos.Count; i++)
		{
			YouTubeVideo video = videos[i];
			if (
				existingDict.TryGetValue(video.VideoId, out YouTubeVideo? existing)
				&& existing.TranslatedTitle is { }
				&& existing.TranslatedDescription is { }
				&& existing.Title.IsEqualTo(video.Title)
				&& existing.Description.IsEqualTo(video.Description)
			)
				videos[i] = video with
				{
					TranslatedTitle = existing.TranslatedTitle,
					TranslatedDescription = existing.TranslatedDescription,
					DetectedLanguage = existing.DetectedLanguage,
				};
		}

		var cachedCount = videos.Count(v =>
			v.TranslatedTitle is { } && v.TranslatedDescription is { }
		);
		if (cachedCount > 0)
		{
			IEnumerable<string> langGroups = CachedVideosSummary(videos);
			Telemetry.Debug(
				"Cache: {Count}/{Total} videos from previous run ({LangSummary})",
				cachedCount,
				videos.Count,
				string.Join(", ", langGroups)
			);
		}

		return new MergeCacheResult(videos, added);
	}

	private static IEnumerable<string> CachedVideosSummary(List<YouTubeVideo> videos) =>
		videos
			.Where(v => v.TranslatedTitle is { } && v.TranslatedDescription is { })
			.GroupBy(v => v.DetectedLanguage ?? "unknown")
			.OrderByDescending(g => g.Count())
			.Select(g => $"{g.Count()} {g.Key}");

	private static async Task<List<YouTubeVideo>> LoadExistingVideosAsync(
		string processedPath,
		CancellationToken ct
	)
	{
		if (!File.Exists(processedPath))
			return [];

		try
		{
			await using FileStream stream = File.OpenRead(processedPath);
			return await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
					stream,
					YouTubeFetchState.JsonOptions,
					ct
				) ?? [];
		}
		catch (Exception ex) when (ex is JsonException or FormatException)
		{
			Telemetry.Error(
				"Invalid JSON in processed file {Path}: {Error}",
				processedPath,
				ex.Message
			);
			return [];
		}
	}

	private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(value, YouTubeFetchState.JsonOptions);
		await File.WriteAllTextAsync(path, json, ct);
	}

	public readonly record struct ProcessResult(
		int Videos,
		int Skipped,
		int AzureChars,
		int NewVideoCount
	);

	public readonly record struct BuildVideoResult(List<YouTubeVideo> Videos, int Skipped);

	private readonly record struct MergeResult(
		List<YouTubeVideo> Videos,
		int Skipped,
		int NewVideoCount
	);

	private readonly record struct MergeCacheResult(List<YouTubeVideo> Videos, int NewVideoCount);

	private sealed record PlaylistProcessContext(
		PlaylistSnapshot Snapshot,
		string SanitizedTitle,
		string RawPath,
		string ProcessedPath
	);
}
