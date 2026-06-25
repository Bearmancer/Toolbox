using Azure.AI.Translation.Text;
using Core;

namespace Services.Azure;

public sealed record TranslationResult(string DetectedLanguage, string TranslatedText);

public class TranslateService(TextTranslationClient client)
{
    private const int MaxChars = 50_000;

    public async Task<IReadOnlyList<TranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string toLang,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Translate");

        var oversized = texts.Where(t => t.Length > MaxChars).ToList();
        if (oversized.Count > 0)
            throw new ArgumentOutOfRangeException(
                nameof(texts),
                $"Batch contains {oversized.Count} texts exceeding {MaxChars} chars"
            );

        var response = await client.TranslateAsync(toLang, texts, cancellationToken: ct);

        var results = new List<TranslationResult>(texts.Count);
        foreach (var item in response.Value)
        {
            var detected = item.DetectedLanguage?.Language ?? "unknown";
            var translated = item.Translations[0].Text;

            results.Add(new TranslationResult(detected, translated));
        }

        return results;
    }
}
