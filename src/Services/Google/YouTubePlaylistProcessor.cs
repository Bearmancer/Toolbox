using System.Diagnostics;
using System.Text.Json;
using Core;
using Google.Apis.YouTube.v3.Data;
using Services.Google.Models;

namespace Services.Google;

public class YouTubePlaylistProcessor(
    YoutubeService youtubeService,
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

    public async Task<(int Videos, int Skipped, int AzureChars)> ProcessPlaylistAsync(
        PlaylistSnapshot snapshot,
        CancellationToken ct
    )
    {
        var playlistStopwatch = Stopwatch.StartNew();
        var sanitizedTitle = FileNameSanitizer.Sanitize(snapshot.Title);

        Telemetry.Debug("Processing playlist: {Title} ({Id})", snapshot.Title, snapshot.PlaylistId);

        var rawPages = await youtubeService.GetPlaylistItemPagesRawAsync(
            snapshot.PlaylistId,
            "snippet,contentDetails",
            ct
        );

        List<PlaylistItem> allItems = [.. rawPages.SelectMany(p => p.Items ?? [])];
        var rawPath = Path.Combine(RawDir, $"{sanitizedTitle}.json");
        await WriteJsonAsync(rawPath, allItems, ct);
        Telemetry.Debug("Saved {Count} items to raw/{Title}.json", allItems.Count, sanitizedTitle);

        var videoIds = allItems
            .Select(i => i.ContentDetails!.VideoId ?? i.Snippet!.ResourceId!.VideoId!)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var durations = await youtubeService.GetVideoDurationsAsync(videoIds, ct);
        var videos = new List<YouTubeVideo>();
        var skipped = 0;

        foreach (var item in allItems)
        {
            var videoId = item.ContentDetails!.VideoId ?? item.Snippet!.ResourceId!.VideoId!;

            if (!durations.TryGetValue(videoId, out var duration))
            {
                Telemetry.Debug(
                    "Skipping video {VideoId} — no duration available (deleted or private)",
                    videoId
                );
                skipped++;
                continue;
            }

            videos.Add(
                new YouTubeVideo
                {
                    Title = item.Snippet.Title!,
                    Description = item.Snippet.Description ?? "",
                    Duration = duration,
                    ChannelName = item.Snippet.VideoOwnerChannelTitle ?? item.Snippet.ChannelTitle!,
                    VideoId = item.ContentDetails?.VideoId ?? item.Snippet.ResourceId?.VideoId!,
                    ChannelId = item.Snippet.VideoOwnerChannelId ?? item.Snippet.ChannelId!,
                }
            );
        }

        var playlistPath = Path.Combine(ProcessedDir, $"{sanitizedTitle}.json");
        var existingVideos = await LoadExistingVideosAsync(playlistPath, ct);
        var existingDict = new Dictionary<string, YouTubeVideo>();
        foreach (var video in existingVideos)
            existingDict.TryAdd(video.VideoId, video);

        var incomingIds = videos.Select(v => v.VideoId).ToHashSet();
        if (existingVideos.Count == 0)
            Telemetry.Info("  fresh sync: {Count} videos", videos.Count);
        else
        {
            var added = incomingIds.Except(existingDict.Keys).Count();
            var removed = existingDict.Keys.Except(incomingIds).Count();
            var net = added - removed;
            var netStr = net switch
            {
                > 0 => $"+{net}",
                0 => "net 0",
                _ => $"{net}",
            };
            Telemetry.Info(
                "  update sync: {Added} added, {Removed} removed ({Net}), {Total} total",
                added,
                removed,
                netStr,
                videos.Count
            );

            for (var i = 0; i < videos.Count; i++)
            {
                var video = videos[i];
                if (
                    existingDict.TryGetValue(video.VideoId, out var existing)
                    && existing.TranslatedTitle is { }
                    && existing.TranslatedDescription is { }
                    && string.Equals(existing.Title, video.Title, StringComparison.Ordinal)
                    && string.Equals(
                        existing.Description,
                        video.Description,
                        StringComparison.Ordinal
                    )
                )
                    videos[i] = video with
                    {
                        TranslatedTitle = existing.TranslatedTitle,
                        TranslatedDescription = existing.TranslatedDescription,
                        DetectedLanguage = existing.DetectedLanguage,
                    };
            }
        }

        var cachedVideos = videos
            .Where(v => v.TranslatedTitle is { } && v.TranslatedDescription is { })
            .ToList();

        if (cachedVideos.Count > 0)
        {
            var langGroups = cachedVideos
                .GroupBy(v => v.DetectedLanguage ?? "unknown")
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");
            Telemetry.Info(
                "  cache: {Count}/{Total} videos from previous run ({LangSummary})",
                cachedVideos.Count,
                videos.Count,
                string.Join(", ", langGroups)
            );
        }

        var (translatedVideos, azureChars) = await translationService.TranslateVideosAsync(
            videos,
            ct,
            async (currentVideos, checkpointCt) =>
            {
                Telemetry.Debug(
                    "Checkpointing processed file: {Path} ({Count} videos)",
                    playlistPath,
                    currentVideos.Count
                );
                await WriteJsonAsync(playlistPath, currentVideos, checkpointCt);
            }
        );

        Telemetry.Debug(
            "Wrote processed file: {Path} ({Count} videos)",
            playlistPath,
            translatedVideos.Count
        );

        playlistStopwatch.Stop();
        Telemetry.Info(
            "  done — {Count} videos, {Skipped} skipped in {Elapsed}s",
            translatedVideos.Count,
            skipped,
            playlistStopwatch.Elapsed.TotalSeconds
        );

        return (translatedVideos.Count, skipped, azureChars);
    }

    private static async Task<List<YouTubeVideo>> LoadExistingVideosAsync(
        string processedPath,
        CancellationToken ct
    )
    {
        if (!File.Exists(processedPath))
            return [];

        try
        {
            await using var stream = File.OpenRead(processedPath);
            return await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
                stream,
                YouTubeFetchState.JsonOptions,
                ct
            ) ?? [];
        }
        catch (JsonException ex)
        {
            Telemetry.Error(
                "Invalid JSON in processed file {Path}: {Error}",
                processedPath,
                ex.Message
            );
            return [];
        }
        catch (FormatException ex)
        {
            Telemetry.Error(
                "Invalid data in processed file {Path}: {Error}",
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
}
