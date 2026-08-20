using System.Text.RegularExpressions;

namespace Services.Pristine;

public static class PristineText
{
	private static readonly Regex WinIllegalChars = new(
		@"[<>:""/\\|?*\x00-\x1f]",
		RegexOptions.Compiled
	);

	private static readonly Regex TrailingDotsSpaces = new(@"[\s.]+$", RegexOptions.Compiled);

	private static readonly Regex AudioUrlRe = new(
		@"\.(flac|mp3|wav|aac|ogg)(\?|$)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	private static readonly Regex TimestampPrefixRe = new(
		@"^\s*\d{1,2}:\d{2}(?::\d{2})?\s*[-\u2013\u2014:.)]*\s*",
		RegexOptions.Compiled
	);

	private static readonly Regex MovementPrefixRe = new(
		@"^\s*(?:(?<ord>\d{1,2})(?:st|nd|rd|th)?\s*(?:m(?:ovement|ov\.?|vt|vmt)\.?)?|(?<roman>[ivxlcdm]{1,6})\s*(?:m(?:ovement|ov\.?|vt|vmt)\.?)?|(?<word>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\s+movement)\s*[-\u2013\u2014:.)]*\s*",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	private static readonly Dictionary<string, int> WordToInt = new(
		StringComparer.OrdinalIgnoreCase
	)
	{
		["first"] = 1,
		["second"] = 2,
		["third"] = 3,
		["fourth"] = 4,
		["fifth"] = 5,
		["sixth"] = 6,
		["seventh"] = 7,
		["eighth"] = 8,
		["ninth"] = 9,
		["tenth"] = 10,
	};

	private static readonly string[] RomanNumerals =
	[
		"",
		"I",
		"II",
		"III",
		"IV",
		"V",
		"VI",
		"VII",
		"VIII",
		"IX",
		"X",
		"XI",
		"XII",
		"XIII",
		"XIV",
		"XV",
	];

	public static string Roman(int n) =>
		n > 0 && n < RomanNumerals.Length ? RomanNumerals[n] : n.ToString();

	public static string SanitizePathComponent(string name)
	{
		name = WinIllegalChars.Replace(name, "-");
		name = TrailingDotsSpaces.Replace(name, "");
		var trimmed = name.Trim();
		return string.IsNullOrEmpty(trimmed) ? "Unknown" : trimmed;
	}

	public static bool IsAudioUrl(string url) => AudioUrlRe.IsMatch(url);

	public static string ClampFileName(
		string directory,
		string stem,
		string extension,
		int maxPathLength = 240
	)
	{
		var budget = maxPathLength - directory.Length - 1 - extension.Length;
		if (budget < 1)
			budget = 1;
		return stem.Length <= budget ? stem : stem[..budget].TrimEnd();
	}

	public static string FormatAlbumFolderName(string code, string rawTitle)
	{
		var title = rawTitle;
		var suffix = $" - {code}";
		if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			title = title[..^suffix.Length];
		return SanitizePathComponent($"{code} - {title}");
	}

	public static string NormalizeTrackTitle(string title)
	{
		var text = TimestampPrefixRe.Replace(title, "").Trim();
		Match m = MovementPrefixRe.Match(text);
		if (!m.Success)
		{
			return text;
		}

		var remainder = text[m.Length..].Trim();
		var num = 0;
		if (m.Groups["ord"].Success)
		{
			num = int.Parse(m.Groups["ord"].Value);
		}
		else if (m.Groups["roman"].Success)
		{
			var roman = m.Groups["roman"].Value.ToUpperInvariant();
			Dictionary<char, int> map = new()
			{
				['I'] = 1,
				['V'] = 5,
				['X'] = 10,
				['L'] = 50,
				['C'] = 100,
			};
			var val = 0;
			var prev = 0;
			for (var i = roman.Length - 1; i >= 0; i--)
			{
				var curr = map.GetValueOrDefault(roman[i], 0);
				if (curr >= prev)
				{
					val += curr;
				}
				else
				{
					val -= curr;
				}

				prev = curr;
			}

			num = val;
		}
		else if (m.Groups["word"].Success)
		{
			WordToInt.TryGetValue(m.Groups["word"].Value, out num);
		}

		var prefix = $"{Roman(num)}.";
		return string.IsNullOrEmpty(remainder) ? prefix : $"{prefix} {remainder}";
	}
}
