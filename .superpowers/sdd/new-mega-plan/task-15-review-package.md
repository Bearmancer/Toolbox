# Review package: 1fb4064..37b285a

## Commits
37b285a chore(audio): remove orphaned P2.3 SacdProbe harness

## Files changed
 .superpowers/sdd/new-mega-plan/task-15-report.md |  83 ++++++
 src/Services/Audio/AudioSetup.cs                 |   1 -
 src/Services/Audio/RealDffFixture.cs             |  50 ----
 src/Services/Audio/SacdProbeRunner.cs            | 357 -----------------------
 src/Services/Audio/SacdProbeService.cs           |  15 -
 5 files changed, 83 insertions(+), 423 deletions(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-15-report.md b/.superpowers/sdd/new-mega-plan/task-15-report.md
new file mode 100644
index 0000000..1c5164e
--- /dev/null
+++ b/.superpowers/sdd/new-mega-plan/task-15-report.md
@@ -0,0 +1,83 @@
+# Task 15 Report ΓÇö P2.3 Probe harness disposition
+
+Branch: `sacd-completion-v2` (HEAD `1fb4064` before change)
+Worktree: `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
+
+## Decision
+
+`SacdProbeService` is DI-registered but `RunProbeAsync` has no caller; `RealDffFixture` hardcodes `C:\Temp\t.dff` in shipped assembly. Remove probe harness: delete `SacdProbeService.cs`, `SacdProbeRunner.cs`, `RealDffFixture.cs`, and the registration together. `DffMetadataStripper` is retained ΓÇö it is used by `DsdConvertService` (lines 23, 33), not orphaned.
+
+## Subtask 1 ΓÇö Confirm orphaned / no public caller
+
+Command: `rg -n "SacdProbeService|SacdProbeRunner|RealDffFixture|ProbeResult" --glob "*.cs"`
+
+Raw output (pre-deletion):
+```
+src\Services\Audio\AudioSetup.cs:18: services.AddSingleton<SacdProbeService>();
+src\Services\Audio\SacdProbeService.cs:3: public sealed class SacdProbeService(SaraconService saracon)
+src\Services\Audio\SacdProbeService.cs:5: private readonly SacdProbeRunner Runner = new(saracon);
+src\Services\Audio\SacdProbeService.cs:7: public Task<ProbeResult> RunProbeAsync(CancellationToken ct = default)
+src\Services\Audio\SacdProbeRunner.cs:7: internal sealed class SacdProbeRunner(SaraconService saracon)
+src\Services\Audio\RealDffFixture.cs:5: internal static class RealDffFixture
+```
+
+`SacdProbeService` referenced only by its own file and the DI registration. `SacdProbeRunner`/`RealDffFixture`/`ProbeResult` referenced only within the three files. No public caller of `RunProbeAsync` exists.
+
+Result: **PASS**
+
+## Subtask 2 ΓÇö Delete files + registration
+
+Command: `git rm src/Services/Audio/SacdProbeService.cs src/Services/Audio/SacdProbeRunner.cs src/Services/Audio/RealDffFixture.cs`
+
+Raw output:
+```
+rm 'src/Services/Audio/RealDffFixture.cs'
+rm 'src/Services/Audio/SacdProbeRunner.cs'
+rm 'src/Services/Audio/SacdProbeService.cs'
+```
+
+Edit `AudioSetup.cs`: removed `services.AddSingleton<SacdProbeService>();` (line 18).
+
+Diff:
+```diff
+ 			services.AddSingleton<DiskSpaceChecker>();
+-			services.AddSingleton<SacdProbeService>();
+ 			services.AddSingleton(sp => new SacdExtractService(
+```
+
+Result: **PASS**
+
+## Subtask 3 ΓÇö Reference search after deletion
+
+Command: `rg -n "SacdProbeService|SacdProbeRunner|RealDffFixture|ProbeResult" --glob "*.cs"`
+
+Raw output (post-deletion): only unrelated `DsdProbeResult` / `SacdProbeResult` matches remain (used by `DsdConvertService`, `SacdExtractService`, `DiscOutputInspector`, `PipelineOrchestrator`, `DsdConvertCommand`). No match for the deleted types.
+
+Result: **PASS**
+
+## Subtask 4 ΓÇö Clean build
+
+Command: `dotnet build Toolbox.slnx --no-restore --no-incremental`
+
+Raw output (tail):
+```
+  Audio -> ...\Audio\debug\Audio.dll
+  CLI -> ...\CLI\debug\CLI.dll
+  App -> ...\App\debug\App.dll
+
+Build succeeded.
+    0 Warning(s)
+    0 Error(s)
+
+Time Elapsed 00:00:10.97
+```
+
+Result: **PASS**
+
+## Acceptance
+
+- Three files and registration gone: **PASS**
+- Clean build (0 warnings, 0 errors): **PASS**
+- No unreferenced public member remains: **PASS** (`SacdProbeService`/`ProbeResult` removed; `DffMetadataStripper` retained and referenced by `DsdConvertService`)
+
+No runtime blocker required ΓÇö static removal and clean build observed.
diff --git a/src/Services/Audio/AudioSetup.cs b/src/Services/Audio/AudioSetup.cs
index 3afa8f4..34d657f 100644
--- a/src/Services/Audio/AudioSetup.cs
+++ b/src/Services/Audio/AudioSetup.cs
@@ -8,21 +8,20 @@ public static class AudioSetup
 	{
 		public void AddAudioServices()
 		{
 			ValidateBinaryOnPath("saracon");
 			ValidateBinaryOnPath("sox");
 			ValidateBinaryOnPath("sacd_extract");
 
 			services.AddSingleton<ProcessRunner>();
 			services.AddSingleton<PathValidator>();
 			services.AddSingleton<DiskSpaceChecker>();
-			services.AddSingleton<SacdProbeService>();
 			services.AddSingleton(sp => new SacdExtractService(
 				sp.GetRequiredService<ProcessRunner>(),
 				"sacd_extract"
 			));
 			services.AddSingleton(sp => new SaraconService(
 				sp.GetRequiredService<ProcessRunner>(),
 				"saracon"
 			));
 			services.AddSingleton(sp => new SoxService(
 				sp.GetRequiredService<ProcessRunner>(),
diff --git a/src/Services/Audio/RealDffFixture.cs b/src/Services/Audio/RealDffFixture.cs
deleted file mode 100644
index 137c52b..0000000
--- a/src/Services/Audio/RealDffFixture.cs
+++ /dev/null
@@ -1,50 +0,0 @@
-using System.Buffers.Binary;
-
-namespace Services.Audio;
-
-internal static class RealDffFixture
-{
-	public const string Path = @"C:\Temp\t.dff";
-
-	private const int DsdSampleRate = 2822400;
-	private const int Channels = 2;
-	private const int PcmSampleRate = 88200;
-	private const int BytesPerPcmSample = 3;
-
-	public static bool Exists() => File.Exists(Path);
-
-	public static long ExpectedPcmBytes()
-	{
-		if (!File.Exists(Path))
-			return -1;
-		var dsdBytes = ReadDsdChunkSize(Path);
-		if (dsdBytes <= 0)
-			return -1;
-
-		var dsdSamplesPerChannel = dsdBytes / Channels;
-		var durationSeconds = (double)dsdSamplesPerChannel * 8.0 / DsdSampleRate;
-		var pcmSamples = (long)(durationSeconds * PcmSampleRate);
-		return pcmSamples * Channels * BytesPerPcmSample;
-	}
-
-	private static long ReadDsdChunkSize(string path)
-	{
-		using FileStream fs = File.OpenRead(path);
-		fs.Seek(16, SeekOrigin.Begin);
-		Span<byte> hdr = stackalloc byte[12];
-		while (fs.Position < fs.Length - 12)
-		{
-			if (fs.Read(hdr) < 12)
-				break;
-			var id = System.Text.Encoding.ASCII.GetString(hdr[..4]);
-			var size = BinaryPrimitives.ReadUInt64BigEndian(hdr[4..]);
-			if (id == "DSD ")
-				return (long)size;
-			var skip = size % 2 != 0 ? size + 1 : size;
-			if (fs.Position + (long)skip > fs.Length)
-				break;
-			fs.Seek((long)skip, SeekOrigin.Current);
-		}
-		return -1;
-	}
-}
diff --git a/src/Services/Audio/SacdProbeRunner.cs b/src/Services/Audio/SacdProbeRunner.cs
deleted file mode 100644
index c2f70dc..0000000
--- a/src/Services/Audio/SacdProbeRunner.cs
+++ /dev/null
@@ -1,357 +0,0 @@
-using System.Diagnostics;
-using Core;
-using ErrorOr;
-
-namespace Services.Audio;
-
-internal sealed class SacdProbeRunner(SaraconService saracon)
-{
-	private static readonly string JournalPath = Path.Combine(
-		PathResolver.RepoRoot,
-		"docs",
-		"superpowers",
-		"audits",
-		"sacd-probe-journal.md"
-	);
-	private static readonly string OutRoot = @"C:\Temp\saracon-probe\out";
-
-	private enum FailureSignature
-	{
-		None,
-		RegistryOleInit,
-		CharsetEncoding,
-		Truncation,
-		ZeroBytes,
-		Other,
-	}
-
-	private record ProbeVariant(
-		string Label,
-		bool Stripped,
-		bool Headless,
-		FailureSignature DeclaredExpected
-	);
-
-	private static readonly ProbeVariant[] Matrix =
-	[
-		new("raw/headless", false, true, FailureSignature.None),
-		new("stripped/headless", true, true, FailureSignature.None),
-		new("raw/visible", false, false, FailureSignature.CharsetEncoding),
-		new("stripped/visible", true, false, FailureSignature.CharsetEncoding),
-	];
-
-	private record RunResult(string Row, FailureSignature Signature);
-
-	public async Task<ProbeResult> RunAllAsync(CancellationToken ct)
-	{
-		Directory.CreateDirectory(OutRoot);
-
-		if (!RealDffFixture.Exists())
-		{
-			await Console.Error.WriteLineAsync(
-				$"PRECONDITION FAILED: real DFF not found at {RealDffFixture.Path}",
-				ct
-			);
-			return new ProbeResult(
-				false,
-				JournalPath,
-				[$"PRECONDITION FAILED: DFF not found at {RealDffFixture.Path}"]
-			);
-		}
-
-		var expectedBytes = RealDffFixture.ExpectedPcmBytes();
-		Console.WriteLine($"Real DFF  : {RealDffFixture.Path}");
-		Console.WriteLine($"Expected  : {expectedBytes:N0} PCM bytes");
-
-		Console.WriteLine("\n--- Precondition canary (raw/headless) ---");
-		RunResult canary = await RunVariantAsync(Matrix[0], expectedBytes, ct);
-		AppendJournal(canary.Row);
-		Console.WriteLine(canary.Row);
-
-		if (canary.Signature == FailureSignature.RegistryOleInit)
-		{
-			await Console.Error.WriteLineAsync(
-				"\nPRECONDITION FAILED: registry/OLE init error ΓÇö v2 spec ┬º4.\n"
-					+ "Fix A: grant HKCU\\Software\\Weiss Engineering FullControl to the executing SID.\n"
-					+ "Fix B: confirm agent session vs. interactive session mismatch via:\n"
-					+ "       [Security.Principal.WindowsIdentity]::GetCurrent().Name; query session; (Get-Process -Id $PID).SessionId",
-				ct
-			);
-			return new ProbeResult(false, JournalPath, [canary.Row]);
-		}
-
-		Console.WriteLine("\n--- Full matrix ---");
-		List<string> variantRows = [];
-		var unexpectedFail = false;
-		foreach (ProbeVariant variant in Matrix)
-		{
-			RunResult run = await RunVariantAsync(variant, expectedBytes, ct);
-			AppendJournal(run.Row);
-			Console.WriteLine(run.Row);
-			variantRows.Add(run.Row);
-			if (run.Row.Contains("FAIL-unexpected"))
-				unexpectedFail = true;
-		}
-
-		Console.WriteLine(unexpectedFail ? "\nPROBE FAIL (unexpected outcome)" : "\nPROBE PASS");
-		return new ProbeResult(!unexpectedFail, JournalPath, variantRows);
-	}
-
-	private async Task<RunResult> RunVariantAsync(
-		ProbeVariant v,
-		long expectedBytes,
-		CancellationToken ct
-	)
-	{
-		Stopwatch sw = Stopwatch.StartNew();
-		var input = RealDffFixture.Path;
-
-		try
-		{
-			if (v.Stripped)
-			{
-				ErrorOr<string> strip = await DffMetadataStripper.StripId3TagsAsync(
-					input,
-					OutRoot,
-					ct
-				);
-				if (strip.IsError)
-				{
-					sw.Stop();
-					var errText = strip.Errors[0].Description;
-					FailureSignature sig = Classify(errText);
-					return MakeResult(v, sw, -1, 0, sig, errText);
-				}
-				input = strip.Value;
-			}
-
-			if (v.Headless)
-				return await RunHeadlessAsync(v, input, sw, expectedBytes, ct);
-			else
-				return await RunVisibleAsync(v, input, sw, expectedBytes);
-		}
-		catch (Exception ex)
-		{
-			sw.Stop();
-			return new RunResult(
-				$"| {Ts()} | {v.Label} | exit=-2 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | {Escape(ex.Message)} |",
-				FailureSignature.Other
-			);
-		}
-	}
-
-	private async Task<RunResult> RunHeadlessAsync(
-		ProbeVariant v,
-		string input,
-		Stopwatch sw,
-		long expectedBytes,
-		CancellationToken ct
-	)
-	{
-		ErrorOr<string> result = await saracon.ConvertDsdToPcmAsync(
-			input,
-			OutRoot,
-			88200,
-			24,
-			0.0,
-			2822400,
-			2,
-			ct: ct
-		);
-		sw.Stop();
-
-		if (result.IsError)
-		{
-			var errText = result.Errors[0].Description;
-			return MakeResult(v, sw, -1, 0, Classify(errText), errText);
-		}
-
-		var outFile = result.Value;
-		var bytes = File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
-		FailureSignature sig = OutSig(bytes, expectedBytes);
-		return MakeResult(
-			v,
-			sw,
-			0,
-			bytes,
-			sig,
-			$"{Path.GetFileName(outFile)} {bytes:N0}/{expectedBytes:N0} bytes"
-		);
-	}
-
-	private static async Task<RunResult> RunVisibleAsync(
-		ProbeVariant v,
-		string input,
-		Stopwatch sw,
-		long expectedBytes
-	)
-	{
-		var saraconPath = FindSaracon();
-		if (saraconPath is null)
-		{
-			sw.Stop();
-			return new RunResult(
-				$"| {Ts()} | {v.Label} | exit=-1 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | saracon not found on PATH |",
-				FailureSignature.Other
-			);
-		}
-
-		var outName = Path.Combine(OutRoot, Path.GetFileNameWithoutExtension(input) + "-visible");
-		ProcessStartInfo psi = new()
-		{
-			FileName = saraconPath,
-			UseShellExecute = false,
-			RedirectStandardOutput = true,
-			RedirectStandardError = true,
-			CreateNoWindow = false,
-			WorkingDirectory = OutRoot,
-		};
-		foreach (
-			var arg in new[]
-			{
-				"-c",
-				"d2p",
-				"-r",
-				"88200",
-				"-f",
-				"wav",
-				"-n",
-				"24bit",
-				"-d",
-				"tpdf",
-				"-g",
-				"0.00",
-				"-T",
-				"-V",
-				"all",
-				"-t",
-				OutRoot,
-				input,
-			}
-		)
-			psi.ArgumentList.Add(arg);
-
-		using Process proc =
-			Process.Start(psi)
-			?? throw new InvalidOperationException("Failed to start saracon (visible)");
-
-		Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
-		Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
-		await Task.WhenAll(stdoutTask, stderrTask);
-		await proc.WaitForExitAsync(cancellationToken: default);
-		sw.Stop();
-
-		var combined = await stdoutTask + await stderrTask;
-		FailureSignature sig = Classify(combined);
-
-		if (proc.ExitCode != 0 || sig != FailureSignature.None)
-			return MakeResult(
-				v,
-				sw,
-				proc.ExitCode,
-				0,
-				sig,
-				combined[..Math.Min(200, combined.Length)]
-			);
-
-		var outFile = Directory
-			.EnumerateFiles(OutRoot, "*.wav")
-			.OrderByDescending(f => new FileInfo(f).LastWriteTime)
-			.FirstOrDefault();
-		var bytes = outFile is not null && File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
-		return MakeResult(
-			v,
-			sw,
-			proc.ExitCode,
-			bytes,
-			OutSig(bytes, expectedBytes),
-			$"{(outFile is null ? "no output" : Path.GetFileName(outFile))} {bytes:N0}/{expectedBytes:N0} bytes"
-		);
-	}
-
-	private static string? FindSaracon()
-	{
-		if (ProcessRunner.IsOnPath("saracon"))
-			return "saracon";
-		var path = Environment.GetEnvironmentVariable("PATH") ?? "";
-		return path.Split(Path.PathSeparator)
-			.Select(d => Path.Combine(d, "saracon.exe"))
-			.FirstOrDefault(File.Exists);
-	}
-
-	private static FailureSignature Classify(string text) =>
-		text switch
-		{
-			var s
-				when s.Contains("Cannot initialize OLE", StringComparison.OrdinalIgnoreCase)
-					|| s.Contains("Can't open registry key", StringComparison.OrdinalIgnoreCase)
-					|| s.Contains("wxIdleWakeUpModule", StringComparison.OrdinalIgnoreCase) =>
-				FailureSignature.RegistryOleInit,
-
-			var s
-				when s.Contains("Unknown encoding", StringComparison.OrdinalIgnoreCase)
-					|| s.Contains(
-						"Cannot convert from the charset",
-						StringComparison.OrdinalIgnoreCase
-					) => FailureSignature.CharsetEncoding,
-
-			_ => FailureSignature.Other,
-		};
-
-	private static FailureSignature OutSig(long bytes, long expected) =>
-		bytes == 0 ? FailureSignature.ZeroBytes
-		: bytes < expected * 0.5 ? FailureSignature.Truncation
-		: FailureSignature.None;
-
-	private static RunResult MakeResult(
-		ProbeVariant v,
-		Stopwatch sw,
-		int exit,
-		long bytes,
-		FailureSignature actual,
-		string note
-	)
-	{
-		string verdict;
-		if (actual == FailureSignature.None)
-			verdict = "PASS";
-		else if (v.DeclaredExpected != FailureSignature.None && actual == v.DeclaredExpected)
-			verdict = $"FAIL-expected({actual})";
-		else
-			verdict = $"FAIL-unexpected({actual})";
-
-		var row =
-			$"| {Ts()} | {v.Label} | exit={exit} | {sw.ElapsedMilliseconds}ms | {bytes:N0}B | {verdict} | {Escape(note)} |";
-		return new RunResult(row, actual);
-	}
-
-	private static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
-
-	private static string Escape(string s) =>
-		s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
-
-	private static void AppendJournal(string row)
-	{
-		var content = File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : "";
-		if (content.Contains(row))
-			return;
-
-		var header =
-			"| timestamp | variant | exit | elapsed | out-bytes | verdict | note |\n"
-			+ "|---|---|---|---|---|---|---|\n";
-		var runsIdx = content.IndexOf("## Runs (v2", StringComparison.Ordinal);
-		if (runsIdx >= 0)
-		{
-			var afterSep = content.IndexOf(
-				'\n',
-				content.IndexOf("|---|", runsIdx, StringComparison.Ordinal)
-			);
-			content = content.Insert(afterSep + 1, row + "\n");
-		}
-		else
-		{
-			content += $"\n## Runs (v2 ΓÇö real DFF, {DateTime.Now:yyyy-MM-dd})\n\n{header}{row}\n";
-		}
-		File.WriteAllText(JournalPath, content);
-	}
-}
diff --git a/src/Services/Audio/SacdProbeService.cs b/src/Services/Audio/SacdProbeService.cs
deleted file mode 100644
index 4c566a0..0000000
--- a/src/Services/Audio/SacdProbeService.cs
+++ /dev/null
@@ -1,15 +0,0 @@
-namespace Services.Audio;
-
-public sealed class SacdProbeService(SaraconService saracon)
-{
-	private readonly SacdProbeRunner Runner = new(saracon);
-
-	public Task<ProbeResult> RunProbeAsync(CancellationToken ct = default) =>
-		Runner.RunAllAsync(ct);
-}
-
-public sealed record ProbeResult(
-	bool Passed,
-	string JournalPath,
-	IReadOnlyList<string> VariantResults
-);
