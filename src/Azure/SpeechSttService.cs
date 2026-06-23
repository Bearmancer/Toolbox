using System.Diagnostics;
using App.Services.Azure.Options;
using Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;

namespace App.Services.Azure;

public class SpeechSttService(IOptions<AzureOptions> opts)
{
    public async Task<string> TranscribeAsync(
        string filePath,
        string language = "en-US",
        CancellationToken ct = default
    )
    {
        using var activity = Telemetry.StartActivity("Speech.Transcribe");

        var path = FileHelpers.ResolvePath(filePath);
        FileHelpers.ReadChecked(path, Constants.SpeechMaxBytes, "Speech");

        var wavPath = path;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".wav")
        {
            wavPath = Path.Combine(Path.GetTempPath(), $"azureai-{Guid.NewGuid():N}.wav");
            await ConvertToWavAsync(path, wavPath, ct);
        }

        var config = BuildSpeechConfig();
        config.SpeechRecognitionLanguage = language;

        using var recognizer = new SpeechRecognizer(config, AudioConfig.FromWavFileInput(wavPath));
        var segments = new List<string>();
        var stopped = new TaskCompletionSource<bool>();

        recognizer.Recognized += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
                segments.Add(e.Result.Text);
        };
        recognizer.SessionStopped += (_, _) => stopped.TrySetResult(true);
        recognizer.Canceled += (_, _) => stopped.TrySetResult(true);

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "Speech", "StartContinuousRecognition", language);
        var startTime = DateTime.UtcNow;
        await recognizer.StartContinuousRecognitionAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Constants.SpeechMaxDurationSeconds));
        await using var registration = cts.Token.Register(() => stopped.TrySetResult(true));
        await stopped.Task;

        await recognizer.StopContinuousRecognitionAsync();
        Telemetry.Debug("API response: {Service} {StatusCode} {ElapsedMs:F0}ms", "Speech", 200, (DateTime.UtcNow - startTime).TotalMilliseconds);

        if (ext is not ".wav")
            throw new InvalidOperationException("Extension is not WAV!");

        activity.Complete();
        return $"Language: {language}\nSegments: {segments.Count}\n---\n{string.Join(' ', segments)}";
    }

    private static async Task ConvertToWavAsync(string input, string output, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg.exe",
            Arguments = $"-y -i \"{input}\" -ar 16000 -ac 1 -f wav \"{output}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p =
            Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.exe process.");
        await p.WaitForExitAsync(ct);
    }

    private SpeechConfig BuildSpeechConfig()
    {
        var endpoint = new Uri(opts.Value.SpeechEndpoint!);
        return SpeechConfig.FromEndpoint(endpoint, opts.Value.SpeechKey!);
    }
}
