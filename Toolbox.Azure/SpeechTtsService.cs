using Microsoft.CognitiveServices.Speech;
using Toolbox.Core;

namespace Toolbox.Azure;

public static class SpeechTtsService
{
    public static async Task<string> SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        CancellationToken ct = default
    )
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("Speech.Synthesize");

        if (text.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 512K"
            );
        var config = AzureClients.CreateSpeechConfig();
        config.SpeechSynthesisVoiceName = voice;
        using var synth = new SpeechSynthesizer(config);

        Logger.ApiRequest("Speech", "SpeakText", voice);
        var startTime = DateTime.UtcNow;
        var result = await synth.SpeakTextAsync(text);
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("Speech", 200, elapsed);

        await File.WriteAllBytesAsync(outputPath, result.AudioData, ct);

        Logger.Complete("Speech.Synthesize");
        return $"Voice: {voice}\nSaved: {outputPath}\nSize: {result.AudioData.Length:N0} bytes";
    }
}