using Microsoft.Extensions.DependencyInjection;
using Services.Azure;
using Services.Google;
using Spectre.Console;

namespace App;

public static class ManualIntegrationTest
{
    public static async Task RunAsync(IServiceProvider provider, CancellationToken ct)
    {
        var resources = Path.Combine(Directory.GetCurrentDirectory(), "resources");

        AnsiConsole.MarkupLine("[bold yellow]Starting Azure Services Integration Test[/]");

        await RunTest("1. Testing OpenAI Chat...", async () =>
        {
            var openai = provider.GetRequiredService<OpenAiService>();
            return await openai.ChatAsync("What is the capital of France? Reply in one word.", ct);
        });

        await RunTest("2. Testing Translator...", async () =>
        {
            var translator = provider.GetRequiredService<TranslateService>();
            return await translator.TranslateAsync("Hello, how are you?", "es", "en", ct);
        });

        await RunTest("3. Testing Text Analytics (Sentiment)...", async () =>
        {
            var textAnalytics = provider.GetRequiredService<TextAnalyticsService>();
            return await textAnalytics.SentimentAsync("I absolutely love this new feature!", "en", ct);
        });

        await RunTest("4. Testing Vision (Tags)...", async () =>
        {
            var vision = provider.GetRequiredService<VisionService>();
            var visionPath = Path.Combine(resources, "Box 02 Booklet 04.jpg");
            return await vision.AnalyzeAsync(visionPath, "tags", "en", ct);
        });

        await RunTest("5. Testing Doc Intelligence...", async () =>
        {
            var docIntel = provider.GetRequiredService<DocIntelService>();
            var docPath = Path.Combine(resources, "example.pdf");
            return await docIntel.AnalyzeAsync(docPath, "prebuilt-read", ct);
        });

        await RunTest("6. Testing Speech TTS...", async () =>
        {
            var tts = provider.GetRequiredService<SpeechTtsService>();
            var ttsOut = Path.Combine(resources, "test_output.wav");
            return await tts.SynthesizeAsync("This is a test of the speech synthesis capabilities in Azure.", "en-US-JennyNeural", ttsOut, ct);
        });

        await RunTest("7. Testing Speech STT...", async () =>
        {
            var stt = provider.GetRequiredService<SpeechSttService>();
            var sttInput = Path.Combine(resources, "test_output.wav");
            return await stt.TranscribeAsync(sttInput, "en-US", ct);
        });

        await RunTest("8. Testing YouTube (list playlists)...", async () =>
        {
            var yt = provider.GetRequiredService<YoutubeService>();
            var playlists = await yt.GetPlaylistsAsync(ct);
            return $"{playlists.Count} playlist(s): {string.Join(", ", playlists.Select(p => p.Snippet.Title))}";
        });

        AnsiConsole.MarkupLine("\n[bold green]Test run complete![/]");
    }

    private static async Task RunTest(string title, Func<Task<string>> testFunc)
    {
        AnsiConsole.MarkupLine($"\n[bold blue]{title}[/]");
        try
        {
            var result = await testFunc();
            AnsiConsole.MarkupLine($"[green]Result:[/] {result}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed:[/] {ex.Message}");
        }
    }
}
