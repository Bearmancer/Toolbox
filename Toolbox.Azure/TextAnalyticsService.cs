using System.Text;
using Azure.AI.TextAnalytics;
using Toolbox.Core;

namespace Toolbox.Azure;

public static class TextAnalyticsService
{
    public static async Task<string> SentimentAsync(string text, CancellationToken ct = default)
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("TextAnalytics.Sentiment");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );
        var client = AzureClients.CreateTextAnalyticsClient();

        Logger.ApiRequest("TextAnalytics", "AnalyzeSentiment", "en");
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeSentimentAsync(text, "en", ct);
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("TextAnalytics", 200, elapsed);

        Logger.Complete("TextAnalytics.Sentiment");
        return
            $"Sentiment: {result.Value.Sentiment}\nScores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}";
    }

    public static async Task<string> EntitiesAsync(string text, CancellationToken ct = default)
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("TextAnalytics.Entities");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );
        var client = AzureClients.CreateTextAnalyticsClient();

        Logger.ApiRequest("TextAnalytics", "RecognizeEntities", "en");
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizeEntitiesAsync(
            text,
            "en",
            ct
        );
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("TextAnalytics", 200, elapsed);

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

        Logger.Complete("TextAnalytics.Entities");
        return sb.Length > 0 ? sb.ToString() : "(no entities)";
    }

    public static async Task<string> KeyPhrasesAsync(string text, CancellationToken ct = default)
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("TextAnalytics.KeyPhrases");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );
        var client = AzureClients.CreateTextAnalyticsClient();

        Logger.ApiRequest("TextAnalytics", "ExtractKeyPhrases", "en");
        var startTime = DateTime.UtcNow;
        var result = await client.ExtractKeyPhrasesAsync(text, "en", ct);
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("TextAnalytics", 200, elapsed);

        Logger.Complete("TextAnalytics.KeyPhrases");
        return string.Join(", ", result.Value);
    }

    public static async Task<string> DetectLanguageAsync(
        string text,
        CancellationToken ct = default
    )
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("TextAnalytics.DetectLanguage");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );
        var client = AzureClients.CreateTextAnalyticsClient();

        Logger.ApiRequest("TextAnalytics", "DetectLanguage", "");
        var startTime = DateTime.UtcNow;
        var result = await client.DetectLanguageAsync(text, "", ct);
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("TextAnalytics", 200, elapsed);

        Logger.Complete("TextAnalytics.DetectLanguage");
        return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
    }

    public static async Task<string> PiiAsync(string text, CancellationToken ct = default)
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("TextAnalytics.Pii");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );
        var client = AzureClients.CreateTextAnalyticsClient();

        Logger.ApiRequest("TextAnalytics", "RecognizePiiEntities", "en");
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizePiiEntitiesAsync(
            text,
            "en",
            new RecognizePiiEntitiesOptions(),
            ct
        );
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("TextAnalytics", 200, elapsed);

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text}");

        Logger.Complete("TextAnalytics.Pii");
        return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
    }
}