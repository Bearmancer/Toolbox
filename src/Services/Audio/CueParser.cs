using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class CueParser
{
	private static readonly Regex QuotedValue = new(@"^""(.+)""$", RegexOptions.Compiled);
	private static readonly Regex TimeFormat = new(@"^(\d+):(\d+):(\d+)$", RegexOptions.Compiled);

	public ErrorOr<CueSheet> Parse(string cueFilePath)
	{
		if (!File.Exists(cueFilePath))
			return Error.NotFound("Cue.FileNotFound", $"CUE file not found: {cueFilePath}");

		var raw = File.ReadAllBytes(cueFilePath);
		var content = DetectEncoding(raw);

		string? albumTitle = null;
		string? albumArtist = null;
		string? genre = null;
		string? date = null;
		string? sourceFile = null;
		List<CueTrack> tracks = [];
		CueTrack? current = null;

		foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var trimmed = line.Trim();
			if (trimmed.Length == 0)
				continue;

			var directive = trimmed.Split(' ', 2)[0];

			switch (directive.ToUpperInvariant())
			{
				case "TITLE":
					var titleVal = Unquote(trimmed[6..]);
					if (current is null)
						albumTitle = titleVal;
					else
						current = current with { Title = titleVal };
					break;

				case "PERFORMER":
					var performerVal = Unquote(trimmed[10..]);
					if (current is null)
						albumArtist = performerVal;
					else
						current = current with { Performer = performerVal };
					break;

				case "GENRE":
					genre = Unquote(trimmed[6..]);
					break;

				case "DATE":
					date = Unquote(trimmed[5..]);
					break;

				case "FILE":
					var fileRest = trimmed[5..];
					var lastSpace = fileRest.LastIndexOf(' ');
					if (lastSpace > 0)
						sourceFile = Unquote(fileRest[..lastSpace]);
					break;

				case "TRACK":
					if (current is not null)
						tracks.Add(current);

					var trackParts = trimmed[6..].Split(' ', 2);
					if (trackParts.Length < 2 || !int.TryParse(trackParts[0], out var num))
						return Errors.Audio.InvalidCueFormat(
							cueFilePath,
							$"Bad TRACK line: {trimmed}"
						);

					current = new CueTrack(num, "", null, null, TimeSpan.Zero, null);
					break;

				case "ISRC":
					if (current is not null)
						current = current with { Isrc = Unquote(trimmed[5..]) };
					break;

				case "INDEX":
					if (current is null)
						break;

					var indexParts = trimmed[6..].Split(' ', 2);
					if (indexParts.Length < 2)
						break;

					if (!int.TryParse(indexParts[0], out var indexNumber) || indexNumber != 1)
						break;

					var time = ParseCueTime(indexParts[1]);
					if (time.IsError)
						return time.Errors;

					current = current with { StartTime = time.Value };
					break;
			}
		}

		if (current is not null)
			tracks.Add(current);

		if (sourceFile is null)
			return Errors.Audio.InvalidCueFormat(cueFilePath, "No FILE directive found");
		if (tracks.Count == 0)
			return Errors.Audio.InvalidCueFormat(cueFilePath, "No TRACK entries found");

		for (var i = 0; i < tracks.Count - 1; i++)
		{
			var t = tracks[i];
			tracks[i] = t with { Duration = tracks[i + 1].StartTime - t.StartTime };
		}

		return new CueSheet(sourceFile, albumTitle, albumArtist, genre, date, tracks);
	}

	private static string DetectEncoding(byte[] raw)
	{
		if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
			return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
		if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
			return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
		if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
			return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);

		if (IsValidUtf8(raw))
			return Encoding.UTF8.GetString(raw);

		return Encoding.GetEncoding(1252).GetString(raw);
	}

	private static bool IsValidUtf8(byte[] bytes)
	{
		var i = 0;
		while (i < bytes.Length)
		{
			if (bytes[i] <= 0x7F)
			{
				i++;
			}
			else if (
				bytes[i] >= 0xC2
				&& bytes[i] <= 0xDF
				&& i + 1 < bytes.Length
				&& bytes[i + 1] >= 0x80
				&& bytes[i + 1] <= 0xBF
			)
			{
				i += 2;
			}
			else if (
				bytes[i] >= 0xE0
				&& bytes[i] <= 0xEF
				&& i + 2 < bytes.Length
				&& bytes[i + 1] >= 0x80
				&& bytes[i + 1] <= 0xBF
				&& bytes[i + 2] >= 0x80
				&& bytes[i + 2] <= 0xBF
			)
			{
				i += 3;
			}
			else if (
				bytes[i] >= 0xF0
				&& bytes[i] <= 0xF4
				&& i + 3 < bytes.Length
				&& bytes[i + 1] >= 0x80
				&& bytes[i + 1] <= 0xBF
				&& bytes[i + 2] >= 0x80
				&& bytes[i + 2] <= 0xBF
				&& bytes[i + 3] >= 0x80
				&& bytes[i + 3] <= 0xBF
			)
			{
				i += 4;
			}
			else
			{
				return false;
			}
		}
		return true;
	}

	private static string Unquote(string value)
	{
		var m = QuotedValue.Match(value.Trim());
		return m.Success ? m.Groups[1].Value : value.Trim();
	}

	private static ErrorOr<TimeSpan> ParseCueTime(string time)
	{
		var m = TimeFormat.Match(time.Trim());
		if (!m.Success)
			return Error.Validation("Cue.BadTime", $"Invalid time format: {time}");

		var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
		var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
		var frames = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

		var totalSeconds = minutes * 60 + seconds + frames / 75.0;
		return TimeSpan.FromSeconds(totalSeconds);
	}
}
