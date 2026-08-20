using System.Globalization;
using System.Text;
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
		int dsdSampleRate,
		int channels,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "wav");
		return await RunConversionAsync(
			inputDff,
			outputDir,
			"wav",
			args,
			sampleRate,
			bitDepth,
			gainDb,
			dsdSampleRate,
			channels,
			onOutputLine,
			ct
		);
	}

	public async Task<ErrorOr<string>> ConvertDsdToFlacAsync(
		string inputDff,
		string outputDir,
		int sampleRate,
		int bitDepth,
		double gainDb,
		int dsdSampleRate,
		int channels,
		Action<string>? onOutputLine = null,
		CancellationToken ct = default
	)
	{
		var args = BuildD2pArgs(inputDff, outputDir, sampleRate, bitDepth, gainDb, "flac");
		return await RunConversionAsync(
			inputDff,
			outputDir,
			"flac",
			args,
			sampleRate,
			bitDepth,
			gainDb,
			dsdSampleRate,
			channels,
			onOutputLine,
			ct
		);
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
			"-c",
			"d2p",
			"-r",
			sampleRate.ToString(CultureInfo.InvariantCulture),
			"-f",
			format,
			"-n",
			$"{bitDepth}bit",
			"-d",
			"tpdf",
			"-g",
			gainDb.ToString("F2", CultureInfo.InvariantCulture),
			"-T",
			"-V",
			"all",
			"-t",
			outputDir,
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
		int dsdSampleRate,
		int channels,
		Action<string>? onOutputLine,
		CancellationToken ct
	)
	{
		Telemetry.Debug(
			"Saracon.ConvertStart input={Input} outputDir={OutputDir} format={Format} rate={Rate} bitDepth={BitDepth} gain={Gain}dB",
			Path.GetFileName(inputDff),
			Path.GetFileName(outputDir),
			extension,
			sampleRate,
			bitDepth,
			gainDb
		);

		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		ErrorOr<ProcessResult> result = await processRunner.RunAsync(
			binaryPath,
			args,
			ct,
			timeout: DefaultTimeout,
			inactivityTimeout: TimeSpan.FromMinutes(5),
			onOutputLine: onOutputLine ?? (line => Telemetry.Debug("Saracon.Output {Line}", line))
		);

		if (result.IsError)
		{
			Telemetry.Error(
				"Saracon.ConversionFailed input={Input} error={Error}",
				Path.GetFileName(inputDff),
				result.Errors[0].Description
			);
			return result.Errors;
		}

		if (
			result.Value.TerminationReason != TerminationReason.Exited
			|| result.Value.ExitCode != 0
		)
		{
			var stderr = result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)];
			Telemetry.Error(
				"Saracon.ConversionFailed input={Input} reason={Reason} exitCode={ExitCode} stderr={Stderr}",
				Path.GetFileName(inputDff),
				result.Value.TerminationReason,
				result.Value.ExitCode,
				stderr
			);
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon terminated with {result.Value.TerminationReason}, exit code {result.Value.ExitCode}: {stderr}"
			);
		}

		var baseName = Path.GetFileNameWithoutExtension(inputDff);
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
				Telemetry.Error(
					"Saracon.OutputNotFound input={Input} tried={Tried1},{Tried2}",
					Path.GetFileName(inputDff),
					Path.GetFileName(expectedOutput),
					Path.GetFileName(d2pOutput)
				);
				return Errors.Audio.ConversionFailed(
					inputDff,
					$"saracon reported success but neither {Path.GetFileName(expectedOutput)} nor {Path.GetFileName(d2pOutput)} found in {outputDir}"
				);
			}
		}

		var outputSize = new FileInfo(expectedOutput).Length;
		if (!IsExpectedOutput(expectedOutput, extension, sampleRate, bitDepth, channels))
		{
			Telemetry.Error(
				"Saracon.OutputInvalid input={Input} output={Output} format={Format}",
				Path.GetFileName(inputDff),
				Path.GetFileName(expectedOutput),
				extension
			);
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon output {Path.GetFileName(expectedOutput)} has invalid {extension} structure"
			);
		}

		var expectedPcmBytes = EstimateExpectedPcmBytes(
			inputDff,
			dsdSampleRate,
			sampleRate,
			channels,
			bitDepth
		);
		if (expectedPcmBytes > 0 && outputSize < expectedPcmBytes / 2)
		{
			Telemetry.Warn(
				"Saracon.OutputTooSmall output={Output} size={Size}MB expected~{Expected}MB",
				Path.GetFileName(expectedOutput),
				outputSize / 1_048_576.0,
				expectedPcmBytes / 1_048_576.0
			);
			return Errors.Audio.ConversionFailed(
				inputDff,
				$"saracon output {Path.GetFileName(expectedOutput)} is {outputSize} bytes — expected ~{expectedPcmBytes} (truncated conversion)"
			);
		}

		Telemetry.Debug(
			"Saracon.ConvertComplete output={Output} size={Size}MB",
			Path.GetFileName(expectedOutput),
			outputSize / 1_048_576.0
		);

		return expectedOutput;
	}

	private static bool IsExpectedOutput(
		string outputPath,
		string extension,
		int sampleRate,
		int bitDepth,
		int channels
	)
	{
		if (extension == "flac")
		{
			using FileStream stream = File.OpenRead(outputPath);
			Span<byte> magic = stackalloc byte[4];
			return stream.Length >= magic.Length
				&& stream.Read(magic) == magic.Length
				&& Encoding.ASCII.GetString(magic) == "fLaC";
		}

		using FileStream wav = File.OpenRead(outputPath);
		using BinaryReader reader = new(wav);
		if (wav.Length < 44 || new string(reader.ReadChars(4)) != "RIFF")
			return false;
		reader.ReadUInt32();
		if (new string(reader.ReadChars(4)) != "WAVE")
			return false;

		var hasFormat = false;
		var hasData = false;
		while (wav.Position + 8 <= wav.Length)
		{
			var chunkId = new string(reader.ReadChars(4));
			var chunkSize = reader.ReadUInt32();
			var chunkEnd = wav.Position + chunkSize + (chunkSize & 1);
			if (chunkEnd > wav.Length)
				return false;

			if (chunkId == "fmt " && chunkSize >= 16)
			{
				hasFormat =
					reader.ReadUInt16() == 1
					&& reader.ReadUInt16() == channels
					&& reader.ReadUInt32() == sampleRate;
				reader.ReadUInt32();
				reader.ReadUInt16();
				hasFormat &= reader.ReadUInt16() == bitDepth;
			}
			else if (chunkId == "data" && chunkSize > 0)
			{
				hasData = true;
			}

			wav.Position = chunkEnd;
		}

		return hasFormat && hasData;
	}

	private static long EstimateExpectedPcmBytes(
		string dffPath,
		int dsdSampleRate,
		int sampleRate,
		int channels,
		int bitDepth
	)
	{
		try
		{
			using FileStream stream = File.OpenRead(dffPath);
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
				if (skip <= 0)
					break; // malformed: zero-size chunk mid-walk
				if (skip % 2 != 0)
					skip++;
				if (stream.Position + skip > stream.Length)
					break; // miscoded size: bound by EOF
				stream.Seek(skip, SeekOrigin.Current);
			}

			if (dsdBytes == 0)
				return 0;
			var durationSeconds = dsdBytes / (dsdSampleRate / 8.0 * channels);
			return (long)(durationSeconds * sampleRate * channels * (bitDepth / 8.0));
		}
		catch
		{
			return 0;
		}
	}
}
