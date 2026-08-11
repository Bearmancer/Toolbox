using System.Globalization;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class SaraconService(ProcessRunner processRunner, string binaryPath)
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);

	public async Task<ErrorOr<string>> ConvertDsdToPcmAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "wav");
		return await RunConversionAsync(inputDff, outputDir, "wav", args, sampleRate, bitDepth, gainDb, onOutputLine, ct);
	}

	public async Task<ErrorOr<string>> ConvertDsdToFlacAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "flac");
		return await RunConversionAsync(inputDff, outputDir, "flac", args, sampleRate, bitDepth, gainDb, onOutputLine, ct);
	}

	private static string[] BuildD2pArgs(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		string format
	) =>
	[
		"-c", "d2p",
		"-r", sampleRate.ToString(CultureInfo.InvariantCulture),
		"-f", format,
		"-n", $"{bitDepth}bit",
		"-d", "tpdf",
		"-g", gainDb.ToString("F2", CultureInfo.InvariantCulture),
		"-T",
		"-V", "all",
		"-t", outputDir,
		inputDff,
	];

	// No retry loop. Retrying a failed/timed-out Saracon launch respawns a new
	// process against the same output filename while the previous instance's
	// partial write may not have fully released — that's the mechanism behind the
	// self-restart death loop on the original Disc 10 DFF. Saracon either finishes
	// cleanly within the timeout or it doesn't; there's no transient failure here
	// worth retrying automatically. If a "just try again" behavior turns out to be
	// needed in practice, add it at the PipelineOrchestrator/disc level (re-run the
	// whole disc after cleaning up prior output), not inside a single Saracon call.
	private async Task<ErrorOr<string>> RunConversionAsync(
		string inputDff,
		string outputDir,
		string extension,
		string[] args,
		int sampleRate,
		int bitDepth,
		double gainDb,
		Action<string>? onOutputLine,
		CancellationToken ct
	)
	{
		Telemetry.Debug(
			"Saracon.ConvertStart input={Input} outputDir={OutputDir} format={Format} rate={Rate} bitDepth={BitDepth} gain={Gain}dB",
			Path.GetFileName(inputDff), outputDir, extension, sampleRate, bitDepth, gainDb);

		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		var effectiveInput = inputDff;
		var effectiveArgs = args;

		if (DffMetadataStripper.HasId3Chunk(inputDff))
		{
			Telemetry.Warn("Saracon.Id3Detected input={Input} — ID3 chunk found, stripping before conversion",
				Path.GetFileName(inputDff));

			var stripResult = await DffMetadataStripper.StripId3TagsAsync(inputDff, outputDir, ct);
			if (stripResult.IsError)
			{
				// Hard failure, deliberately: falling back to converting the
				// original ID3-laden file would silently reintroduce the exact
				// condition under investigation, with no retry loop left to mask it.
				Telemetry.Error("Saracon.Id3StripFailed input={Input} error={Error}",
					Path.GetFileName(inputDff), stripResult.Errors[0].Description);
				return stripResult.Errors;
			}

			effectiveInput = stripResult.Value;
			effectiveArgs = BuildD2pArgs(effectiveInput, outputDir, sampleRate, bitDepth, gainDb, extension);
		}

		var result = await processRunner.RunAsync(
			binaryPath,
			effectiveArgs,
			ct,
			timeout: DefaultTimeout,
			onOutputLine: onOutputLine,
			completionPattern: "100%",
			completionTimeout: TimeSpan.FromSeconds(10)
		);

		if (result.IsError)
		{
			Telemetry.Error("Saracon.ConversionFailed input={Input} error={Error}",
				Path.GetFileName(inputDff), result.Errors[0].Description);
			return result.Errors;
		}

		if (result.Value.ExitCode != 0)
		{
			var stderr = result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)];
			Telemetry.Error("Saracon.ConversionFailed input={Input} exitCode={ExitCode} stderr={Stderr}",
				Path.GetFileName(inputDff), result.Value.ExitCode, stderr);
			return Errors.Audio.ConversionFailed(inputDff, $"saracon exit code {result.Value.ExitCode}: {stderr}");
		}

		// Match against the file Saracon actually converted (post-strip, if
		// stripping happened) — Saracon names its output from that path, not from
		// the original disc filename.
		var baseName = Path.GetFileNameWithoutExtension(effectiveInput);
		var expectedOutput = Path.Combine(outputDir, baseName + $".{extension}");

		if (!File.Exists(expectedOutput))
		{
			// Saracon commonly appends "-d2p" to the output filename — check that
			// explicit variant. Deliberately not a glob/prefix scan: a glob can
			// match a stale 0-byte file left behind by an earlier failed attempt in
			// the same output directory and report false success.
			var d2pOutput = Path.Combine(outputDir, baseName + $"-d2p.{extension}");
			if (File.Exists(d2pOutput))
			{
				expectedOutput = d2pOutput;
			}
			else
			{
				Telemetry.Error("Saracon.OutputNotFound input={Input} tried={Tried1},{Tried2}",
					Path.GetFileName(inputDff), Path.GetFileName(expectedOutput), Path.GetFileName(d2pOutput));
				return Errors.Audio.ConversionFailed(
					inputDff,
					$"saracon reported success but neither {Path.GetFileName(expectedOutput)} nor {Path.GetFileName(d2pOutput)} found in {outputDir}"
				);
			}
		}

		var outputSize = new FileInfo(expectedOutput).Length;
		var expectedPcmBytes = EstimateExpectedPcmBytes(effectiveInput, sampleRate, bitDepth);
		if (expectedPcmBytes > 0 && outputSize < expectedPcmBytes / 2)
		{
			Telemetry.Warn("Saracon.OutputTooSmall output={Output} size={Size}MB expected~{Expected}MB",
				Path.GetFileName(expectedOutput), outputSize / 1_048_576.0, expectedPcmBytes / 1_048_576.0);
			return Errors.Audio.ConversionFailed(inputDff,
				$"saracon output {Path.GetFileName(expectedOutput)} is {outputSize} bytes — expected ~{expectedPcmBytes} (truncated conversion)");
		}

		Telemetry.Debug("Saracon.ConvertComplete output={Output} size={Size}MB",
			Path.GetFileName(expectedOutput), outputSize / 1_048_576.0);

		return expectedOutput;
	}

	private static long EstimateExpectedPcmBytes(string dffPath, int sampleRate, int bitDepth)
	{
		try
		{
			using var stream = File.OpenRead(dffPath);
			var magic = new byte[4];
			stream.ReadExactly(magic, 0, 4);
			if (System.Text.Encoding.ASCII.GetString(magic) != "FRM8")
				return 0;

			stream.Seek(16, SeekOrigin.Begin); // skip magic(4) + size(8) + form type(4) = 16
			long dsdBytes = 0;
			while (stream.Position < stream.Length - 12)
			{
				var idBuf = new byte[4];
				stream.ReadExactly(idBuf, 0, 4);
				var id = System.Text.Encoding.ASCII.GetString(idBuf);
				var sizeBuf = new byte[8];
				stream.ReadExactly(sizeBuf, 0, 8);
				var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(sizeBuf);

				if (id == "DSD ")
					dsdBytes = (long)size;

				var skip = (long)size;
				if (skip <= 0) break; // malformed: zero-size chunk mid-walk
				if (skip % 2 != 0) skip++;
				if (stream.Position + skip > stream.Length) break; // miscoded size: bound by EOF
				stream.Seek(skip, SeekOrigin.Current);
			}

			if (dsdBytes == 0) return 0;
			// DSD64: bit clock 2822400 Hz → PCM 88.2kHz (decimate by 32)
			// PCM bytes/sec = (bitClock / 32) * channels * bytesPerSample
			var durationSeconds = dsdBytes / (2822400.0 / 8.0 * 2); // stereo DSD bytes
			return (long)(durationSeconds * sampleRate * 2 * (bitDepth / 8.0));
		}
		catch
		{
			return 0;
		}
	}
}
