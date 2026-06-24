using Microsoft.Extensions.DependencyInjection;
using Services.Azure;

namespace App.Tests;

public static class AzureTests
{
    public static async Task RunAsync(IServiceProvider provider, CancellationToken ct)
    {
        Console.WriteLine("=== Azure Services Integration Test ===\n");

        var resources = Path.Combine(Directory.GetCurrentDirectory(), "resources");

        Console.WriteLine("1. Testing OpenAI Chat...");
        try
        {
            var openai = provider.GetRequiredService<OpenAiService>();
            var result = await openai.ChatAsync("What is the capital of France? Reply in one word.", ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n2. Testing Translator...");
        try
        {
            var translator = provider.GetRequiredService<TranslateService>();
            var translation = await translator.TranslateAsync("Hello, how are you?", "es", "en", ct);
            Console.WriteLine($"Result: {translation.TranslatedText}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n3. Testing Text Analytics (Sentiment)...");
        try
        {
            var textAnalytics = provider.GetRequiredService<TextAnalyticsService>();
            var result = await textAnalytics.SentimentAsync("I absolutely love this new feature!", "en", ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n4. Testing Vision (Tags)...");
        try
        {
            var vision = provider.GetRequiredService<VisionService>();
            var visionPath = Path.Combine(resources, "Box 02 Booklet 04.jpg");
            var result = await vision.AnalyzeAsync(visionPath, "tags", "en", ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n5. Testing Doc Intelligence...");
        try
        {
            var docIntel = provider.GetRequiredService<DocIntelService>();
            var docPath = Path.Combine(resources, "example.pdf");
            var result = await docIntel.AnalyzeAsync(docPath, "prebuilt-read", ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n6. Testing Speech TTS...");
        try
        {
            var tts = provider.GetRequiredService<SpeechTtsService>();
            var ttsOut = Path.Combine(resources, "test_output.wav");
            var result = await tts.SynthesizeAsync("This is a test of the speech synthesis capabilities in Azure.", "en-US-JennyNeural", ttsOut, ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n7. Testing Speech STT...");
        try
        {
            var stt = provider.GetRequiredService<SpeechSttService>();
            var sttInput = Path.Combine(resources, "test_output.wav");
            var result = await stt.TranscribeAsync(sttInput, "en-US", ct);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }

        Console.WriteLine("\n=== Azure test run complete! ===");
    }
}
