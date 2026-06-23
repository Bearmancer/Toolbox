using System.Text;
using Azure.AI.TextAnalytics;
using Core;

namespace Services.Azure;

public class TextAnalyticsService(TextAnalyticsClient client)
{
    private const int MaxChars = 5_120;

    public async Task<string> SentimentAsync(
        string text,
        string language,
        CancellationToken ct,
        bool opinionMining = false
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("TextAnalytics.Sentiment");

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "TextAnalytics", "AnalyzeSentiment", language);
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeSentimentAsync(
            text,
            language,
            new AnalyzeSentimentOptions { IncludeOpinionMining = opinionMining },
            ct
        );
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "TextAnalytics",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        var sb = new StringBuilder();
        sb.AppendLine($"Sentiment: {result.Value.Sentiment}");
        sb.AppendLine($"Scores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}");

        if (opinionMining && result.Value.Sentences is { } sentences)
        {
            foreach (var sentence in sentences)
            {
                if (sentence.Opinions is { Count: > 0 } opinions)
                {
                    foreach (var opinion in opinions)
                    {
                        var aspect = string.IsNullOrWhiteSpace(opinion.Target.Text) ? "(no aspect)" : opinion.Target.Text;
                        var assessment = opinion.Assessments is { Count: > 0 }
                            ? string.Join(", ", opinion.Assessments.Select(a => $"{a.Text} ({a.Sentiment})"))
                            : "(no assessment)";
                        sb.AppendLine($"  Aspect: {aspect} -> {assessment}");
                    }
                }
            }
        }

        activity.Complete();
        return sb.ToString();
    }

    public async Task<string> EntitiesAsync(
        string text,
        string language,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("TextAnalytics.Entities");

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "TextAnalytics", "RecognizeEntities", language);
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizeEntitiesAsync(text, language, ct);
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "TextAnalytics",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

        activity.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no entities)";
    }

    public async Task<string> KeyPhrasesAsync(
        string text,
        string language,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("TextAnalytics.KeyPhrases");

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "TextAnalytics", "ExtractKeyPhrases", language);
        var startTime = DateTime.UtcNow;
        var result = await client.ExtractKeyPhrasesAsync(text, language, ct);
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "TextAnalytics",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        activity.Complete();
        return string.Join(", ", result.Value);
    }

    public async Task<string> DetectLanguageAsync(
        string text,
        string countryHint,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("TextAnalytics.DetectLanguage");

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "TextAnalytics", "DetectLanguage", countryHint);
        var startTime = DateTime.UtcNow;
        var result = await client.DetectLanguageAsync(text, countryHint, ct);
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "TextAnalytics",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        activity.Complete();
        return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
    }

    public async Task<string> PiiAsync(
        string text,
        string language,
        string? domain,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("TextAnalytics.Pii");

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        var options = new RecognizePiiEntitiesOptions();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var normalized = domain.Trim().ToLowerInvariant();
            options.DomainFilter = normalized switch
            {
                "phi" => PiiEntityDomain.ProtectedHealthInformation,
                "none" => PiiEntityDomain.None,
                _ => throw new ArgumentException(
                    $"Unknown PII domain '{domain}'. Valid values: phi, none",
                    nameof(domain)
                ),
            };
        }

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "TextAnalytics", "RecognizePiiEntities", language);
        var startTime = DateTime.UtcNow;
        var result = await client.RecognizePiiEntitiesAsync(text, language, options, ct);
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "TextAnalytics",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        var sb = new StringBuilder();
        if (domain is not null)
            sb.AppendLine($"Domain: {domain}");

        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text}");

        activity.Complete();
        return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
    }
}
