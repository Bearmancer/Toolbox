using Microsoft.Extensions.DependencyInjection;
using Services.Google;

namespace App.Tests;

public static class GoogleTests
{
    public static async Task RunAsync(IServiceProvider provider, CancellationToken ct)
    {
        Console.WriteLine("=== Google Services Integration Test ===\n");

        Console.WriteLine("1. Testing YouTube (list playlists)...");
        try
        {
            var yt = provider.GetRequiredService<YoutubeService>();
            var playlists = await yt.GetPlaylistsAsync(ct);
            Console.WriteLine($"Result: {playlists.Count} playlist(s): {string.Join(", ", playlists.Select(p => p.Snippet.Title))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n2. Testing YouTube Sync (Alan Gilbert only)...");
        try
        {
            var orchestrator = provider.GetRequiredService<YouTubePlaylistOrchestrator>();
            await orchestrator.ExecuteForPlaylistTitleAsync("Alan Gilbert", ct);
            Console.WriteLine("Result: Sync complete — check state/youtube/playlists/Alan Gilbert.json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n=== Google test run complete! ===");
    }
}
