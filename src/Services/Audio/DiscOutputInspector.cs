using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class DiscOutputInspector(
	CueParser cueParser,
	DsdConvertService convertService,
	FlacCompletenessChecker flacChecker
)
{
	public sealed record DiscAssessment(
		DiscState State,
		int CueTrackCount,
		int PrimaryFlacCount,
		string DffDir
	);

	public async Task<DiscAssessment> EvaluateDiscAsync(
		string channelDir,
		string discName,
		CancellationToken ct
	)
	{
		var dffDir = FlacCompletenessChecker.FindDffDir(channelDir, discName);

		var cueFiles = Directory.Exists(dffDir) ? Directory.GetFiles(dffDir, "*.cue") : [];
		var cueFile = cueFiles.Length > 0 ? cueFiles[0] : null;

		CueSheet? cue = null;
		if (cueFile is not null)
		{
			ErrorOr<CueSheet> cueResult = cueParser.Parse(cueFile);
			if (cueResult.IsError)
				Telemetry.Warn(
					"Pipeline.CueParseFailed dir={Dir} error={Error}",
					Path.GetFileName(dffDir),
					cueResult.Errors[0].Description
				);
			else
				cue = cueResult.Value;
		}

		var dffFiles = Directory.Exists(dffDir)
			? Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories)
			: [];
		var hasValidDff = false;
		if (dffFiles.Length > 0)
		{
			Array.Sort(
				dffFiles,
				(a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length)
			);
			ErrorOr<DsdProbeResult> probe = await convertService.ProbeDsdAsync(dffFiles[0], ct);
			if (probe.IsSuccess)
				hasValidDff = true;
		}

		Dictionary<int, string> primaryFlacs = FlacCompletenessChecker.GetFlacsByTrackNumber(
			dffDir
		);

		if (cue is null)
		{
			Telemetry.Warn(
				"Pipeline.NoCue dir={Dir} flacs={Flacs}",
				Path.GetFileName(dffDir),
				primaryFlacs.Count
			);
			return new DiscAssessment(
				hasValidDff ? DiscState.InvalidArtifacts : DiscState.NeedsExtraction,
				0,
				primaryFlacs.Count,
				dffDir
			);
		}

		List<int> allTrackNumbers = [.. cue.Tracks.Select(t => t.TrackNumber)];
		var primaryFlacFiles = Directory.Exists(dffDir) ? Directory.GetFiles(dffDir, "*.flac") : [];
		var hasAllTracks =
			primaryFlacFiles.Length == allTrackNumbers.Count
			&& primaryFlacs.Count == allTrackNumbers.Count
			&& allTrackNumbers.All(n => primaryFlacs.ContainsKey(n));

		if (!hasAllTracks)
		{
			Telemetry.Debug(
				"Pipeline.Incomplete dir={Dir} cue={CueCount} flacs={FlacCount}",
				Path.GetFileName(dffDir),
				allTrackNumbers.Count,
				primaryFlacs.Count
			);
			return new DiscAssessment(
				hasValidDff ? DiscState.NeedsPrimaryConversion : DiscState.NeedsExtraction,
				allTrackNumbers.Count,
				primaryFlacs.Count,
				dffDir
			);
		}

		FlacCompletenessChecker.DurationCheckResult durationCheck =
			await flacChecker.CheckTrackDurationsAsync(
				cue.Tracks,
				primaryFlacs,
				dffDir,
				allTrackNumbers.Count,
				primaryFlacs.Count,
				ct
			);

		if (!durationCheck.IsComplete)
			return new DiscAssessment(
				hasValidDff ? DiscState.NeedsPrimaryConversion : DiscState.NeedsExtraction,
				durationCheck.TrackNumberCount,
				durationCheck.PrimaryFlacCount,
				durationCheck.DffDir
			);

		var totalSeconds = cue.Tracks.Sum(t => t.Duration?.TotalSeconds ?? 0);
		var hours = (int)(totalSeconds / 3600);
		var minutes = (int)((totalSeconds % 3600) / 60);
		var seconds = (int)(totalSeconds % 60);
		Telemetry.Info(
			"Skipping {Disc} — {Count}/{Total} FLACs complete ({Duration})",
			Path.GetFileName(dffDir),
			primaryFlacs.Count,
			allTrackNumbers.Count,
			$"{hours}:{minutes:D2}:{seconds:D2}"
		);

		return new DiscAssessment(
			DiscState.Complete,
			allTrackNumbers.Count,
			primaryFlacs.Count,
			dffDir
		);
	}
}
