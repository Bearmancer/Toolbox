namespace Core;

public static class StringExtensions
{
    public static bool Equals(this string? a, string? b)
        => string.Equals(a, b, StringComparison.Ordinal);

    public static bool NotEquals(this string? a, string? b)
        => !string.Equals(a, b, StringComparison.Ordinal);

    public static bool Contains(this string? source, string value)
        => source?.Contains(value, StringComparison.Ordinal) ?? false;

    public static bool NotContains(this string? source, string value)
        => !(source?.Contains(value, StringComparison.Ordinal) ?? false);

    public static bool StartsWith(this string? source, string value)
        => source?.StartsWith(value, StringComparison.Ordinal) ?? false;

    public static bool EndsWith(this string? source, string value)
        => source?.EndsWith(value, StringComparison.Ordinal) ?? false;

    public static bool EqualsIgnore(this string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool NotEqualsIgnore(this string? a, string? b)
        => !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool ContainsIgnore(this string? source, string value)
        => source?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;

    public static bool NotContainsIgnore(this string? source, string value)
        => !(source?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);

    public static bool StartsWithIgnore(this string? source, string value)
        => source?.StartsWith(value, StringComparison.OrdinalIgnoreCase) ?? false;

    public static bool EndsWithIgnore(this string? source, string value)
        => source?.EndsWith(value, StringComparison.OrdinalIgnoreCase) ?? false;
}
