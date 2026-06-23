using Azure.AI.Translation.Text;
using Core;

namespace Services.Azure;

public class TranslateService(TextTranslationClient client)
{
    private const int MaxChars = 50_000;

    public async Task<string> TranslateAsync(
        string text,
        string toLang,
        string fromLang,
        CancellationToken ct
    )
    {
        using var _ = Telemetry.ForService("Azure");
        using var activity = Telemetry.StartActivity("Translate {FromLang} -> {ToLang}", fromLang, toLang);

        if (text.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 50K"
            );

        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        activity.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}
