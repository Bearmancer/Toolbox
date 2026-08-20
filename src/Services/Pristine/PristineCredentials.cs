namespace Services.Pristine;

public sealed class PristineCredentials
{
	private static readonly string DefaultOutDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
		"Pristine"
	);

	public required string BaseOutDir { get; init; }

	public static PristineCredentials Read() =>
		new()
		{
			BaseOutDir = DefaultOutDir,
		};

	public static string ResolveOutDir(string? outDir) =>
		string.IsNullOrWhiteSpace(outDir) ? DefaultOutDir : outDir;
}
