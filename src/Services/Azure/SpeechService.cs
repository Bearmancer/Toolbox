using System.Diagnostics;
using System.Text.RegularExpressions;
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
	private const int ChunkMaxChars = 6_000;
	private const int ChunkTimeoutSeconds = 600;

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
				Telemetry.Error(
					"Speech: ffmpeg conversion failed for {File}: {Error}",
					path,
					ex.Message
				);
				return Errors.Speech.ApiError(ex.Message);
			}
		}

		try
		{
			SpeechConfig config = BuildSpeechConfig();
			config.SpeechRecognitionLanguage = language;

			using SpeechRecognizer recognizer = new(config, AudioConfig.FromWavFileInput(wavPath));
			List<string> segments = [];
			TaskCompletionSource<bool> stopped = new();

			recognizer.Recognized += (_, e) =>
			{
				if (!string.IsNullOrEmpty(e.Result.Text))
					segments.Add(e.Result.Text);
			};
			recognizer.SessionStopped += (_, _) => stopped.TrySetResult(true);
			recognizer.Canceled += (_, _) => stopped.TrySetResult(true);

			await recognizer.StartContinuousRecognitionAsync();
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(TimeSpan.FromSeconds(MaxDurationSeconds));
			await using CancellationTokenRegistration registration = cts.Token.Register(() =>
				stopped.TrySetResult(true)
			);
			await stopped.Task;

			await recognizer.StopContinuousRecognitionAsync();

			return $"Language: {language}\nSegments: {segments.Count}\n---\n{string.Join(' ', segments)}";
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				"Speech: transcription failed for {File} lang={Language}: {Error}",
				path,
				language,
				ex.Message
			);
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
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 512K"
			);

		return await SynthesizeCoreAsync(text, voice, outputPath, ct);
	}

	public async Task<ErrorOr<string>> SynthesizeFromFileAsync(
		string filePath,
		string voice,
		string outputPath,
		CancellationToken ct
	)
	{
		var path = PathResolver.ResolveInput(filePath);
		PathResolver.ReadChecked(path, MaxTextLength, "Speech TTS");

		var text = await File.ReadAllTextAsync(path, ct);

		if (string.IsNullOrWhiteSpace(text))
			return Errors.Validation.InvalidInput(nameof(filePath), "File contains no text");

		return await SynthesizeCoreAsync(text, voice, outputPath, ct);
	}

	private async Task<ErrorOr<string>> SynthesizeCoreAsync(
		string text,
		string voice,
		string outputPath,
		CancellationToken ct
	)
	{
		Telemetry.Debug(
			"Speech: starting synthesis — voice={Voice}, text length={Length}",
			voice,
			text.Length
		);

		SpeechConfig config = BuildSpeechConfig();
		config.SpeechSynthesisVoiceName = voice;
		config.SetSpeechSynthesisOutputFormat(
			SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3
		);

		List<string> chunks = SplitTextIntoChunks(text, ChunkMaxChars);
		Telemetry.Debug("Speech: split into {ChunkCount} chunks", chunks.Count);

		using SpeechSynthesizer synth = new(speechConfig: config);
		Stopwatch sw = Stopwatch.StartNew();
		List<byte[]> allAudio = [];
		var charsProcessed = 0;

		synth.Synthesizing += (_, e) =>
			Telemetry.Verbose(
				"Speech: audio chunk — {Bytes} bytes",
				e.Result.AudioData?.Length ?? 0
			);

		for (var i = 0; i < chunks.Count; i++)
		{
			ct.ThrowIfCancellationRequested();

			var chunk = chunks[i];
			Telemetry.Debug(
				"Speech: synthesizing chunk {Index}/{Total} ({Chars} chars)",
				i + 1,
				chunks.Count,
				chunk.Length
			);

			Task<SpeechSynthesisResult> speakTask = synth.SpeakTextAsync(chunk);
			Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(ChunkTimeoutSeconds), ct);
			Task completed = await Task.WhenAny(speakTask, timeoutTask);

			if (completed == timeoutTask)
			{
				await synth.StopSpeakingAsync();
				Telemetry.Error(
					"Speech: chunk {Index} timed out after {Seconds}s",
					i + 1,
					ChunkTimeoutSeconds
				);
				return Errors.Speech.ApiError(
					$"Chunk {i + 1} synthesis timed out after {ChunkTimeoutSeconds}s"
				);
			}

			SpeechSynthesisResult result = await speakTask;

			if (result.Reason != ResultReason.SynthesizingAudioCompleted)
			{
				Telemetry.Error(
					"Speech: chunk {Index} failed — reason={Reason}",
					i + 1,
					result.Reason
				);
				return Errors.Speech.ApiError($"Chunk {i + 1} synthesis failed: {result.Reason}");
			}

			if (result.AudioData is null || result.AudioData.Length == 0)
			{
				Telemetry.Error("Speech: chunk {Index} returned empty audio", i + 1);
				return Errors.Speech.ApiError($"Chunk {i + 1} returned no audio data");
			}

			allAudio.Add(result.AudioData);
			charsProcessed += chunk.Length;

			var pct = (int)((double)charsProcessed / text.Length * 100);
			Telemetry.Info(
				"Speech: chunk {Index}/{Total} complete — {Pct}% ({Chars}/{TotalChars} chars) [{Elapsed}]",
				i + 1,
				chunks.Count,
				pct,
				charsProcessed,
				text.Length,
				sw.Elapsed.ToString(@"mm\:ss")
			);
		}

		var totalAudio = allAudio.SelectMany(bytes => bytes).ToArray();
		await File.WriteAllBytesAsync(outputPath, totalAudio, ct);

		Telemetry.Info(
			"Speech: synthesis complete — {Bytes} bytes in {Elapsed}",
			totalAudio.Length,
			sw.Elapsed.ToString(@"mm\:ss")
		);
		return $"Voice: {voice}\nSaved: {outputPath}\nSize: {totalAudio.Length:N0} bytes";
	}

	private static List<string> SplitTextIntoChunks(string text, int maxChars)
	{
		List<string> chunks = [];
		var sentences = Regex.Split(text, @"(?<=[.!?])\s+");

		System.Text.StringBuilder current = new();
		foreach (var sentence in sentences)
		{
			if (current.Length + sentence.Length > maxChars && current.Length > 0)
			{
				chunks.Add(current.ToString().Trim());
				current.Clear();
			}
			current.Append(sentence).Append(' ');
		}

		if (current.Length > 0)
			chunks.Add(current.ToString().Trim());

		return chunks;
	}

	private static async Task ConvertToWavAsync(string input, string output, CancellationToken ct)
	{
		ProcessStartInfo psi = new()
		{
			FileName = "ffmpeg.exe",
			Arguments =
				$"-y -i \"{input.Replace("\"", "\\\"")}\" -ar 16000 -ac 1 -f wav \"{output.Replace("\"", "\\\"")}\"",
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		using Process p =
			Process.Start(psi)
			?? throw new InvalidOperationException("Failed to start ffmpeg.exe process.");
		Task<string> stderrTask = p.StandardError.ReadToEndAsync(ct);
		await p.WaitForExitAsync(ct);
		var stderr = await stderrTask;
		if (p.ExitCode != 0)
			throw new InvalidOperationException($"ffmpeg failed (exit {p.ExitCode}): {stderr}");
	}

	private SpeechConfig BuildSpeechConfig()
	{
		Uri endpoint = new(opts.SpeechEndpoint);
		return SpeechConfig.FromEndpoint(endpoint, opts.SpeechKey);
	}
}
