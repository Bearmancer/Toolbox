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
	public static (DsdConversionSettings Primary, DsdConversionSettings? Derived) ForDsdRate(
		int dsdSampleRate,
		AudioOutputFormat format,
		double gain
	) =>
		dsdSampleRate switch
		{
			2822400 => format switch
			{
				AudioOutputFormat.Bit16 => (new DsdConversionSettings(44100, 16, gain), null),
				AudioOutputFormat.Bit24 => (new DsdConversionSettings(88200, 24, gain), null),
				AudioOutputFormat.Both => (
					new DsdConversionSettings(88200, 24, gain),
					new DsdConversionSettings(44100, 16, gain)
				),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			5644800 => format switch
			{
				AudioOutputFormat.Bit16 => (new DsdConversionSettings(88200, 16, gain), null),
				AudioOutputFormat.Bit24 => (new DsdConversionSettings(176400, 24, gain), null),
				AudioOutputFormat.Both => (
					new DsdConversionSettings(176400, 24, gain),
					new DsdConversionSettings(88200, 16, gain)
				),
				_ => throw new InvalidOperationException($"Unsupported format: {format}"),
			},
			_ => throw new InvalidOperationException(
				$"Unsupported DSD sample rate {dsdSampleRate} Hz. Expected 2822400 (DSD64) or 5644800 (DSD128)."
			),
		};
}

public enum AudioOutputFormat
{
	Bit16,
	Bit24,
	Both,
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
	List<string> RecoverableErrors
);
