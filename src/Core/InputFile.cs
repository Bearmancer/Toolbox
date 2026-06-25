namespace Core;

public static class InputFile
{
    private static readonly string ResourcesDir = Path.GetFullPath(
        "resources",
        Directory.GetCurrentDirectory()
    );

    public static string ResolvePath(string input)
    {
        if (Path.IsPathRooted(input))
            return File.Exists(input)
                ? input
                : throw new FileNotFoundException($"File not found: {input}");

        var combined = Path.GetFullPath(input, ResourcesDir);
        return File.Exists(combined)
            ? combined
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
