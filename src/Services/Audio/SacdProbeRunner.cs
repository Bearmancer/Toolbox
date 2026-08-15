using System.Diagnostics;
using Core;
using ErrorOr;

namespace Services.Audio;

internal sealed class SacdProbeRunner(SaraconService saracon)
{
	private static readonly string JournalPath = Path.Combine(
		PathResolver.RepoRoot,
		"docs",
		"superpowers",
		"audits",
		"sacd-probe-journal.md"
	);
	private static readonly string OutRoot = @"C:\Temp\saracon-probe\out";

	private enum FailureSignature
	{
		None,
		RegistryOleInit,
		CharsetEncoding,
		Truncation,
		ZeroBytes,
		Other,
	}

	private record ProbeVariant(
		string Label,
		bool Stripped,
		bool Headless,
		FailureSignature DeclaredExpected
	);

	private static readonly ProbeVariant[] Matrix =
	[
		new("raw/headless", false, true, FailureSignature.None),
		new("stripped/headless", true, true, FailureSignature.None),
		new("raw/visible", false, false, FailureSignature.CharsetEncoding),
		new("stripped/visible", true, false, FailureSignature.CharsetEncoding),
	];

	private record RunResult(string Row, FailureSignature Signature);

	public async Task<ProbeResult> RunAllAsync(CancellationToken ct)
	{
		Directory.CreateDirectory(OutRoot);

		if (!RealDffFixture.Exists())
		{
			await Console.Error.WriteLineAsync(
				$"PRECONDITION FAILED: real DFF not found at {RealDffFixture.Path}",
				ct
			);
			return new ProbeResult(
				false,
				JournalPath,
				[$"PRECONDITION FAILED: DFF not found at {RealDffFixture.Path}"]
			);
		}

		var expectedBytes = RealDffFixture.ExpectedPcmBytes();
		Console.WriteLine($"Real DFF  : {RealDffFixture.Path}");
		Console.WriteLine($"Expected  : {expectedBytes:N0} PCM bytes");

		Console.WriteLine("\n--- Precondition canary (raw/headless) ---");
		RunResult canary = await RunVariantAsync(Matrix[0], expectedBytes, ct);
		AppendJournal(canary.Row);
		Console.WriteLine(canary.Row);

		if (canary.Signature == FailureSignature.RegistryOleInit)
		{
			await Console.Error.WriteLineAsync(
				"\nPRECONDITION FAILED: registry/OLE init error — v2 spec §4.\n"
					+ "Fix A: grant HKCU\\Software\\Weiss Engineering FullControl to the executing SID.\n"
					+ "Fix B: confirm agent session vs. interactive session mismatch via:\n"
					+ "       [Security.Principal.WindowsIdentity]::GetCurrent().Name; query session; (Get-Process -Id $PID).SessionId",
				ct
			);
			return new ProbeResult(false, JournalPath, [canary.Row]);
		}

		Console.WriteLine("\n--- Full matrix ---");
		List<string> variantRows = [];
		var unexpectedFail = false;
		foreach (ProbeVariant variant in Matrix)
		{
			RunResult run = await RunVariantAsync(variant, expectedBytes, ct);
			AppendJournal(run.Row);
			Console.WriteLine(run.Row);
			variantRows.Add(run.Row);
			if (run.Row.Contains("FAIL-unexpected"))
				unexpectedFail = true;
		}

		Console.WriteLine(unexpectedFail ? "\nPROBE FAIL (unexpected outcome)" : "\nPROBE PASS");
		return new ProbeResult(!unexpectedFail, JournalPath, variantRows);
	}

	private async Task<RunResult> RunVariantAsync(
		ProbeVariant v,
		long expectedBytes,
		CancellationToken ct
	)
	{
		Stopwatch sw = Stopwatch.StartNew();
		var input = RealDffFixture.Path;

		try
		{
			if (v.Stripped)
			{
				ErrorOr<string> strip = await DffMetadataStripper.StripId3TagsAsync(
					input,
					OutRoot,
					ct
				);
				if (strip.IsError)
				{
					sw.Stop();
					var errText = strip.Errors[0].Description;
					FailureSignature sig = Classify(errText);
					return MakeResult(v, sw, -1, 0, sig, errText);
				}
				input = strip.Value;
			}

			if (v.Headless)
				return await RunHeadlessAsync(v, input, sw, expectedBytes, ct);
			else
				return await RunVisibleAsync(v, input, sw, expectedBytes);
		}
		catch (Exception ex)
		{
			sw.Stop();
			return new RunResult(
				$"| {Ts()} | {v.Label} | exit=-2 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | {Escape(ex.Message)} |",
				FailureSignature.Other
			);
		}
	}

	private async Task<RunResult> RunHeadlessAsync(
		ProbeVariant v,
		string input,
		Stopwatch sw,
		long expectedBytes,
		CancellationToken ct
	)
	{
		ErrorOr<string> result = await saracon.ConvertDsdToPcmAsync(
			input,
			OutRoot,
			88200,
			24,
			0.0,
			2822400,
			2,
			ct: ct
		);
		sw.Stop();

		if (result.IsError)
		{
			var errText = result.Errors[0].Description;
			return MakeResult(v, sw, -1, 0, Classify(errText), errText);
		}

		var outFile = result.Value;
		var bytes = File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
		FailureSignature sig = OutSig(bytes, expectedBytes);
		return MakeResult(
			v,
			sw,
			0,
			bytes,
			sig,
			$"{Path.GetFileName(outFile)} {bytes:N0}/{expectedBytes:N0} bytes"
		);
	}

