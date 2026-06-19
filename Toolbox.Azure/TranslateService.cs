using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Azure;

public static class TranslateService
{
    public static async Task<string> TranslateAsync(
        string text,
        string toLang,
        string fromLang = "en",
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("Translate.Translate");

        if (text.Length > Constants.TranslatorMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 50K");

        var client = AzureClients.CreateTranslationClient();

        Log.Emit(new ApiRequested("Translate", "Translate", $"{fromLang}->{toLang}"));
        var startTime = DateTime.UtcNow;
        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        Log.Emit(new ApiResponded("Translate", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}