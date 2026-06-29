using System.Text;

namespace Core;

public static class Text
{
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = name.Aggregate(
            new StringBuilder(),
            (sb, c) =>
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
                return sb;
            }
        );
        return sanitized.ToString().Trim();
    }

    extension(string? a)
    {
        public bool IsEqualTo(string? b) => string.Equals(a, b, StringComparison.Ordinal);

        public bool IsEqualToIgnore(string? b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    extension(string? source)
    {
        public bool Has(string value) => source?.Contains(value, StringComparison.Ordinal) ?? false;

        public bool StartsWith(string value) =>
            source?.StartsWith(value, StringComparison.Ordinal) ?? false;
    }
}
