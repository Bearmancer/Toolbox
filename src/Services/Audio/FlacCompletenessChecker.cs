using System.Text.RegularExpressions;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class FlacCompletenessChecker(SoxService sox)
{
	private static readonly Regex TrackNumberPattern = new(
		@"^(\d{1,2})\.\s",
		RegexOptions.Compiled
	);

	public sealed record DurationCheckResult(
		bool IsComplete,
		int TrackNumberCount,
		int PrimaryFlacCount,
		string DffDir
	);

	public async Task<DurationCheckResult> CheckTrackDurationsAsync(
		IReadOnlyList<CueTrack> cueTracks,
		Dictionary<int, string> primaryFlacs,
		string dffDir,
		int trackNumberCount,
		int primaryFlacCount,
		CancellationToken ct
	)
	{
		foreach (CueTrack track in cueTracks)
		{
			if (!primaryFlacs.TryGetValue(track.TrackNumber, out var flacPath))
				continue;

			ErrorOr<TimeSpan> durationResult = await sox.GetDurationAsync(flacPath, ct);
			if (durationResult.IsError)
			{
				Telemetry.Warn(
					"Pipeline.DurationCheckFailed dir={Dir} file={File} error={Error}",
					Path.GetFileName(dffDir),
					Path.GetFileName(flacPath),
					durationResult.Errors[0].Description
				);
				return new DurationCheckResult(false, trackNumberCount, primaryFlacCount, dffDir);
			}

			if (track.Duration is { } expectedDur)
			{
				var diff = Math.Abs((durationResult.Value - expectedDur).TotalSeconds);
				if (diff > 2.0)
				{
					Telemetry.Info(
						"Pipeline.DurationMismatch dir={Dir} track={Track} expected={Expected:F1}s actual={Actual:F1}s",
						Path.GetFileName(dffDir),
						track.TrackNumber,
						expectedDur.TotalSeconds,
						durationResult.Value.TotalSeconds
					);
					return new DurationCheckResult(
						false,
						trackNumberCount,
						primaryFlacCount,
						dffDir
					);
				}
			}
			else if (track == cueTracks[^1])
			{
				if (durationResult.Value.TotalSeconds <= 0)
				{
					Telemetry.Warn(
						"Pipeline.LastTrackTooShort dir={Dir} duration={Duration:F1}s",
						Path.GetFileName(dffDir),
						durationResult.Value.TotalSeconds
					);
					return new DurationCheckResult(
						false,
						trackNumberCount,
						primaryFlacCount,
						dffDir
					);
				}
				if (durationResult.Value.TotalSeconds < 30.0)
				{
					Telemetry.Warn(
						"Pipeline.LastTrackShort dir={Dir} duration={Duration:F1}s",
						Path.GetFileName(dffDir),
						durationResult.Value.TotalSeconds
					);
				}
			}
		}

		return new DurationCheckResult(true, trackNumberCount, primaryFlacCount, dffDir);
	}

	internal static Dictionary<int, string> GetFlacsByTrackNumber(string dir)
	{
		Dictionary<int, string> result = [];
		if (!Directory.Exists(dir))
			return result;

		foreach (var flac in Directory.GetFiles(dir, "*.flac", SearchOption.TopDirectoryOnly))
		{
			var name = Path.GetFileName(flac);
			Match match = TrackNumberPattern.Match(name);
			if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
				result[num] = flac;
		}
		return result;
	}

	internal static string FindDffDir(string channelDir, string discName)
	{
		var inner = Path.Combine(channelDir, discName);
		if (Directory.Exists(inner))
			return inner;

		if (Directory.Exists(channelDir))
		{
			var dffFiles = Directory.GetFiles(channelDir, "*.dff", SearchOption.AllDirectories);
			if (dffFiles.Length > 0)
			{
				var dir = Path.GetDirectoryName(dffFiles[0]);
				if (dir is not null)
					return dir;
			}
		}

		return inner;
	}
}
