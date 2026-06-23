using Azure.AI.Translation.Text;
using Core;

namespace App.Services.Azure;

public class TranslateService(TextTranslationClient client)
{
    public async Task<string> TranslateAsync(
        string text,
        string toLang,
        string fromLang = "en",
        CancellationToken ct = default
    )
    {
        using var activity = Telemetry.StartActivity("Translate {FromLang} -> {ToLang}", fromLang, toLang);

        if (text.Length > Constants.TranslatorMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 50K"
            );

        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        activity.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}
