using Core;
using ErrorOr;
using Microsoft.CognitiveServices.Speech;

namespace Services.Azure;

public class SpeechTtsService(AzureCredentials opts)
{
    private const int MaxTextLength = 512_000;

    public async Task<ErrorOr<string>> SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        CancellationToken ct
    )
    {
        if (text.Length > MaxTextLength)
            return Errors.Validation.InvalidInput(nameof(text), $"Text length {text.Length} exceeds 512K");

        var config = BuildSpeechConfig();
        config.SpeechSynthesisVoiceName = voice;
        using var synth = new SpeechSynthesizer(speechConfig: config);

        await using var reg = ct.Register(() => _ = synth.StopSpeakingAsync());

        try
        {
            var result = await synth.SpeakTextAsync(text: text);

            await File.WriteAllBytesAsync(
                outputPath,
                result.AudioData,
                ct
            );

            return $"Voice: {voice}\nSaved: {outputPath}\nSize: {result.AudioData.Length:N0} bytes";
        }
        catch (Exception ex)
        {
            return Errors.Speech.ApiError(ex.Message);
        }
    }

    private SpeechConfig BuildSpeechConfig()
    {
        var endpoint = new Uri(uriString: opts.SpeechEndpoint);
        return SpeechConfig.FromEndpoint(endpoint, opts.SpeechKey);
    }
}
