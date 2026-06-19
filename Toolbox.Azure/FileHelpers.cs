namespace Toolbox.Azure;

public static class FileHelpers
{
    public static string ResolvePath(string input)
    {
        if (Path.IsPathRooted(input) && File.Exists(input))
            return input;
        var combined = Path.Combine(Constants.Resources, input);
        if (File.Exists(combined))
            return combined;
        throw new FileNotFoundException($"File not found: {input}");
    }

    public static byte[] ReadChecked(string path, long maxBytes, string serviceName)
    {
        var info = new FileInfo(path);
        if (info.Length > maxBytes)
            throw new ArgumentOutOfRangeException(
                nameof(path),
                $"Payload too large: {info.Length} bytes exceeds {maxBytes} byte limit for {serviceName}"
            );
        return File.ReadAllBytes(path);
    }
}