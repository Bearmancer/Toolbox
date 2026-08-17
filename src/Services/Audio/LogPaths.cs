namespace Services.Audio;

public static class LogPaths
{
	private static string? IsoRoot;
	private static string? OutputRoot;

	public static void Setup(string isoRoot, string outputRoot)
	{
		IsoRoot = Normalise(isoRoot);
		OutputRoot = Normalise(outputRoot);
	}

	public static void Reset()
	{
		IsoRoot = null;
		OutputRoot = null;
	}

	public static string Format(string path)
	{
		if (IsoRoot is { } isoRoot && IsWithin(path, isoRoot))
			return FormatRooted(path, isoRoot, "ISO");

		if (OutputRoot is { } outputRoot && IsWithin(path, outputRoot))
			return FormatRooted(path, outputRoot, "OUT");

		var tempRoot = Normalise(Path.GetTempPath());
		if (IsWithin(path, tempRoot))
			return FormatRooted(path, tempRoot, "TMP");

		return path;
	}

	public static string FormatText(string text)
	{
		var result = text;
		if (IsoRoot is { } isoRoot)
			result = ReplaceRoot(result, isoRoot, "ISO");
		if (OutputRoot is { } outputRoot)
			result = ReplaceRoot(result, outputRoot, "OUT");
		result = ReplaceRoot(result, Normalise(Path.GetTempPath()), "TMP");
		return result;
	}

	private static bool IsWithin(string path, string root) =>
		path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith(root, StringComparison.OrdinalIgnoreCase);

	private static string FormatRooted(string path, string root, string label) =>
		path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
			? $"«{label}»"
			: $"«{label}»\\{path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}";

	private static string ReplaceRoot(string text, string root, string label) =>
		text.Replace(root, $"«{label}»\\", StringComparison.OrdinalIgnoreCase);

	private static string Normalise(string path) =>
		path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
}
