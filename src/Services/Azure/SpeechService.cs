using System.Diagnostics;
using Core;
using ErrorOr;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Services.Azure;

public class SpeechService(AzureCredentials opts)
{
    private const int MaxBytes = 100_000_000;
    private const int MaxDurationSeconds = 120;
    private const int MaxTextLength = 512_000;

    public async Task<ErrorOr<string>> TranscribeAsync(
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
            try
            {
                await ConvertToWavAsync(path, wavPath, ct);
            }
            catch (Exception ex)
            {
                return Errors.Speech.ApiError(ex.Message);
            }
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
        catch (Exception ex)
        {
            return Errors.Speech.ApiError(ex.Message);
        }
        finally
        {
            if (isTempFile && File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

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

    private static async Task ConvertToWavAsync(string input, string output, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg.exe",
            Arguments = $"-y -i \"{input.Replace("\"", "\\\"")}\" -ar 16000 -ac 1 -f wav \"{output.Replace("\"", "\\\"")}\"",
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
