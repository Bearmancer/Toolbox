using Azure.AI.Translation.Text;
using Core.Logging;

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
        using var op = Log.BeginOperation("Translate.Translate");

        if (text.Length > Constants.TranslatorMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 50K"
            );

        Log.Emit(new ApiRequested("Translate", "Translate", $"{fromLang}->{toLang}"));
        var startTime = DateTime.UtcNow;
        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        Log.Emit(
            new ApiResponded("Translate", 200, (DateTime.UtcNow - startTime).TotalMilliseconds)
        );

        op.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}
