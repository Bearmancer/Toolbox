using Azure.AI.Translation.Text;
using Core;
using ErrorOr;

namespace Services.Azure;

public readonly record struct TranslationResult(string DetectedLanguage, string TranslatedText);

public class TranslateService(TextTranslationClient client)
{
    private const int MaxChars = 50_000;

    public async Task<ErrorOr<List<TranslationResult>>> TranslateBatchAsync(
        List<string> texts,
        string toLang,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService(ServiceName.Translate);

        try
        {
            var oversized = texts.Where(t => t.Length > MaxChars).ToList();
            if (oversized.Count > 0)
                return Errors.Translate.ApiError(
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
        catch (Exception ex)
        {
            return Errors.Translate.ApiError(ex.Message);
        }
    }
}
