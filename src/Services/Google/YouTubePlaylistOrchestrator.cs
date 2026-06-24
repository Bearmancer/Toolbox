using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Azure;
using Core;
using Services.Google.Models;
using GoogleApiException = Google.GoogleApiException;

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

        List<PlaylistSnapshot> playlistsToProcess = [.. newPlaylists, .. changedPlaylists];

        if (playlistsToProcess.Count == 0)
        {
            Telemetry.Info("No playlists need updating — everything is current ({Unchanged} playlists unchanged, {Deleted} deleted)", unchangedPlaylists.Count, deletedPlaylists.Count);
            return;
        }

        var totalVideos = 0;
        var skippedVideos = 0;

        Dictionary<string, PlaylistSnapshot> updatedSnapshots = new(stored.PlaylistSnapshots);
        foreach (var snapshot in deletedPlaylists)
            updatedSnapshots.Remove(snapshot.PlaylistId);

        var currentMonth = DateTimeOffset.UtcNow.Month;
        var azureCharsUsed = stored.AzureCharsMonth == currentMonth ? stored.AzureCharsUsed : 0;
        Telemetry.Info("Azure Translator: {Used} chars used this month (2,000,000 free tier)", azureCharsUsed);

        List<PlaylistSnapshot> processedSnapshots = [];
        var playlistIndex = 0;
        foreach (var snapshot in playlistsToProcess)
        {
            ct.ThrowIfCancellationRequested();

            playlistIndex++;
            Telemetry.Info(
                "[{Index}/{Total}] {Title}",
                playlistIndex,
                playlistsToProcess.Count,
                snapshot.Title);

            try
            {
                var (videos, skipped, azureChars) = await ProcessPlaylistAsync(snapshot, ct);
                totalVideos += videos;
                skippedVideos += skipped;
                azureCharsUsed += azureChars;
                processedSnapshots.Add(snapshot);

                updatedSnapshots[snapshot.PlaylistId] = snapshot;
                await YouTubeFetchState.SaveAsync(ManifestFile, new YouTubeFetchState
                {
                    PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(updatedSnapshots),
                    LastChecked = DateTimeOffset.UtcNow,
                    LastUpdated = DateTimeOffset.UtcNow,
                    FetchComplete = false,
                    AzureCharsUsed = azureCharsUsed,
                    AzureCharsMonth = currentMonth,
                }, ct);
            }
            catch (GoogleApiException ex)
            {
                if (ex.HttpStatusCode == HttpStatusCode.TooManyRequests)
                {
                    Telemetry.Warn("  google api rate limit reached (429). skipping remaining playlists.");
                    break;
                }

                if (ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    Telemetry.Error("  google api key invalid or forbidden ({Code}).", (int)ex.HttpStatusCode);
                    break;
                }

                throw;
            }
            catch (RequestFailedException ex)
            {
                if (ex.Status == 429)
                {
                    Telemetry.Warn("  azure translation rate limit reached (429). skipping remaining playlists.");
                    break;
                }

                if (ex.Status is 401 or 403)
                {
                    Telemetry.Error("  azure translation key invalid or forbidden ({Code}).", ex.Status);
                    break;
                }

                throw;
            }
        }

        syncStopwatch.Stop();

        var fetchComplete = processedSnapshots.Count == playlistsToProcess.Count;

        var newState = new YouTubeFetchState
        {
            PlaylistSnapshots = updatedSnapshots,
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            FetchComplete = fetchComplete,
            AzureCharsUsed = azureCharsUsed,
            AzureCharsMonth = currentMonth,
        };

        await YouTubeFetchState.SaveAsync(ManifestFile, newState, ct);

        Telemetry.Info(
            "Sync complete in {Elapsed}s: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged | {TotalVideos} videos ({Skipped} skipped) | {PlaylistsProcessed}/{PlaylistsTotal} playlists",
            syncStopwatch.Elapsed.TotalSeconds,
            newPlaylists.Count,
            changedPlaylists.Count,
            deletedPlaylists.Count,
            unchangedPlaylists.Count,
            totalVideos,
            skippedVideos,
            processedSnapshots.Count,
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

        var (videos, skipped, _) = await ProcessPlaylistAsync(match, ct);

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
            "Synced playlist {Title}: {Videos} videos ({Skipped} skipped)",
            match.Title, videos, skipped);
    }

    private async Task<(int Videos, int Skipped, int AzureChars)> ProcessPlaylistAsync(PlaylistSnapshot snapshot, CancellationToken ct)
    {
        var playlistStopwatch = Stopwatch.StartNew();
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
        var existingVideos = await LoadExistingVideosAsync(playlistPath, ct);
        var existingDict = new Dictionary<string, YouTubeVideo>();
        foreach (var v in existingVideos)
            existingDict.TryAdd(v.VideoId, v);
        var incomingIds = videos.Select(v => v.VideoId).ToHashSet();

        if (existingVideos.Count == 0)
        {
            Telemetry.Info("  fresh sync: {Count} videos", videos.Count);
        }
        else
        {
            var added = incomingIds.Except(existingDict.Keys).Count();
            var removed = existingDict.Keys.Except(incomingIds).Count();
            var net = added - removed;
            var netStr = net switch { > 0 => $"+{net}", 0 => "net 0", _ => $"{net}" };
            Telemetry.Info(
                "  update sync: {Added} added, {Removed} removed ({Net}), {Total} total",
                added, removed, netStr, videos.Count);

            for (var i = 0; i < videos.Count; i++)
            {
                if (existingDict.TryGetValue(videos[i].VideoId, out var existing)
                    && existing.TranslatedTitle is not null
                    && existing.DetectedLanguage is not null)
                {
                    videos[i] = videos[i] with
                    {
                        TranslatedTitle = existing.TranslatedTitle,
                        TranslatedDescription = existing.TranslatedDescription,
                        DetectedLanguage = existing.DetectedLanguage,
                    };
                }
            }
        }

        var cachedVideos = videos.Where(v => v.TranslatedTitle is not null).ToList();
        if (cachedVideos.Count > 0)
        {
            var langGroups = cachedVideos
                .GroupBy(v => v.DetectedLanguage ?? "unknown")
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");
            Telemetry.Info("  cache: {Count}/{Total} videos from previous run ({LangSummary})",
                cachedVideos.Count, videos.Count, string.Join(", ", langGroups));
        }

        var (translatedVideos, azureChars) = await translationService.TranslateVideosAsync(videos, ct);

        Telemetry.Debug("Writing processed file: {Path} ({Count} videos)", playlistPath, translatedVideos.Count);

        var playlistJson = JsonSerializer.Serialize(translatedVideos, YouTubeFetchState.JsonOptions);
        await File.WriteAllTextAsync(playlistPath, playlistJson, ct);

        Telemetry.Debug("Wrote {Size} bytes to {Path}", playlistJson.Length, playlistPath);

        playlistStopwatch.Stop();

        Telemetry.Info(
            "  done — {Count} videos, {Skipped} skipped in {Elapsed:F1}s",
            translatedVideos.Count,
            skipped,
            playlistStopwatch.Elapsed.TotalSeconds);

        return (translatedVideos.Count, skipped, azureChars);
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

    private static async Task<List<YouTubeVideo>> LoadExistingVideosAsync(string processedPath, CancellationToken ct)
    {
        if (!File.Exists(processedPath))
            return [];

        try
        {
            await using var stream = File.OpenRead(processedPath);
            return await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
                stream, YouTubeFetchState.JsonOptions, ct) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
