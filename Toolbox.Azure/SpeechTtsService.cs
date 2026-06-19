using Microsoft.CognitiveServices.Speech;
using Toolbox.Core;
using Toolbox.Core.Logging;

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
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("Speech.Synthesize");

        if (text.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 512K");

        var config = AzureClients.CreateSpeechConfig();
        config.SpeechSynthesisVoiceName = voice;
        using var synth = new SpeechSynthesizer(config);

        Log.Emit(new ApiRequested("Speech", "SpeakText", voice));
        var startTime = DateTime.UtcNow;
        var result = await synth.SpeakTextAsync(text);
        Log.Emit(new ApiResponded("Speech", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        await File.WriteAllBytesAsync(outputPath, result.AudioData, ct);

        op.Complete();
        return $"Voice: {voice}\nSaved: {outputPath}\nSize: {result.AudioData.Length:N0} bytes";
    }
}