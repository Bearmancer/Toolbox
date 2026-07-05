using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class YouTubeSyncProcessor(
    YouTubePlaylistProcessor playlistProcessor,
    YouTubeSortService sortService
)
{
    private static readonly string StateRoot = Path.Combine(
        PathResolver.RepoRoot,
        "state",
        "youtube"
    );
    private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
    private static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");

    private static readonly string ManifestFile = Path.Combine(
        StateRoot,
        "manifest.json"
    );

    public async Task<SyncResult> ProcessPlaylistsAsync(
        List<PlaylistSnapshot> playlistsToProcess,
        YouTubeFetchState stored,
        CancellationToken ct
    )
    {
        var counters = SyncCounters.FromStoredState(stored);

        Telemetry.Info(
            "Azure Translator: {Used} chars used this month (2,000,000 free tier)",
            counters.AzureCharsUsed
        );

        var processedSnapshots = new List<PlaylistSnapshot>();

        for (var i = 0; i < playlistsToProcess.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = playlistsToProcess[i];

            Telemetry.Info(
                "[{Index}/{Total}] {Title}",
                i + 1,
                playlistsToProcess.Count,
                snapshot.Title
            );

            var result = await ProcessSinglePlaylistAsync(snapshot, counters, ct);
            if (result.ShouldBreak)
                break;

            processedSnapshots.Add(snapshot);
            counters.UpdateFrom(result);
        }

        return counters.ToResult(processedSnapshots);
    }

    private async Task<ProcessResult> ProcessSinglePlaylistAsync(
        PlaylistSnapshot snapshot,
        SyncCounters counters,
        CancellationToken ct
    )
    {
        var processorResult = await playlistProcessor.ProcessPlaylistAsync(snapshot, ct);

        if (processorResult.IsError)
        {
            var error = processorResult.Errors[0];

            if (error.Code is "YT.RateLimit" or "Azure.RateLimit")
            {
                Telemetry.Warn("Rate limit reached ({Code}). Skipping remaining playlists.", error.Code);
                return ProcessResult.Break;
            }

            if (error.Code is "Azure.AuthFailed")
            {
                Telemetry.Error("Azure translation key invalid or forbidden ({Code}).", error.Code);
                return ProcessResult.Break;
            }

            Telemetry.Error("Unexpected error processing playlist {Title}: {Error}",
                snapshot.Title, error.Description);
            return ProcessResult.Break;
        }

        var result = processorResult.Value;
        await SaveIncrementalStateAsync(
            counters.UpdatedSnapshots,
            snapshot,
            counters.AzureCharsUsed + result.AzureChars,
            counters.CurrentMonth,
            ct
        );

        return new ProcessResult(result.Videos, result.Skipped, result.AzureChars, false);
    }

    private async Task SaveIncrementalStateAsync(
        IReadOnlyDictionary<string, PlaylistSnapshot> updatedSnapshots,
        PlaylistSnapshot snapshot,
        int azureCharsUsed,
        int currentMonth,
        CancellationToken ct
    )
    {
        var snapshots = new Dictionary<string, PlaylistSnapshot>(updatedSnapshots)
        {
            [snapshot.PlaylistId] = snapshot,
        };

        var state = new YouTubeFetchState
        {
            PlaylistSnapshots = snapshots,
            LastChecked = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            AzureCharsUsed = azureCharsUsed,
            AzureCharsMonth = currentMonth,
        };
        await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
    }

    public void ArchiveDeletedPlaylists(IReadOnlyList<PlaylistSnapshot> deletedPlaylists)
    {
        foreach (var snapshot in deletedPlaylists)
            ArchivePlaylist(snapshot);
    }

    private static void ArchivePlaylist(PlaylistSnapshot snapshot)
    {
        var sanitizedTitle = Text.SanitizeFileName(snapshot.Title);
        var sourcePath = Path.Combine(ProcessedDir, $"{sanitizedTitle}.json");
        var destPath = Path.Combine(DeletedDir, $"{sanitizedTitle}.json");

        if (!File.Exists(sourcePath))
            return;

        File.Move(sourcePath, destPath, true);
        Telemetry.Info("Archived deleted playlist: {Title}", snapshot.Title);
    }

    public async Task SortPlaylistsAsync(
        IReadOnlyList<string> playlistIds,
        YouTubeFetchState state,
        CancellationToken ct
    )
    {
        Telemetry.Info("Sorting {Count} playlist(s) after sync", playlistIds.Count);

        var anySorted = false;

        foreach (var playlistId in playlistIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!state.PlaylistSnapshots.TryGetValue(playlistId, out var snapshot))
                continue;

            var sorted = await SortSinglePlaylistAsync(playlistId, snapshot, state, ct);
            if (sorted)
                anySorted = true;
            else
                break;
        }

        if (anySorted)
            await YouTubeFetchState.SaveAsync(ManifestFile, state, ct);
    }

    private async Task<bool> SortSinglePlaylistAsync(
        string playlistId,
        PlaylistSnapshot snapshot,
        YouTubeFetchState stored,
        CancellationToken ct
    )
    {
        var sortResult = await sortService.SortPlaylistAsync(playlistId, ct);

        if (sortResult.IsError)
        {
            Telemetry.Error("Sorting failed for {Title}: {Error}", snapshot.Title, sortResult.FirstError.Description);
            return false;
        }

        var result = sortResult.Value;

        if (result.Repositioned > 0)
            await playlistProcessor.RefreshLocalStateAsync(snapshot, ct);

        if (!string.IsNullOrEmpty(result.NewETag))
            stored.PlaylistSnapshots[playlistId] = snapshot with { ETag = result.NewETag };

        Telemetry.Info("{Title}: {Repositioned} items repositioned", snapshot.Title, result.Repositioned);
        return true;
    }

    public readonly record struct SyncResult(
        IReadOnlyList<string> ProcessedIds,
        Dictionary<string, PlaylistSnapshot> UpdatedSnapshots,
        int TotalVideos,
        int SkippedVideos,
        int AzureCharsUsed,
        int CurrentMonth
    );

    public readonly record struct ProcessResult(
        int Videos,
        int Skipped,
        int AzureChars,
        bool ShouldBreak
    )
    {
        public static ProcessResult Break { get; } = new(0, 0, 0, true);
    }

    public sealed class SyncCounters
    {
        public Dictionary<string, PlaylistSnapshot> UpdatedSnapshots { get; }
        public int CurrentMonth { get; }
        public int AzureCharsUsed { get; set; }
        public int TotalVideos { get; set; }
        public int SkippedVideos { get; set; }

        private SyncCounters(Dictionary<string, PlaylistSnapshot> updatedSnapshots, int currentMonth, int azureCharsUsed)
        {
            UpdatedSnapshots = updatedSnapshots;
            CurrentMonth = currentMonth;
            AzureCharsUsed = azureCharsUsed;
        }

        public static SyncCounters FromStoredState(YouTubeFetchState stored)
        {
            var currentMonth = DateTimeOffset.UtcNow.Month;
            return new SyncCounters(
                new Dictionary<string, PlaylistSnapshot>(stored.PlaylistSnapshots),
                currentMonth,
                stored.AzureCharsMonth == currentMonth ? stored.AzureCharsUsed : 0
            );
        }

        public void UpdateFrom(ProcessResult result)
        {
            TotalVideos += result.Videos;
            SkippedVideos += result.Skipped;
            AzureCharsUsed += result.AzureChars;
        }

        public SyncResult ToResult(List<PlaylistSnapshot> processedSnapshots)
        {
            var updated = new Dictionary<string, PlaylistSnapshot>(processedSnapshots.Count);
            foreach (var s in processedSnapshots)
                updated[s.PlaylistId] = s;

            return new SyncResult(
                [.. processedSnapshots.Select(s => s.PlaylistId)],
                updated,
                TotalVideos,
                SkippedVideos,
                AzureCharsUsed,
                CurrentMonth
            );
        }
    }
}
