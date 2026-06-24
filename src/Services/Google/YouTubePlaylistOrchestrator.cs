using System.Diagnostics;
using System.Text.Json;
using Core;
using Services.Google.Models;

namespace Services.Google;

public class YouTubePlaylistOrchestrator(
    YoutubeService youtubeService,
    YouTubeTranslationService translationService)
{
    private static readonly string StateRoot = Path.Combine(Directory.GetCurrentDirectory(), "state", "youtube");
    private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");
    private static readonly string RawDir = Path.Combine(StateRoot, "raw");
    private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
    private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");
    public async Task ExecuteAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        var syncStopwatch = Stopwatch.StartNew();

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
        var current = await youtubeService.GetPlaylistSummariesAsync(ct);

        Telemetry.Debug("Fetched {Count} playlist summaries from API", current.Count);

        var (newPlaylists, changedPlaylists, deletedPlaylists, unchangedPlaylists) =
            YouTubeChangeDetector.DetectChanges(current, stored);

        foreach (var snapshot in deletedPlaylists)
            ArchivePlaylist(snapshot);

        var playlistsToProcess = new List<PlaylistSnapshot>();
        playlistsToProcess.AddRange(newPlaylists);
        playlistsToProcess.AddRange(changedPlaylists);

        var totalVideos = 0;
        var skippedVideos = 0;
        var playlistStopwatch = Stopwatch.StartNew();

        var playlistIndex = 0;
        foreach (var snapshot in playlistsToProcess)
        {
            if (ct.IsCancellationRequested)
                break;

            playlistIndex++;
            Telemetry.Info(
                "[{Index}/{Total}] {Title}",
                playlistIndex,
                playlistsToProcess.Count,
                snapshot.Title);

            var (videos, skipped) = await ProcessPlaylistAsync(snapshot, ct);
            totalVideos += videos;
            skippedVideos += skipped;
        }

        syncStopwatch.Stop();

        var newState = new YouTubeFetchState
        {
            PlaylistSnapshots = current.ToDictionary(p => p.PlaylistId),
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            FetchComplete = !ct.IsCancellationRequested,
        };

        await YouTubeFetchState.SaveAsync(ManifestFile, newState, ct);

        Telemetry.Info(
            "Sync complete in {Elapsed}s: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged | {TotalVideos} videos ({Skipped} skipped) | {Quota} quota units | {PlaylistsProcessed}/{PlaylistsTotal} playlists",
            syncStopwatch.Elapsed.TotalSeconds,
            newPlaylists.Count,
            changedPlaylists.Count,
            deletedPlaylists.Count,
            unchangedPlaylists.Count,
            totalVideos,
            skippedVideos,
            youtubeService.QuotaUsed,
            playlistsToProcess.Count,
            current.Count);
    }

    public async Task ExecuteForPlaylistTitleAsync(string title, CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);

        var match = stored.PlaylistSnapshots.Values
            .FirstOrDefault(s => s.Title.ContainsIgnore(title));

        if (match is null)
        {
            var summaries = await youtubeService.GetPlaylistSummariesAsync(ct);
            match = summaries.FirstOrDefault(s => s.Title.ContainsIgnore(title));
        }
        else
        {
            Telemetry.Debug("Cached ID for {Title} (skipped Playlists.list)", match.Title);
        }

        if (match is null)
        {
            Telemetry.Error("Playlist not found: {Title}", title);
            return;
        }

        var (videos, skipped) = await ProcessPlaylistAsync(match, ct);

        stored = stored with
        {
            PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots)
            {
                [match.PlaylistId] = match
            },
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            FetchComplete = true,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);

        Telemetry.Info(
            "Synced playlist {Title}: {Videos} videos ({Skipped} skipped) | {Quota} quota units",
            match.Title, videos, skipped, youtubeService.QuotaUsed);
    }

    private async Task<(int Videos, int Skipped)> ProcessPlaylistAsync(PlaylistSnapshot snapshot, CancellationToken ct)
    {
        var playlistStopwatch = Stopwatch.StartNew();
        var quotaBefore = youtubeService.QuotaUsed;
        var sanitizedTitle = FileNameSanitizer.Sanitize(snapshot.Title);

        Telemetry.Debug("Processing playlist: {Title} ({Id})", snapshot.Title, snapshot.PlaylistId);

        var rawPages = await youtubeService.GetPlaylistItemPagesRawAsync(
            snapshot.PlaylistId, "snippet,contentDetails", ct);

        var allItems = rawPages.SelectMany(p => p.Items ?? []).ToList();

        var rawPath = Path.Combine(RawDir, $"{sanitizedTitle}.json");
        var rawJson = JsonSerializer.Serialize(allItems, YouTubeFetchState.JsonOptions);
        await File.WriteAllTextAsync(rawPath, rawJson, ct);

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
                Telemetry.Debug("Skipping video {VideoId} — no duration available (deleted or private)", videoId);
                skipped++;
                continue;
            }

            videos.Add(new YouTubeVideo
            {
                Title = item.Snippet.Title!,
                Description = item.Snippet.Description ?? "",
                Duration = duration,
                ChannelName = item.Snippet.VideoOwnerChannelTitle ?? item.Snippet.ChannelTitle!,
                VideoId = item.ContentDetails?.VideoId ?? item.Snippet.ResourceId?.VideoId!,
                ChannelId = item.Snippet.VideoOwnerChannelId ?? item.Snippet.ChannelId!
            });
        }

        var playlistPath = Path.Combine(ProcessedDir, $"{sanitizedTitle}.json");
        var existingIds = await LoadExistingVideoIdsAsync(playlistPath, ct);
        var incomingIds = videos.Select(v => v.VideoId).ToHashSet();

        if (existingIds.Count == 0)
        {
            Telemetry.Info("  fresh sync: {Count} videos", videos.Count);
        }
        else
        {
            var added = incomingIds.Except(existingIds).Count();
            var removed = existingIds.Except(incomingIds).Count();
            var net = added - removed;
            var netStr = net switch { > 0 => $"+{net}", 0 => "net 0", _ => $"{net}" };
            Telemetry.Info(
                "  update sync: {Added} added, {Removed} removed ({Net}), {Total} total",
                added, removed, netStr, videos.Count);
        }

        Telemetry.Debug("Translating {Count} videos for {Title}", videos.Count, snapshot.Title);

        videos = await translationService.TranslateVideosAsync(videos, ct);

        Telemetry.Debug("Writing processed file: {Path} ({Count} videos)", playlistPath, videos.Count);

        var playlistJson = JsonSerializer.Serialize(videos, YouTubeFetchState.JsonOptions);
        await File.WriteAllTextAsync(playlistPath, playlistJson, ct);

        Telemetry.Debug("Wrote {Size} bytes to {Path}", playlistJson.Length, playlistPath);

        playlistStopwatch.Stop();
        var quotaUsed = youtubeService.QuotaUsed - quotaBefore;

        Telemetry.Info(
            "  done — {Count} videos, {Skipped} skipped in {Elapsed:F1}s ({Quota} quota units)",
            videos.Count,
            skipped,
            playlistStopwatch.Elapsed.TotalSeconds,
            quotaUsed);

        return (videos.Count, skipped);
    }

    private static void ArchivePlaylist(PlaylistSnapshot snapshot)
    {
        var sanitizedTitle = FileNameSanitizer.Sanitize(snapshot.Title);
        var sourcePath = Path.Combine(ProcessedDir, $"{sanitizedTitle}.json");
        var destPath = Path.Combine(DeletedDir, $"{sanitizedTitle}.json");

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destPath, overwrite: true);
            Telemetry.Info("Archived deleted playlist: {Title}", snapshot.Title);
        }
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(RawDir);
        Directory.CreateDirectory(ProcessedDir);
        Directory.CreateDirectory(DeletedDir);
    }

    private static async Task<HashSet<string>> LoadExistingVideoIdsAsync(string processedPath, CancellationToken ct)
    {
        if (!File.Exists(processedPath))
            return [];

        try
        {
            await using var stream = File.OpenRead(processedPath);
            var existing = await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
                stream, YouTubeFetchState.JsonOptions, ct);
            return existing?.Select(v => v.VideoId).ToHashSet() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
