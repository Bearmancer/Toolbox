using System.Diagnostics;
using System.Net;
using Azure;
using Core;
using GoogleApiException = Google.GoogleApiException;

namespace Services.Google.YouTube;

public class YouTubePlaylistOrchestrator(
    YouTubePlaylistService playlistService,
    YouTubePlaylistProcessor playlistProcessor
)
{
    private static readonly string ManifestFile = Path.Combine(YouTubePaths.StateRoot, "manifest.json");

    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");
        var syncStopwatch = Stopwatch.StartNew();

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
        var current = await playlistService.GetPlaylistSummariesAsync(ct);

        Telemetry.Debug("Fetched {Count} playlist summaries from API", current.Count);

        var (newPlaylists, changedPlaylists, deletedPlaylists, unchangedPlaylists) =
            YouTubeChangeDetector.DetectChanges(current, stored);

        foreach (var snapshot in deletedPlaylists)
            ArchivePlaylist(snapshot);

        List<PlaylistSnapshot> playlistsToProcess = [.. newPlaylists, .. changedPlaylists];

        if (playlistsToProcess.Count == 0)
        {
            Telemetry.Info(
                "No playlists need updating — everything is current ({Unchanged} playlists unchanged, {Deleted} deleted)",
                unchangedPlaylists.Count,
                deletedPlaylists.Count
            );
            return [];
        }

        var totalVideos = 0;
        var skippedVideos = 0;

        Dictionary<string, PlaylistSnapshot> updatedSnapshots = new(stored.PlaylistSnapshots);
        foreach (var snapshot in deletedPlaylists)
            updatedSnapshots.Remove(snapshot.PlaylistId);

        var currentMonth = DateTimeOffset.UtcNow.Month;
        var azureCharsUsed = stored.AzureCharsMonth == currentMonth ? stored.AzureCharsUsed : 0;
        Telemetry.Info(
            "Azure Translator: {Used} chars used this month (2,000,000 free tier)",
            azureCharsUsed
        );

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
                snapshot.Title
            );

            try
            {
                var (videos, skipped, azureChars) = await playlistProcessor.ProcessPlaylistAsync(
                    snapshot,
                    ct
                );
                totalVideos += videos;
                skippedVideos += skipped;
                azureCharsUsed += azureChars;
                processedSnapshots.Add(snapshot);

                updatedSnapshots[snapshot.PlaylistId] = snapshot;
                await YouTubeFetchState.SaveAsync(
                    ManifestFile,
                    new YouTubeFetchState
                    {
                        PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(
                            updatedSnapshots
                        ),
                        LastChecked = DateTimeOffset.UtcNow,
                        LastUpdated = DateTimeOffset.UtcNow,
                        FetchComplete = false,
                        AzureCharsUsed = azureCharsUsed,
                        AzureCharsMonth = currentMonth,
                    },
                    ct
                );
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.TooManyRequests)
            {
                Telemetry.Warn(
                    "Google API rate limit reached (429). Skipping remaining playlists."
                );
                break;
            }
            catch (GoogleApiException ex)
                when (ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Telemetry.Error(
                    "Google API key invalid or forbidden ({Code}).",
                    (int)ex.HttpStatusCode
                );
                break;
            }
            catch (RequestFailedException ex) when (ex.Status == 429)
            {
                Telemetry.Warn(
                    "Azure translation rate limit reached (429). Skipping remaining playlists."
                );
                break;
            }
            catch (RequestFailedException ex) when (ex.Status is 401 or 403)
            {
                Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", ex.Status);
                break;
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
            current.Count
        );

        return [.. processedSnapshots.Select(s => s.PlaylistId)];
    }

    public async Task<string?> ExecuteForPlaylistTitleAsync(string title, CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Google");

        EnsureDirectories();

        var stored = await YouTubeFetchState.LoadAsync(ManifestFile, ct);

        var match = stored.PlaylistSnapshots.Values.FirstOrDefault(s =>
            s.Title.IsEqualToIgnore(title)
        );

        if (match is null)
        {
            var summaries = await playlistService.GetPlaylistSummariesAsync(ct);
            match = summaries.FirstOrDefault(s => s.Title.IsEqualToIgnore(title));
        }
        else
            Telemetry.Debug("Cached ID for {Title} (skipped Playlists.list)", match.Title);

        if (match is null)
        {
            Telemetry.Error("Playlist not found: {Title}", title);
            return null;
        }

        var (videos, skipped, _) = await playlistProcessor.ProcessPlaylistAsync(match, ct);

        stored = stored with
        {
            PlaylistSnapshots = new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots)
            {
                [match.PlaylistId] = match,
            },
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            FetchComplete = true,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, stored, ct);

        Telemetry.Info(
            "Synced playlist {Title}: {Videos} videos ({Skipped} skipped)",
            match.Title,
            videos,
            skipped
        );

        return match.PlaylistId;
    }

    private static void ArchivePlaylist(PlaylistSnapshot snapshot)
    {
        var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
        var sourcePath = Path.Combine(YouTubePaths.ProcessedDir, $"{sanitizedTitle}.json");
        var destPath = Path.Combine(YouTubePaths.DeletedDir, $"{sanitizedTitle}.json");

        if (!File.Exists(sourcePath))
            return;

        File.Move(sourcePath, destPath, true);
        Telemetry.Info("Archived deleted playlist: {Title}", snapshot.Title);
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(YouTubePaths.RawDir);
        Directory.CreateDirectory(YouTubePaths.ProcessedDir);
        Directory.CreateDirectory(YouTubePaths.DeletedDir);
    }
}
