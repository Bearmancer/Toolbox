using System.Text.RegularExpressions;

namespace Services.Google;

public static partial class FileNameSanitizer
{
    public static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "untitled";

        var sanitized = InvalidChars().Replace(title, "_");
        return sanitized.Length > 200 ? sanitized[..200] : sanitized;
    }

    [GeneratedRegex(@"[<>:""/\\|?*]")]
    private static partial Regex InvalidChars();
}
