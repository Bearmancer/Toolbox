using System.ComponentModel;
using Core;
using Services.Audio;
using Spectre.Console.Cli;

namespace CLI.Audio;

internal sealed class DsdConvertCommand(
	DsdConvertService convertService,
	AudioMetadataService metadataService
) : AsyncCommand<DsdConvertCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[Description("Input DSF or DFF file")]
		[CommandArgument(0, "<input>")]
		public required string Input { get; init; }

		[Description("Output FLAC file path")]
		[CommandArgument(1, "[output]")]
		public string? Output { get; init; }

		[Description("Gain in dB (default: auto-detect from volumedetect)")]
		[CommandOption("-g|--gain")]
		public double? GainDb { get; init; }

		[Description("Output format: 16 (default), 24, both")]
		[CommandOption("-f|--format")]
		public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Bit16;

		[Description("Copy metadata from source DSD file to output FLAC")]
		[CommandOption("--copy-tags")]
		public bool CopyTags { get; init; }
	}

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings settings,
		CancellationToken cancellationToken
	)
	{
		using var _ = Telemetry.ForService(ServiceName.Audio);

		var inputPath = Path.GetFullPath(settings.Input);
		var outputPath = settings.Output ?? Path.ChangeExtension(inputPath, ".flac");

		if (!File.Exists(inputPath))
		{
			await Console.Error.WriteLineAsync(
				$"Input file not found: {inputPath}",
				cancellationToken
			);
			return 1;
		}

		Telemetry.Info("Probing DSD file: {File}", inputPath);
		var dsdProbe = await convertService.ProbeDsdAsync(inputPath, cancellationToken);
		if (dsdProbe.IsError)
		{
			await Console.Error.WriteLineAsync(dsdProbe.Errors[0].Description, cancellationToken);
			return 1;
		}

		Telemetry.Info(
			"DSD: {Codec} @ {Rate} Hz, {Channels} ch",
			dsdProbe.Value.CodecName,
			dsdProbe.Value.SampleRate,
			dsdProbe.Value.Channels
		);

		var gain = settings.GainDb ?? 0.0;

		if (settings.GainDb is null)
		{
			Telemetry.Info("Auto-detecting gain for {File}", inputPath);
			var gainResult = await convertService.CalculateGainAsync(inputPath, cancellationToken);
			if (gainResult.IsError)
			{
				await Console.Error.WriteLineAsync(
					gainResult.Errors[0].Description,
					cancellationToken
				);
				return 1;
			}
			gain = gainResult.Value;
		}

		Telemetry.Info("Converting with gain {Gain:F2} dB", gain);

		var (primary, derived) = DsdConversionSettings.ForDsdRate(
			dsdProbe.Value.SampleRate,
			settings.Format,
			gain
		);

		var result = await convertService.ConvertFullDffAsync(
			inputPath,
			outputPath,
			primary,
			cancellationToken
		);

		if (result.IsError)
		{
			await Console.Error.WriteLineAsync(result.Errors[0].Description, cancellationToken);
			return 1;
		}

		if (derived is not null)
		{
			var derivedPath =
				Path.ChangeExtension(outputPath, null) + $" [16-bit {derived.SampleRate}].flac";
			Telemetry.Info("Deriving 16-bit: {File}", Path.GetFileName(derivedPath));

			var deriveResult = await convertService.DeriveFlacAsync(
				outputPath,
				derivedPath,
				derived.SampleRate,
				cancellationToken
			);
			if (deriveResult.IsError)
				Telemetry.Warn("Derive failed: {Error}", deriveResult.Errors[0].Description);
		}

		if (settings.CopyTags)
		{
			var metaResult = metadataService.ReadDsdMetadata(inputPath);
			if (!metaResult.IsError)
			{
				var tagResult = metadataService.WriteFlacTags(outputPath, metaResult.Value);
				if (tagResult.IsError)
					Telemetry.Warn("Tagging failed: {Error}", tagResult.Errors[0].Description);
			}
		}

		await Console.Out.WriteLineAsync(
			$"Converted: {inputPath} → {outputPath} ({result.Value.FileSizeBytes / 1024 / 1024} MB)",
			cancellationToken
		);
		return 0;
	}
}
