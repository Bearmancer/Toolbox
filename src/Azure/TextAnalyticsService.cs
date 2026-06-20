using System.Text;
using Azure.AI.TextAnalytics;
using Core.Logging;

namespace App.Services.Azure;

public class TextAnalyticsService(TextAnalyticsClient client)
{
    public async Task<string> SentimentAsync(
        string text,
        string language = "en",
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("TextAnalytics.Sentiment");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Log.Emit(new ApiRequested("TextAnalytics", "AnalyzeSentiment", language));
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeSentimentAsync(text, language, ct);
        Log.Emit(
            new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        op.Complete();
        return $"Sentiment: {result.Value.Sentiment}\nScores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}";
    }

    public async Task<string> EntitiesAsync(
        string text,
        string language = "en",
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("TextAnalytics.Entities");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Log.Emit(new ApiRequested("TextAnalytics", "RecognizeEntities", language));
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizeEntitiesAsync(text, language, ct);
        Log.Emit(
            new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

        op.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no entities)";
    }

    public async Task<string> KeyPhrasesAsync(
        string text,
        string language = "en",
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("TextAnalytics.KeyPhrases");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Log.Emit(new ApiRequested("TextAnalytics", "ExtractKeyPhrases", language));
        var startTime = DateTime.UtcNow;
        var result = await client.ExtractKeyPhrasesAsync(text, language, ct);
        Log.Emit(
            new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        op.Complete();
        return string.Join(", ", result.Value);
    }

    public async Task<string> DetectLanguageAsync(
        string text,
        string countryHint = "us",
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("TextAnalytics.DetectLanguage");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Log.Emit(new ApiRequested("TextAnalytics", "DetectLanguage", countryHint));
        var startTime = DateTime.UtcNow;
        var result = await client.DetectLanguageAsync(text, countryHint, ct);
        Log.Emit(
            new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        op.Complete();
        return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
    }

    public async Task<string> PiiAsync(
        string text,
        string language = "en",
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("TextAnalytics.Pii");

        if (text.Length > Constants.TextAnalyticsMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Log.Emit(new ApiRequested("TextAnalytics", "RecognizePiiEntities", language));
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizePiiEntitiesAsync(
            text,
            language,
            new RecognizePiiEntitiesOptions(),
            ct
        );
        Log.Emit(
            new ApiResponded("TextAnalytics", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text}");

        op.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
    }
}
