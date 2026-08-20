using Core;
using ErrorOr;

namespace Services.Pristine;

public static class PristineVerification
{
	public static async Task<bool> VerifyAndKeepAsync(
		PristineAudioVerifier verifier,
		string dest,
		string code,
		int track,
		CancellationToken ct
	)
	{
		ErrorOr<PristineProbeResult> probeOr = await verifier.VerifyAsync(dest, code, track, ct);
		if (probeOr.IsError)
		{
			Telemetry.Error(
				"Pristine.Verify.HardFail code={Code} track={Track} dest={Dest} err={Err}",
				code,
				track,
				Path.GetFileName(dest),
				probeOr.FirstError.Description
			);
			DeleteRejectedFile(dest, code, track, probeOr.FirstError.Code);
			return false;
		}

		PristineProbeResult probe = probeOr.Value;
		Telemetry.Debug(
			"Pristine.Verify.Checked code={Code} track={Track} is24={Is24} codec={Codec} dest={Dest} note={Note}",
			code,
			track,
			probe.Is24BitFlac,
			probe.Codec,
			Path.GetFileName(dest),
			probe.Note
		);
		if (probe.Is24BitFlac is false)
		{
			DeleteRejectedFile(dest, code, track, probe.Note);
			return false;
		}

		return true;
	}

	public static void DeleteRejectedFile(string path, string code, int track, string reason)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
			Telemetry.Debug(
				"Pristine.Verify.Rejected code={Code} track={Track} dest={Dest} reason={Reason} — file deleted, track not counted as downloaded",
				code,
				track,
				Path.GetFileName(path),
				reason
			);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn(
				"Pristine.Verify.RejectedDeleteFailed code={Code} track={Track} dest={Dest} reason={Reason}: {Error}",
				code,
				track,
				Path.GetFileName(path),
				reason,
				ex.Message
			);
		}
	}
}
