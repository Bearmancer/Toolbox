using System.ComponentModel;
using System.Globalization;

namespace Services.Audio;

public sealed record SacdDisc(
	string IsoPath,
	string AlbumTitle,
	string? AlbumArtist,
	string? Publisher,
	int? Year,
	string? CatalogNumber,
	string? Genre,
	bool HasStereo,
	bool HasMultichannel,
	List<SacdTrack> Tracks
);

public sealed record SacdTrack(
	int TrackNumber,
	string Title,
	string? Artist,
	string? Isrc,
	TimeSpan StartOffset,
	TimeSpan? Duration
);

public sealed record DsdConversionSettings(int SampleRate, int BitDepth, double GainDb)
{
	public static DsdConversionSettings ForDsdRate(
		int dsdSampleRate,
		AudioOutputFormat format,
		double gain
	) =>
		dsdSampleRate switch
		{
			2822400 => format switch
			{
				AudioOutputFormat.Bit16 => new DsdConversionSettings(44100, 16, gain),
				AudioOutputFormat.Bit24 => new DsdConversionSettings(88200, 24, gain),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			5644800 => format switch
			{
				AudioOutputFormat.Bit16 => new DsdConversionSettings(88200, 16, gain),
				AudioOutputFormat.Bit24 => new DsdConversionSettings(176400, 24, gain),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			_ => throw new InvalidOperationException(
				$"Unsupported DSD sample rate {dsdSampleRate} Hz. Expected 2822400 (DSD64) or 5644800 (DSD128)."
			),
		};
}

[TypeConverter(typeof(AudioOutputFormatConverter))]
public enum AudioOutputFormat
{
	Bit16,
	Bit24,
}

/// <summary>
/// Parses <c>--format</c> option values for <see cref="AudioOutputFormat"/>.
/// Spectre.Console.Cli's default enum binding falls through to <see cref="Enum.Parse(Type, string)"/>,
/// which treats a purely numeric string as the enum's underlying integer value rather than a
/// member name to match. Since <see cref="AudioOutputFormat.Bit24"/>'s underlying value is 1
/// (not 24), typing <c>--format 24</c> would silently bind the undefined value
/// <c>(AudioOutputFormat)24</c> instead of <see cref="AudioOutputFormat.Bit24"/>. This converter
/// accepts the documented bit-depth numbers ("16"/"24") and the literal member names
/// ("Bit16"/"Bit24"), and rejects everything else with a clear error instead of silently
/// falling through to an out-of-range value.
/// </summary>
public sealed class AudioOutputFormatConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object ConvertFrom(
		ITypeDescriptorContext? context,
		CultureInfo? culture,
		object value
	)
	{
		if (value is string text)
		{
			var trimmed = text.Trim();

			if (
				string.Equals(trimmed, "16", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(
					trimmed,
					nameof(AudioOutputFormat.Bit16),
					StringComparison.OrdinalIgnoreCase
				)
			)
				return AudioOutputFormat.Bit16;

			if (
				string.Equals(trimmed, "24", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(
					trimmed,
					nameof(AudioOutputFormat.Bit24),
					StringComparison.OrdinalIgnoreCase
				)
			)
				return AudioOutputFormat.Bit24;

			throw new FormatException(
				$"Unsupported --format value '{text}'. Expected 16, 24, Bit16, or Bit24."
			);
		}

		throw new NotSupportedException(
			$"Cannot convert value of type '{value.GetType()}' to {nameof(AudioOutputFormat)}."
		);
	}

	public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
		new(new object[] { AudioOutputFormat.Bit16, AudioOutputFormat.Bit24 });
}

public sealed record DsdProbeResult(
	string FilePath,
	string CodecName,
	int SampleRate,
	int Channels
);

public sealed record ConversionResult(string OutputPath, TimeSpan Duration, long FileSizeBytes);

public sealed record CueSheet(
	string SourceFile,
	string? AlbumTitle,
	string? AlbumArtist,
	string? Genre,
	string? Date,
	List<CueTrack> Tracks
);

public sealed record CueTrack(
	int TrackNumber,
	string Title,
	string? Performer,
	string? Isrc,
	TimeSpan StartTime,
	TimeSpan? Duration
);

public sealed record PipelineResult(
	int SucceededCount,
	int FailedCount,
	List<string> RecoverableErrors,
	List<string> GuardFailedDiscs
);
