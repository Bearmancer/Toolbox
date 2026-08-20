using System.Text;

namespace Core;

public static class Text
{
	public static string SanitizeFileName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		StringBuilder sanitized = name.Aggregate(
			new StringBuilder(),
			(sb, c) =>
			{
				_ = sb.Append(invalid.Contains(c) ? '_' : c);
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
}
