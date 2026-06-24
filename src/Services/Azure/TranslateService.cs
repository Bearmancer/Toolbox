using Azure.Core.Diagnostics;
using Azure.AI.Translation.Text;
using Core;
using System.Diagnostics.Tracing;

namespace Services.Azure;

public sealed record TranslationResult(string DetectedLanguage, string TranslatedText);

public class TranslateService(TextTranslationClient client)
{
    private const int MaxChars = 50_000;
    private static readonly AzureEventSourceListener AzureDiagnostics = new(
        (e, message) =>
        {
            if (e.Level <= EventLevel.Warning)
                Telemetry.Debug("[Azure SDK] {Source}: {Message}", e.EventSource.Name, message);
        },
        EventLevel.Verbose);

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string toLang,
        string fromLang,
        CancellationToken ct)
    {
        var results = await TranslateBatchAsync([text], toLang, ct);
        return results[0];
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string toLang,
        CancellationToken ct)
    {
        using var _ = Telemetry.ForService("Translate");

        var oversized = texts.Where(t => t.Length > MaxChars).ToList();
        if (oversized.Count > 0)
            throw new ArgumentOutOfRangeException(nameof(texts), $"Batch contains {oversized.Count} texts exceeding {MaxChars} chars");

        var totalChars = texts.Sum(t => t.Length);

        var response = await client.TranslateAsync(toLang, texts, cancellationToken: ct);

        var results = new List<TranslationResult>(texts.Count);
        for (var i = 0; i < response.Value.Count; i++)
        {
            var item = response.Value[i];
            var detected = item.DetectedLanguage?.Language ?? "unknown";
            var translated = item.Translations[0].Text;

            results.Add(new TranslationResult(
                DetectedLanguage: detected,
                TranslatedText: translated));
        }

        return results;
    }
}
