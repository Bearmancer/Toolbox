namespace Services.Audio;

public sealed class SacdProbeService(SaraconService saracon)
{
	private readonly SacdProbeRunner Runner = new(saracon);

	public Task<ProbeResult> RunProbeAsync(CancellationToken ct = default) =>
		Runner.RunAllAsync(ct);
}

public sealed record ProbeResult(
	bool Passed,
	string JournalPath,
	IReadOnlyList<string> VariantResults
);
