namespace Core;

public static class PathResolver
{
	private static readonly Lazy<string> LazyRepoRoot = new(() =>
	{
		var dir = AppContext.BaseDirectory;

		for (var i = 0; i < 10; i++)
		{
			if (
				Directory.EnumerateFileSystemEntries(dir, ".git").Any()
				|| File.Exists(Path.Combine(dir, ".env"))
			)
				return dir;

			DirectoryInfo? parent = Directory.GetParent(dir);
			if (parent is null)
				break;

			dir = parent.FullName;
		}

		return Directory.GetCurrentDirectory();
	});

	public static string RepoRoot => LazyRepoRoot.Value;

	public static string GetStatePath(string subdirectory) =>
		Path.Combine(RepoRoot, "state", subdirectory);

	private static string ResourcesRoot => Path.Combine(RepoRoot, "resources");

	public static string ResolveInput(string input)
	{
		var path = Path.IsPathRooted(input) ? input : Path.GetFullPath(input, ResourcesRoot);

		return File.Exists(path)
			? path
			: throw new FileNotFoundException($"File not found: {input}");
	}

	public static byte[] ReadChecked(string path, long maxBytes, string serviceName)
	{
		var info = new FileInfo(path);
		return info.Length > maxBytes
			? throw new ArgumentOutOfRangeException(
				nameof(path),
				$"Payload too large: {info.Length} bytes exceeds {maxBytes} byte limit for {serviceName}"
			)
			: File.ReadAllBytes(path);
	}
}
