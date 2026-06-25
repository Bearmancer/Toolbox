namespace Core;

public static class StringExtensions
{
    extension(string? a)
    {
        public bool Equals(string? b) => string.Equals(a, b, StringComparison.Ordinal);
    }

    extension(string? source)
    {
        public bool Contains(string value) =>
            source?.Contains(value, StringComparison.Ordinal) ?? false;

        public bool StartsWith(string value) =>
            source?.StartsWith(value, StringComparison.Ordinal) ?? false;
    }

    extension(string? a)
    {
        public bool EqualsIgnore(string? b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
