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
        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        var result = await client
            .AnalyzeSentimentAsync(
                text,
                language,
                new AnalyzeSentimentOptions { IncludeOpinionMining = opinionMining },
                ct
            )
            .WithTelemetry("TextAnalytics", "TextAnalytics.Sentiment");

        var sb = new StringBuilder();
        sb.AppendLine($"Sentiment: {result.Value.Sentiment}");
        sb.AppendLine(
            $"Scores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}"
        );

        if (opinionMining && result.Value.Sentences is { } sentences)
            foreach (var sentence in sentences)
                if (sentence.Opinions is { Count: > 0 } opinions)
                    foreach (var opinion in opinions)
                    {
                        var aspect = string.IsNullOrWhiteSpace(opinion.Target.Text)
                            ? "(no aspect)"
                            : opinion.Target.Text;
                        var assessment = opinion.Assessments is { Count: > 0 }
                            ? string.Join(
                                ", ",
                                opinion.Assessments.Select(a => $"{a.Text} ({a.Sentiment})")
                            )
                            : "(no assessment)";
                        sb.AppendLine($"  Aspect: {aspect} -> {assessment}");
                    }

        return sb.ToString();
    }

    public async Task<string> EntitiesAsync(string text, string language, CancellationToken ct)
    {
        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        var result = await client
            .RecognizeEntitiesAsync(text, language, ct)
            .WithTelemetry("TextAnalytics", "TextAnalytics.Entities");

        var sb = new StringBuilder();
        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

        return sb.Length > 0 ? sb.ToString() : "(no entities)";
    }

    public async Task<string> KeyPhrasesAsync(string text, string language, CancellationToken ct)
    {
        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        var result = await client
            .ExtractKeyPhrasesAsync(text, language, ct)
            .WithTelemetry("TextAnalytics", "TextAnalytics.KeyPhrases");

        return string.Join(", ", result.Value);
    }

    public async Task<string> DetectLanguageAsync(
        string text,
        string countryHint,
        CancellationToken ct
    )
    {
        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 5K"
            );

        var result = await client
            .DetectLanguageAsync(text, countryHint, ct)
            .WithTelemetry("TextAnalytics", "TextAnalytics.DetectLanguage");

        return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
    }

    public async Task<string> PiiAsync(
        string text,
        string language,
        string? domain,
        CancellationToken ct
    )
    {
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

        var result = await client
            .RecognizePiiEntitiesAsync(text, language, options, ct)
            .WithTelemetry("TextAnalytics", "TextAnalytics.Pii");

        var sb = new StringBuilder();
        if (domain is { })
            sb.AppendLine($"Domain: {domain}");

        foreach (var e in result.Value)
            sb.AppendLine($"  [{e.Category}] {e.Text}");

        return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
    }
}
