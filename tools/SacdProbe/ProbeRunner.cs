using System.Diagnostics;
using Services.Audio;

namespace SacdProbe;

internal static class ProbeRunner
{
	private const string RepoRoot = @"C:\Users\Lance\Dev\Toolbox";
	private static readonly string JournalPath = Path.Combine(RepoRoot, ".superpowers", "audit", "sacd-probe-journal.md");
	private static readonly string OutputRoot = @"C:\Temp\saracon-probe\out";

	private enum FailureSignature
	{
		None,
		RegistryOleInit,
		CharsetEncoding,
		Truncation,
		ZeroBytes,
		Other,
	}

	private sealed record Variant(string Name, bool Stripped, bool Visible, FailureSignature Expected);
	private sealed record Result(string Row, FailureSignature Signature);

	public static int RunAll(int acp)
	{
		if (!RealDffFixture.Exists())
		{
			Console.Error.WriteLine($"PRECONDITION FAILED: real DFF not found at {RealDffFixture.Path}");
			return 3;
		}

		var expected = RealDffFixture.ExpectedPcmBytes();
		if (expected <= 0)
		{
			Console.Error.WriteLine("PRECONDITION FAILED: DSD chunk size could not produce expected PCM size");
			return 3;
		}

		Directory.CreateDirectory(OutputRoot);
		var canary = Run(new Variant("raw/headless-canary", false, false, FailureSignature.None), expected);
		Append(canary.Row);
		Console.WriteLine(canary.Row);
		if (canary.Signature == FailureSignature.RegistryOleInit)
		{
			Console.Error.WriteLine("PRECONDITION FAILED: Saracon registry/OLE initialization blocked probe");
			return 2;
		}

		var visibleExpected = acp == 65001 ? FailureSignature.CharsetEncoding : FailureSignature.None;
		var variants = new[]
		{
			new Variant("raw/headless", false, false, FailureSignature.None),
			new Variant("stripped/headless", true, false, FailureSignature.None),
			new Variant("raw/visible", false, true, visibleExpected),
			new Variant("stripped/visible", true, true, visibleExpected),
		};

		var failed = false;
		foreach (var variant in variants)
		{
			var result = Run(variant, expected);
			Append(result.Row);
			Console.WriteLine(result.Row);
			failed |= result.Row.Contains("FAIL-unexpected", StringComparison.Ordinal);
		}

		return failed ? 1 : 0;
	}

	private static Result Run(Variant variant, long expected)
	{
		var timer = Stopwatch.StartNew();
		var input = RealDffFixture.Path;
		try
		{
			if (variant.Stripped)
			{
				var stripped = DffMetadataStripper.StripId3TagsAsync(input, OutputRoot).GetAwaiter().GetResult();
				if (stripped.IsError)
					return Make(variant, timer, -1, 0, Classify(stripped.Errors[0].Description), stripped.Errors[0].Description);
				input = stripped.Value;
			}

			return variant.Visible
				? RunVisible(variant, input, timer, expected)
				: RunHeadless(variant, input, timer, expected);
		}
		catch (Exception ex)
		{
			return Make(variant, timer, -2, 0, FailureSignature.Other, ex.Message);
		}
	}

	private static Result RunHeadless(Variant variant, string input, Stopwatch timer, long expected)
	{
		var logPath = Path.Combine(OutputRoot, "saracon-run.log");
		Action<string> onLine = line => {
			Console.WriteLine(line);
			File.AppendAllText(logPath, line + Environment.NewLine);
		};

		var result = new SaraconService(new ProcessRunner(), "saracon")
			.ConvertDsdToPcmAsync(input, OutputRoot, 88200, 24, 0.0, onLine)
			.GetAwaiter().GetResult();
		timer.Stop();
		if (result.IsError)
			return Make(variant, timer, -1, 0, Classify(result.Errors[0].Description), result.Errors[0].Description);

		var bytes = File.Exists(result.Value) ? new FileInfo(result.Value).Length : 0;
		return Make(variant, timer, 0, bytes, SizeSignature(bytes, expected), Path.GetFileName(result.Value));
	}

	private static Result RunVisible(Variant variant, string input, Stopwatch timer, long expected)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "saracon",
			UseShellExecute = false,
			CreateNoWindow = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = OutputRoot,
		};
		foreach (var argument in new[] { "-c", "d2p", "-r", "88200", "-f", "wav", "-n", "24bit", "-d", "tpdf", "-g", "0.00", "-T", "-V", "all", "-t", OutputRoot, input })
			startInfo.ArgumentList.Add(argument);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start saracon");
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		process.WaitForExit();
		timer.Stop();
		var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
		var signature = process.ExitCode == 0 ? Classify(output) : Classify(output);
		if (process.ExitCode != 0 || signature != FailureSignature.None)
			return Make(variant, timer, process.ExitCode, 0, signature, output[..Math.Min(output.Length, 500)]);

		var file = Directory.GetFiles(OutputRoot, "*.wav")
			.OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
			.FirstOrDefault();
		var bytes = file is null ? 0 : new FileInfo(file).Length;
		return Make(variant, timer, process.ExitCode, bytes, SizeSignature(bytes, expected), file ?? "no output");
	}

	private static FailureSignature SizeSignature(long bytes, long expected) =>
		bytes == 0 ? FailureSignature.ZeroBytes : bytes < expected / 2 ? FailureSignature.Truncation : FailureSignature.None;

	private static FailureSignature Classify(string text) =>
		text.Contains("Can't open registry key", StringComparison.OrdinalIgnoreCase)
		|| text.Contains("Cannot initialize OLE", StringComparison.OrdinalIgnoreCase)
		|| text.Contains("wxIdleWakeUpModule", StringComparison.OrdinalIgnoreCase)
			? FailureSignature.RegistryOleInit
		: text.Contains("Unknown encoding", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("Cannot convert from the charset", StringComparison.OrdinalIgnoreCase)
			? FailureSignature.CharsetEncoding
			: FailureSignature.Other;

	private static Result Make(Variant variant, Stopwatch timer, int exitCode, long bytes, FailureSignature actual, string note)
	{
		timer.Stop();
		var verdict = actual == FailureSignature.None
			? "PASS"
			: actual == variant.Expected
				? $"FAIL-expected({actual})"
				: $"FAIL-unexpected({actual})";
		var row = $"| {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {variant.Name} | {exitCode} | {timer.ElapsedMilliseconds}ms | {bytes} | {verdict} | {Escape(note)} |";
		return new Result(row, actual);
	}

	private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

	private static void Append(string row)
	{
		var content = File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : "# SACD Probe Journal\n\n## Runs\n\n| timestamp | variant | exit | elapsed | out-bytes | verdict | note |\n|---|---|---:|---:|---:|---|---|\n\n## Findings\n";
		if (!content.Contains(row, StringComparison.Ordinal))
			File.WriteAllText(JournalPath, content.Insert(content.IndexOf("\n## Findings", StringComparison.Ordinal), row + "\n"));
	}
}
