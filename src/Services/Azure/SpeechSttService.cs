using System.Diagnostics;
using Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Services.Azure;

public class SpeechSttService(AzureCredentials opts)
{
    private const int MaxBytes = 100_000_000;
    private const int MaxDurationSeconds = 120;

    public async Task<string> TranscribeAsync(
        string filePath,
        string language,
        CancellationToken ct
    )
    {
        var path = PathResolver.ResolveInput(filePath);
        PathResolver.ReadChecked(path, MaxBytes, "Speech");

        var wavPath = path;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var isTempFile = false;
        if (ext is not ".wav")
        {
            wavPath = Path.Combine(Path.GetTempPath(), $"azureai-{Guid.NewGuid():N}.wav");
            isTempFile = true;
            await ConvertToWavAsync(path, wavPath, ct);
        }

        try
        {
            var config = BuildSpeechConfig();
            config.SpeechRecognitionLanguage = language;

            using var recognizer = new SpeechRecognizer(
                config,
                AudioConfig.FromWavFileInput(wavPath)
            );
            var segments = new List<string>();
            var stopped = new TaskCompletionSource<bool>();

            recognizer.Recognized += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Result.Text))
                    segments.Add(e.Result.Text);
            };
            recognizer.SessionStopped += (_, _) => stopped.TrySetResult(true);
            recognizer.Canceled += (_, _) => stopped.TrySetResult(true);

            await recognizer.StartContinuousRecognitionAsync();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(MaxDurationSeconds));
            await using var registration = cts.Token.Register(() => stopped.TrySetResult(true));
            await stopped.Task;

            await recognizer.StopContinuousRecognitionAsync();

            return $"Language: {language}\nSegments: {segments.Count}\n---\n{string.Join(' ', segments)}";
        }
        finally
        {
            if (isTempFile && File.Exists(wavPath))
                File.Delete(wavPath);
        }
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
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed (exit {p.ExitCode}): {stderr}");
    }

    private SpeechConfig BuildSpeechConfig()
    {
        var endpoint = new Uri(opts.SpeechEndpoint);
        return SpeechConfig.FromEndpoint(endpoint, opts.SpeechKey);
    }
}
