using System.Text;
using Azure.AI.TextAnalytics;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Azure;

public static class TextAnalyticsService
{
    public static async Task<string> SentimentAsync(string text, CancellationToken ct = default)
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("TextAnalytics.Sentiment");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 5K");

        var client = AzureClients.CreateTextAnalyticsClient();

        Log.Emit(new ApiRequested("TextAnalytics", "AnalyzeSentiment", "en"));
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeSentimentAsync(text, "en", ct);
        Log.Emit(new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return $"Sentiment: {result.Value.Sentiment}\nScores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}";
    }

    public static async Task<string> EntitiesAsync(string text, CancellationToken ct = default)
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("TextAnalytics.Entities");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 5K");

        var client = AzureClients.CreateTextAnalyticsClient();

        Log.Emit(new ApiRequested("TextAnalytics", "RecognizeEntities", "en"));
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizeEntitiesAsync(text, "en", ct);
        Log.Emit(new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

        op.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no entities)";
    }

    public static async Task<string> KeyPhrasesAsync(string text, CancellationToken ct = default)
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("TextAnalytics.KeyPhrases");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 5K");

        var client = AzureClients.CreateTextAnalyticsClient();

        Log.Emit(new ApiRequested("TextAnalytics", "ExtractKeyPhrases", "en"));
        var startTime = DateTime.UtcNow;
        var result = await client.ExtractKeyPhrasesAsync(text, "en", ct);
        Log.Emit(new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return string.Join(", ", result.Value);
    }

    public static async Task<string> DetectLanguageAsync(string text, CancellationToken ct = default)
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("TextAnalytics.DetectLanguage");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 5K");

        var client = AzureClients.CreateTextAnalyticsClient();

        Log.Emit(new ApiRequested("TextAnalytics", "DetectLanguage", ""));
        var startTime = DateTime.UtcNow;
        var result = await client.DetectLanguageAsync(text, "", ct);
        Log.Emit(new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
    }

    public static async Task<string> PiiAsync(string text, CancellationToken ct = default)
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("TextAnalytics.Pii");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 5K");

        var client = AzureClients.CreateTextAnalyticsClient();

        Log.Emit(new ApiRequested("TextAnalytics", "RecognizePiiEntities", "en"));
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizePiiEntitiesAsync(text, "en", new RecognizePiiEntitiesOptions(), ct);
        Log.Emit(new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text}");

        op.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
    }
}