namespace Services.Pristine;

public sealed record PristineAlbumResult
{
	public required string Code { get; init; }
	public required string Title { get; init; }
	public required string OutPath { get; init; }
	public int Expected { get; init; }
	public int Downloaded { get; init; }
	public int Resumed { get; init; }
}

public sealed record PristineApiTrack
{
	public required long Id { get; init; }
	public required string Title { get; init; }
	public required int Position { get; init; }
	public required bool Available { get; init; }
}

public sealed record PristineApiAlbum
{
	public required long Id { get; init; }
	public required string Code { get; init; }
	public required string Title { get; init; }
	public string? ArtworkPath { get; init; }
}

public sealed record PristineApiListenSources
{
	public string? Flac { get; init; }
	public string? Mp3 { get; init; }
}
