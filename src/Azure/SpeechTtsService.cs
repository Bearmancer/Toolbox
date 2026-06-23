using App.Services.Azure.Options;
using Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Options;

namespace App.Services.Azure;

public class SpeechTtsService(IOptions<AzureOptions> opts)
{
    public async Task<string> SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        CancellationToken ct = default
    )
    {
        using var activity = Telemetry.StartActivity("Speech.Synthesize");

        if (text.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text length {text.Length} exceeds 512K"
            );

        var config = BuildSpeechConfig();
        config.SpeechSynthesisVoiceName = voice;
        using var synth = new SpeechSynthesizer(config);

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "Speech", "SpeakText", voice);
        var startTime = DateTime.UtcNow;
        var result = await synth.SpeakTextAsync(text);
        Telemetry.Debug("API response: {Service} {StatusCode} {ElapsedMs:F0}ms", "Speech", 200, (DateTime.UtcNow - startTime).TotalMilliseconds);

        await File.WriteAllBytesAsync(outputPath, result.AudioData, ct);

        activity.Complete();
        return $"Voice: {voice}\nSaved: {outputPath}\nSize: {result.AudioData.Length:N0} bytes";
    }

    private SpeechConfig BuildSpeechConfig()
    {
        var endpoint = new Uri(opts.Value.SpeechEndpoint!);
        return SpeechConfig.FromEndpoint(endpoint, opts.Value.SpeechKey!);
    }
}
