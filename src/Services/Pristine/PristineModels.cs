namespace Services.Pristine;

public sealed record PristineDownloadConfig
{
	public required string Code { get; init; }
	public string? OutDir { get; init; }
}

public sealed record PristineAlbumResult
{
	public required string Code { get; init; }
	public required string Title { get; init; }
	public required string OutPath { get; init; }
	public int Expected { get; init; }
	public int Downloaded { get; init; }
}
