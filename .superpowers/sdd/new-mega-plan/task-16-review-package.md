# Review package: 37b285a..ef43b65

## Commits
ef43b65 feat(checks): P3.1 durable harness infrastructure

## Files changed
 checks/GuardChecks.csproj |  13 ++++
 checks/Program.cs         | 194 ++++++++++++++++++++++++++++++++++++++++++++++
 task-16-report.md         | 130 +++++++++++++++++++++++++++++++
 3 files changed, 337 insertions(+)

## Diff
diff --git a/checks/GuardChecks.csproj b/checks/GuardChecks.csproj
new file mode 100644
index 0000000..be9a921
--- /dev/null
+++ b/checks/GuardChecks.csproj
@@ -0,0 +1,13 @@
+∩╗┐<Project Sdk="Microsoft.NET.Sdk">
+	<PropertyGroup>
+		<OutputType>Exe</OutputType>
+		<TargetFramework>net11.0</TargetFramework>
+		<ImplicitUsings>enable</ImplicitUsings>
+		<Nullable>enable</Nullable>
+		<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
+		<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
+	</PropertyGroup>
+	<ItemGroup>
+		<ProjectReference Include="..\src\Services\Audio\Audio.csproj" />
+	</ItemGroup>
+</Project>
diff --git a/checks/Program.cs b/checks/Program.cs
new file mode 100644
index 0000000..3649663
--- /dev/null
+++ b/checks/Program.cs
@@ -0,0 +1,194 @@
+∩╗┐using System.Diagnostics;
+using Core;
+using Serilog.Events;
+
+if (args.Length > 0 && args[0] == "--stub")
+	return await RunStubAsync(args);
+
+await Telemetry.Configure(LogEventLevel.Fatal);
+
+string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
+List<(string Name, bool Pass, string? Error)> results = [];
+
+try
+{
+	Directory.CreateDirectory(tempRoot);
+	string normalizedTempRoot = Path.GetFullPath(tempRoot);
+	string systemTemp = Path.GetFullPath(Path.GetTempPath());
+	Assert("TempRootUnderSystemTemp", normalizedTempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase),
+		$"tempRoot={normalizedTempRoot} systemTemp={systemTemp}");
+
+	await ChildStubExitZeroAsync();
+	await ChildStubExitNonzeroAsync();
+	await ChildStubOutputVolumeAsync();
+	await ChildStubDelayAsync();
+	await ChildStubIgnoreTerminationAsync();
+}
+finally
+{
+	if (Directory.Exists(tempRoot))
+		Directory.Delete(tempRoot, true);
+}
+
+if (args.Contains("--force-fail"))
+{
+	results.Add(("ForcedFailure", false, "Forced failure mode active"));
+	Console.WriteLine("  FAIL: ForcedFailure ΓÇö forced failure mode active");
+}
+
+Console.WriteLine();
+int passed = results.Count(r => r.Pass);
+int failed = results.Count(r => !r.Pass);
+Console.WriteLine($"RESULTS: {passed} passed, {failed} failed, {results.Count} total");
+foreach (var (name, pass, error) in results)
+	Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}: {name}{(error is not null ? $" ΓÇö {error}" : "")}");
+
+return failed > 0 ? 1 : 0;
+
+void Assert(string name, bool condition, string? error = null)
+{
+	if (condition)
+	{
+		results.Add((name, true, null));
+		Console.WriteLine($"  PASS: {name}");
+	}
+	else
+	{
+		results.Add((name, false, error));
+		Console.WriteLine($"  FAIL: {name}{(error is not null ? $" ΓÇö {error}" : "")}");
+	}
+}
+
+async Task ChildStubExitZeroAsync()
+{
+	int code = await SpawnStubAsync("--exit 0");
+	Assert("ChildStubExitZero", code == 0, $"exit code {code}");
+}
+
+async Task ChildStubExitNonzeroAsync()
+{
+	int code = await SpawnStubAsync("--exit 3");
+	Assert("ChildStubExitNonzero", code == 3, $"exit code {code}");
+}
+
+async Task ChildStubOutputVolumeAsync()
+{
+	(int code, string stdout) = await SpawnStubWithOutputAsync("--output 50");
+	int lineCount = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
+	Assert("ChildStubOutputVolume", code == 0 && lineCount == 50, $"exit {code}, lines {lineCount}");
+}
+
+async Task ChildStubDelayAsync()
+{
+	var sw = Stopwatch.StartNew();
+	int code = await SpawnStubAsync("--delay 200");
+	sw.Stop();
+	Assert("ChildStubDelay", code == 0 && sw.ElapsedMilliseconds < 1000, $"exit {code}, {sw.ElapsedMilliseconds}ms");
+}
+
+async Task ChildStubIgnoreTerminationAsync()
+{
+	string exePath = Environment.ProcessPath!;
+	ProcessStartInfo psi = new()
+	{
+		FileName = exePath,
+		Arguments = "--stub --ignore-termination --delay 60000",
+		UseShellExecute = false,
+		RedirectStandardOutput = true,
+		CreateNoWindow = true,
+		WorkingDirectory = tempRoot,
+	};
+	Process? process = Process.Start(psi);
+	if (process is null)
+	{
+		Assert("ChildStubIgnoreTermination", false, "failed to start");
+		return;
+	}
+	await Task.Delay(200);
+	process.Kill(entireProcessTree: true);
+	using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
+	await process.WaitForExitAsync(cts.Token);
+	bool reaped = process.HasExited;
+	process.Dispose();
+	Assert("ChildStubIgnoreTermination", reaped, "process not reaped after kill");
+}
+
+async Task<int> SpawnStubAsync(string stubArgs)
+{
+	string exePath = Environment.ProcessPath!;
+	ProcessStartInfo psi = new()
+	{
+		FileName = exePath,
+		Arguments = $"--stub {stubArgs}",
+		UseShellExecute = false,
+		RedirectStandardOutput = true,
+		CreateNoWindow = true,
+		WorkingDirectory = tempRoot,
+	};
+	Process? process = Process.Start(psi);
+	if (process is null)
+		return -1;
+	await process.WaitForExitAsync();
+	int code = process.ExitCode;
+	process.Dispose();
+	return code;
+}
+
+async Task<(int ExitCode, string Stdout)> SpawnStubWithOutputAsync(string stubArgs)
+{
+	string exePath = Environment.ProcessPath!;
+	ProcessStartInfo psi = new()
+	{
+		FileName = exePath,
+		Arguments = $"--stub {stubArgs}",
+		UseShellExecute = false,
+		RedirectStandardOutput = true,
+		CreateNoWindow = true,
+		WorkingDirectory = tempRoot,
+	};
+	Process? process = Process.Start(psi);
+	if (process is null)
+		return (-1, string.Empty);
+	string stdout = await process.StandardOutput.ReadToEndAsync();
+	await process.WaitForExitAsync();
+	int code = process.ExitCode;
+	process.Dispose();
+	return (code, stdout);
+}
+
+static async Task<int> RunStubAsync(string[] args)
+{
+	int exitCode = 0;
+	int outputLines = 0;
+	int delayMs = 0;
+	bool ignoreTermination = false;
+
+	for (int i = 1; i < args.Length; i++)
+	{
+		switch (args[i])
+		{
+			case "--exit" when i + 1 < args.Length:
+				exitCode = int.Parse(args[++i]);
+				break;
+			case "--output" when i + 1 < args.Length:
+				outputLines = int.Parse(args[++i]);
+				break;
+			case "--delay" when i + 1 < args.Length:
+				delayMs = int.Parse(args[++i]);
+				break;
+			case "--ignore-termination":
+				ignoreTermination = true;
+				break;
+		}
+	}
+
+	for (int i = 0; i < outputLines; i++)
+		Console.WriteLine($"stub-output-{i}");
+
+	if (ignoreTermination)
+		await Task.Delay(Timeout.Infinite);
+	else if (delayMs > 0)
+		await Task.Delay(delayMs);
+
+	return exitCode;
+}
diff --git a/task-16-report.md b/task-16-report.md
new file mode 100644
index 0000000..65b537d
--- /dev/null
+++ b/task-16-report.md
@@ -0,0 +1,130 @@
+# Task 16 ΓÇö P3.1 Harness Infrastructure
+
+**Branch:** sacd-completion-v2 | **HEAD:** 37b285a
+**Date:** 2026-08-17
+
+## Summary
+
+Replaced temporary P1.2/P1.6 GuardChecks with durable P3.1 harness infrastructure. Single committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard-assert and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.
+
+## Files Changed
+
+| File | Lines | Change |
+|------|-------|--------|
+| `checks/Program.cs` | 168 | Full rewrite: P3.1 harness with assertion helper, temp workspace, child stub, forced-failure, Telemetry.Fatal |
+| `checks/GuardChecks.csproj` | 13 | Unchanged (already references Audio, no test packages) |
+
+## Subtask Results
+
+### 1. Plain `.cs` entry point, no test packages, referencing production project
+
+GuardChecks.csproj unchanged ΓÇö references `Audio.csproj` (which transitively references `Core`). No xUnit/NUnit/MSTest. Top-level statements with `Main()` implicit.
+
+**Output:** Build succeeded. 0 Warning(s) 0 Error(s)
+
+### 2. Assertion helpers with failure output naming the case
+
+`Assert(string name, bool condition, string? error)` records pass/fail with case name into `results` list. Output format: `PASS: {name}` or `FAIL: {name} ΓÇö {error}`.
+
+### 3. Temp-workspace creation and teardown, hard assertion under system temp
+
+```
+string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
+```
+
+Hard assertion: `Path.GetFullPath(tempRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()))`. Teardown in `finally` block: `Directory.Delete(tempRoot, true)`.
+
+### 4. Controllable child-process stub
+
+Self-invocation mode: `--stub --exit <code> --output <lines> --delay <ms> --ignore-termination`.
+
+| Stub arg | Behavior |
+|----------|----------|
+| `--exit N` | Exit with code N |
+| `--output N` | Print N lines of stdout |
+| `--delay N` | Sleep N ms before exit |
+| `--ignore-termination` | Wait forever (until killed) |
+
+### 5. Nonzero exit on failure; per-case summary; Telemetry Fatal
+
+`Telemetry.Configure(LogEventLevel.Fatal)` at entry. Exit code 1 if any case fails or `--force-fail` present. Summary line: `RESULTS: X passed, Y failed, Z total`.
+
+## Raw Outputs
+
+### Clean run (no --force-fail)
+
+```
+  PASS: TempRootUnderSystemTemp
+  PASS: ChildStubExitZero
+  PASS: ChildStubExitNonzero
+  PASS: ChildStubOutputVolume
+  PASS: ChildStubDelay
+  PASS: ChildStubIgnoreTermination
+
+RESULTS: 6 passed, 0 failed, 6 total
+  PASS: TempRootUnderSystemTemp
+  PASS: ChildStubExitZero
+  PASS: ChildStubExitNonzero
+  PASS: ChildStubOutputVolume
+  PASS: ChildStubDelay
+  PASS: ChildStubIgnoreTermination
+EXIT_CODE: 0
+```
+
+### Forced-failure run (--force-fail)
+
+```
+  PASS: TempRootUnderSystemTemp
+  PASS: ChildStubExitZero
+  PASS: ChildStubExitNonzero
+  PASS: ChildStubOutputVolume
+  PASS: ChildStubDelay
+  PASS: ChildStubIgnoreTermination
+  FAIL: ForcedFailure - forced failure mode active
+
+RESULTS: 6 passed, 1 failed, 7 total
+  PASS: TempRootUnderSystemTemp
+  PASS: ChildStubExitZero
+  PASS: ChildStubExitNonzero
+  PASS: ChildStubOutputVolume
+  PASS: ChildStubDelay
+  PASS: ChildStubIgnoreTermination
+  FAIL: ForcedFailure - Forced failure mode active
+EXIT_CODE: 1
+```
+
+## Build Verification
+
+```
+dotnet build checks/GuardChecks.csproj
+  Core -> artifacts\bin\Core\debug\Core.dll
+  Audio -> artifacts\bin\Audio\debug\Audio.dll
+  GuardChecks -> artifacts\bin\GuardChecks\debug\GuardChecks.dll
+Build succeeded. 0 Warning(s) 0 Error(s)
+```
+
+## Case Summary
+
+| # | Case | Result |
+|---|------|--------|
+| 1 | TempRootUnderSystemTemp | PASS |
+| 2 | ChildStubExitZero | PASS |
+| 3 | ChildStubExitNonzero | PASS |
+| 4 | ChildStubOutputVolume | PASS |
+| 5 | ChildStubDelay | PASS |
+| 6 | ChildStubIgnoreTermination | PASS |
+| 7 | ForcedFailure (--force-fail only) | FAIL (expected) |
+
+**6/6 PASS clean, 1/1 FAIL forced-failure. Exit 0 clean, exit 1 forced.**
+
+## Acceptance Criteria
+
+- [x] Harness runs, prints per-case results
+- [x] Exits 0 clean
+- [x] Exits non-zero when forced to fail
+- [x] Committed to the repo, not deleted
+
+## Concerns
+
+1. **Telemetry side effects:** `Configure(LogEventLevel.Fatal)` creates `state/logs/` directory and per-service JSONL files. Acceptable for on-demand harness.
+2. **Per-service JSONL sinks** still capture Debug+ despite Fatal console level ΓÇö by design per Telemetry.cs configuration.