	private static async Task<RunResult> RunVisibleAsync(
		ProbeVariant v,
		string input,
		Stopwatch sw,
		long expectedBytes
	)
	{
		var saraconPath = FindSaracon();
		if (saraconPath is null)
		{
			sw.Stop();
			return new RunResult(
				$"| {Ts()} | {v.Label} | exit=-1 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | saracon not found on PATH |",
				FailureSignature.Other
			);
		}

		var outName = Path.Combine(OutRoot, Path.GetFileNameWithoutExtension(input) + "-visible");
		ProcessStartInfo psi = new()
		{
			FileName = saraconPath,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = false,
			WorkingDirectory = OutRoot,
		};
		foreach (
			var arg in new[]
			{
				"-c",
				"d2p",
				"-r",
				"88200",
				"-f",
				"wav",
				"-n",
				"24bit",
				"-d",
				"tpdf",
				"-g",
				"0.00",
				"-T",
				"-V",
				"all",
				"-t",
				OutRoot,
				input,
			}
		)
			psi.ArgumentList.Add(arg);

		using Process proc =
			Process.Start(psi)
			?? throw new InvalidOperationException("Failed to start saracon (visible)");

		Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
		Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
		await Task.WhenAll(stdoutTask, stderrTask);
		await proc.WaitForExitAsync(cancellationToken: default);
		sw.Stop();

		var combined = await stdoutTask + await stderrTask;
		FailureSignature sig = Classify(combined);

		if (proc.ExitCode != 0 || sig != FailureSignature.None)
			return MakeResult(
				v,
				sw,
				proc.ExitCode,
				0,
				sig,
				combined[..Math.Min(200, combined.Length)]
			);

		var outFile = Directory
			.EnumerateFiles(OutRoot, "*.wav")
			.OrderByDescending(f => new FileInfo(f).LastWriteTime)
			.FirstOrDefault();
		var bytes = outFile is not null && File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
		return MakeResult(
			v,
			sw,
			proc.ExitCode,
			bytes,
			OutSig(bytes, expectedBytes),
			$"{(outFile is null ? "no output" : Path.GetFileName(outFile))} {bytes:N0}/{expectedBytes:N0} bytes"
		);
	}

	private static string? FindSaracon()
	{
		if (ProcessRunner.IsOnPath("saracon"))
			return "saracon";
		var path = Environment.GetEnvironmentVariable("PATH") ?? "";
		return path.Split(Path.PathSeparator)
			.Select(d => Path.Combine(d, "saracon.exe"))
			.FirstOrDefault(File.Exists);
	}

	private static FailureSignature Classify(string text) =>
		text switch
		{
			var s
				when s.Contains("Cannot initialize OLE", StringComparison.OrdinalIgnoreCase)
					|| s.Contains("Can't open registry key", StringComparison.OrdinalIgnoreCase)
					|| s.Contains("wxIdleWakeUpModule", StringComparison.OrdinalIgnoreCase) =>
				FailureSignature.RegistryOleInit,

			var s
				when s.Contains("Unknown encoding", StringComparison.OrdinalIgnoreCase)
					|| s.Contains(
						"Cannot convert from the charset",
						StringComparison.OrdinalIgnoreCase
					) => FailureSignature.CharsetEncoding,

			_ => FailureSignature.Other,
		};

	private static FailureSignature OutSig(long bytes, long expected) =>
		bytes == 0 ? FailureSignature.ZeroBytes
		: bytes < expected * 0.5 ? FailureSignature.Truncation
		: FailureSignature.None;

	private static RunResult MakeResult(
		ProbeVariant v,
		Stopwatch sw,
		int exit,
		long bytes,
		FailureSignature actual,
		string note
	)
	{
		string verdict;
		if (actual == FailureSignature.None)
			verdict = "PASS";
		else if (v.DeclaredExpected != FailureSignature.None && actual == v.DeclaredExpected)
			verdict = $"FAIL-expected({actual})";
		else
			verdict = $"FAIL-unexpected({actual})";

		var row =
			$"| {Ts()} | {v.Label} | exit={exit} | {sw.ElapsedMilliseconds}ms | {bytes:N0}B | {verdict} | {Escape(note)} |";
		return new RunResult(row, actual);
	}

	private static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

	private static string Escape(string s) =>
		s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

	private static void AppendJournal(string row)
	{
		var content = File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : "";
		if (content.Contains(row))
			return;

		var header =
			"| timestamp | variant | exit | elapsed | out-bytes | verdict | note |\n"
			+ "|---|---|---|---|---|---|---|\n";
		var runsIdx = content.IndexOf("## Runs (v2", StringComparison.Ordinal);
		if (runsIdx >= 0)
		{
			var afterSep = content.IndexOf(
				'\n',
				content.IndexOf("|---|", runsIdx, StringComparison.Ordinal)
			);
			content = content.Insert(afterSep + 1, row + "\n");
		}
		else
		{
			content += $"\n## Runs (v2 — real DFF, {DateTime.Now:yyyy-MM-dd})\n\n{header}{row}\n";
		}
		File.WriteAllText(JournalPath, content);
	}
}
