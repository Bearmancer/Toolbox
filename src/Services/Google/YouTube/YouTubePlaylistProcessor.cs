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
    public readonly record struct ProcessResult(int Videos, int Skipped, int AzureChars);
    private readonly record struct MergeResult(List<YouTubeVideo> Videos, int Skipped);
    private record PlaylistProcessContext(PlaylistSnapshot Snapshot, string SanitizedTitle, string RawPath, string ProcessedPath);

    public async Task<ErrorOr<ProcessResult>> ProcessPlaylistAsync(
        PlaylistSnapshot snapshot,
        CancellationToken ct
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var ctx = new PlaylistProcessContext(
            snapshot,
            Text.SanitizeFileName(snapshot.Title),
            Path.Combine(YouTubePaths.RawDir, $"{Text.SanitizeFileName(snapshot.Title)}.json"),
            Path.Combine(YouTubePaths.ProcessedDir, $"{Text.SanitizeFileName(snapshot.Title)}.json")
        );

        Telemetry.Debug("Processing playlist: {Title} ({Id})", snapshot.Title, snapshot.PlaylistId);

        var result = await FetchItemsAsync(ctx, ct)
            .ThenAsync(items => BuildVideoListAsync(items, ctx, ct))
            .ThenAsync(async videoCtx =>
            {
                var videos = (await MergeCacheAsync(videoCtx.videos, ctx, ct)).Value;
                return new MergeResult(videos, videoCtx.skipped);
            })
            .ThenAsync(async state =>
            {
                return await translationService.TranslateVideosAsync(
                    state.Videos,
                    ct,
                    async (currentVideos, checkpointCt) =>
                    {
                        Telemetry.Debug("Checkpointing processed file: {Path} ({Count} videos)", ctx.ProcessedPath, currentVideos.Count);
                        await WriteJsonAsync(ctx.ProcessedPath, currentVideos, checkpointCt);
                    }
                ).Then(r => new ProcessResult(r.Videos.Count, state.Skipped, r.AzureChars));
            });

        if (result.IsSuccess)
        {
            Telemetry.Info("Done — {Count} videos, {Skipped} skipped in {Elapsed}s",
                result.Value.Videos, result.Value.Skipped, stopwatch.Elapsed.TotalSeconds);
        }

        return result;
    }

    public async Task<ErrorOr<int>> RefreshLocalStateAsync(
        PlaylistSnapshot snapshot,
        CancellationToken ct
    )
    {
        var ctx = new PlaylistProcessContext(
            snapshot,
            Text.SanitizeFileName(snapshot.Title),
            Path.Combine(YouTubePaths.RawDir, $"{Text.SanitizeFileName(snapshot.Title)}.json"),
            Path.Combine(YouTubePaths.ProcessedDir, $"{Text.SanitizeFileName(snapshot.Title)}.json")
        );

        Telemetry.Info("Refreshing local state for {Title} to reflect sorted order...", snapshot.Title);

        var result = await FetchItemsAsync(ctx, ct)
            .ThenAsync(items => BuildVideoListAsync(items, ctx, ct))
            .ThenAsync(async videoCtx =>
            {
                var videos = (await MergeCacheAsync(videoCtx.videos, ctx, ct)).Value;
                await WriteJsonAsync(ctx.ProcessedPath, videos, ct);
                return videos.Count;
            });

        if (result.IsSuccess)
        {
            Telemetry.Info("Local state refreshed for {Title}: {Videos} videos", snapshot.Title, result.Value);
        }

        return result;
    }

    private async Task<ErrorOr<List<PlaylistItem>>> FetchItemsAsync(PlaylistProcessContext ctx, CancellationToken ct)
    {
        try
        {
            var rawPages = await playlistService.GetPlaylistItemPagesRawAsync(ctx.Snapshot.PlaylistId, "snippet,contentDetails", ct);
            List<PlaylistItem> items = [.. rawPages.SelectMany(p => p.Items ?? [])];
            await WriteJsonAsync(ctx.RawPath, items, ct);
            Telemetry.Debug("Saved {Count} items to raw/{Title}.json", items.Count, ctx.SanitizedTitle);
            return items;
        }
        catch (Exception ex)
        {
            return Errors.YouTube.ApiError(ex.Message);
        }
    }

    private async Task<ErrorOr<(List<YouTubeVideo> videos, int skipped)>> BuildVideoListAsync(List<PlaylistItem> items, PlaylistProcessContext ctx, CancellationToken ct)
    {
        var videoIds = items
            .Select(i => i.ContentDetails!.VideoId ?? i.Snippet!.ResourceId!.VideoId!)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var durationsResult = await videoService.GetVideoDurationsAsync(videoIds, ct);
        if (durationsResult.IsError)
            return durationsResult.FirstError;

        var durations = durationsResult.Value;
        var videos = new List<YouTubeVideo>();
        var skipped = 0;

        foreach (var item in items)
        {
            var videoId = item.ContentDetails!.VideoId ?? item.Snippet!.ResourceId!.VideoId!;
            if (!durations.TryGetValue(videoId, out var duration))
            {
                Telemetry.Debug("Skipping video {VideoId} — no duration available", videoId);
                skipped++;
                continue;
            }

            videos.Add(new YouTubeVideo
            {
                Title = item.Snippet.Title!,
                Description = item.Snippet.Description ?? "",
                Duration = duration,
                VideoId = videoId,
                ChannelName = item.Snippet.VideoOwnerChannelTitle ?? item.Snippet.ChannelTitle!,
                ChannelId = item.Snippet.VideoOwnerChannelId ?? item.Snippet.ChannelId!,
            });
        }

        return (videos, skipped);
    }

    private async Task<ErrorOr<List<YouTubeVideo>>> MergeCacheAsync(List<YouTubeVideo> videos, PlaylistProcessContext ctx, CancellationToken ct)
    {
        var existingVideos = await LoadExistingVideosAsync(ctx.ProcessedPath, ct);
        var existingDict = new Dictionary<string, YouTubeVideo>();
        foreach (var video in existingVideos)
            existingDict.TryAdd(video.VideoId, video);

        var incomingIds = videos.Select(v => v.VideoId).ToHashSet();
        
        if (existingVideos.Count > 0)
        {
            var added = incomingIds.Except(existingDict.Keys).Count();
            var removed = existingDict.Keys.Except(incomingIds).Count();
            var net = added - removed;
            Telemetry.Info("Update sync: {Added} added, {Removed} removed ({Net}), {Total} total", 
                added, removed, net switch { > 0 => $"+{net}", 0 => "net 0", _ => $"{net}" }, videos.Count);
        }
        else
        {
            Telemetry.Info("Fresh sync: {Count} videos", videos.Count);
        }

        for (var i = 0; i < videos.Count; i++)
        {
            var video = videos[i];
            if (existingDict.TryGetValue(video.VideoId, out var existing)
                && existing.TranslatedTitle is { }
                && existing.TranslatedDescription is { }
                && existing.Title.IsEqualTo(video.Title)
                && existing.Description.IsEqualTo(video.Description))
            {
                videos[i] = video with
                {
                    TranslatedTitle = existing.TranslatedTitle,
                    TranslatedDescription = existing.TranslatedDescription,
                    DetectedLanguage = existing.DetectedLanguage,
                };
            }
        }

        var cachedCount = videos.Count(v => v.TranslatedTitle is { } && v.TranslatedDescription is { });
        if (cachedCount > 0)
        {
            var langGroups = cachedVideosSummary(videos);
            Telemetry.Info("Cache: {Count}/{Total} videos from previous run ({LangSummary})", 
                cachedCount, videos.Count, string.Join(", ", langGroups));
        }

        return videos;
    }

    private static IEnumerable<string> cachedVideosSummary(List<YouTubeVideo> videos) =>
        videos.Where(v => v.TranslatedTitle is { } && v.TranslatedDescription is { })
              .GroupBy(v => v.DetectedLanguage ?? "unknown")
              .OrderByDescending(g => g.Count())
              .Select(g => $"{g.Count()} {g.Key}");

    private static async Task<List<YouTubeVideo>> LoadExistingVideosAsync(string processedPath, CancellationToken ct)
    {
        if (!File.Exists(processedPath)) return [];
        try
        {
            await using var stream = File.OpenRead(processedPath);
            return await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(stream, YouTubeFetchState.JsonOptions, ct) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            Telemetry.Error("Invalid JSON in processed file {Path}: {Error}", processedPath, ex.Message);
            return [];
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, YouTubeFetchState.JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
