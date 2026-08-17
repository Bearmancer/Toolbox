# Review package: ef43b65..9c677c9

## Commits
9c677c9 fix(checks): P3.1 review findings ΓÇö hard temp assert, try/finally reaping

## Files changed
 checks/Program.cs | 33 ++++++++++++++++++++++++---------
 task-16-report.md | 37 ++++++++++++++-----------------------
 2 files changed, 38 insertions(+), 32 deletions(-)

## Diff
diff --git a/checks/Program.cs b/checks/Program.cs
index 3649663..13027ab 100644
--- a/checks/Program.cs
+++ b/checks/Program.cs
@@ -8,22 +8,27 @@ if (args.Length > 0 && args[0] == "--stub")
 await Telemetry.Configure(LogEventLevel.Fatal);
 
 string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
 List<(string Name, bool Pass, string? Error)> results = [];
 
 try
 {
 	Directory.CreateDirectory(tempRoot);
 	string normalizedTempRoot = Path.GetFullPath(tempRoot);
 	string systemTemp = Path.GetFullPath(Path.GetTempPath());
-	Assert("TempRootUnderSystemTemp", normalizedTempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase),
-		$"tempRoot={normalizedTempRoot} systemTemp={systemTemp}");
+	if (!normalizedTempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase))
+	{
+		Console.WriteLine($"  FAIL: TempRootUnderSystemTemp ΓÇö tempRoot={normalizedTempRoot} systemTemp={systemTemp}");
+		throw new InvalidOperationException($"Temp root {normalizedTempRoot} is not under system temp {systemTemp}");
+	}
+	Console.WriteLine("  PASS: TempRootUnderSystemTemp");
+	results.Add(("TempRootUnderSystemTemp", true, null));
 
 	await ChildStubExitZeroAsync();
 	await ChildStubExitNonzeroAsync();
 	await ChildStubOutputVolumeAsync();
 	await ChildStubDelayAsync();
 	await ChildStubIgnoreTerminationAsync();
 }
 finally
 {
 	if (Directory.Exists(tempRoot))
@@ -97,27 +102,37 @@ async Task ChildStubIgnoreTerminationAsync()
 		RedirectStandardOutput = true,
 		CreateNoWindow = true,
 		WorkingDirectory = tempRoot,
 	};
 	Process? process = Process.Start(psi);
 	if (process is null)
 	{
 		Assert("ChildStubIgnoreTermination", false, "failed to start");
 		return;
 	}
-	await Task.Delay(200);
-	process.Kill(entireProcessTree: true);
-	using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
-	await process.WaitForExitAsync(cts.Token);
-	bool reaped = process.HasExited;
-	process.Dispose();
-	Assert("ChildStubIgnoreTermination", reaped, "process not reaped after kill");
+	try
+	{
+		await Task.Delay(200);
+		process.Kill(entireProcessTree: true);
+		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
+		await process.WaitForExitAsync(cts.Token);
+		Assert("ChildStubIgnoreTermination", process.HasExited, "process not reaped after kill");
+	}
+	finally
+	{
+		if (!process.HasExited)
+		{
+			try { process.Kill(entireProcessTree: true); }
+			catch (InvalidOperationException) { }
+		}
+		process.Dispose();
+	}
 }
 
 async Task<int> SpawnStubAsync(string stubArgs)
 {
 	string exePath = Environment.ProcessPath!;
 	ProcessStartInfo psi = new()
 	{
 		FileName = exePath,
 		Arguments = $"--stub {stubArgs}",
 		UseShellExecute = false,
diff --git a/task-16-report.md b/task-16-report.md
index 65b537d..0ccf10d 100644
--- a/task-16-report.md
+++ b/task-16-report.md
@@ -1,102 +1,93 @@
 # Task 16 ΓÇö P3.1 Harness Infrastructure
 
-**Branch:** sacd-completion-v2 | **HEAD:** 37b285a
+**Branch:** sacd-completion-v2 | **Baseline:** ef43b65 | **Fix:** HEAD
 **Date:** 2026-08-17
 
 ## Summary
 
-Replaced temporary P1.2/P1.6 GuardChecks with durable P3.1 harness infrastructure. Single committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard-assert and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.
+Replaced temporary P1.2/P1.6 GuardChecks with durable P3.1 harness infrastructure. Single committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard-assert (throws on mismatch) and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.
 
 ## Files Changed
 
 | File | Lines | Change |
 |------|-------|--------|
-| `checks/Program.cs` | 168 | Full rewrite: P3.1 harness with assertion helper, temp workspace, child stub, forced-failure, Telemetry.Fatal |
-| `checks/GuardChecks.csproj` | 13 | Unchanged (already references Audio, no test packages) |
+| `checks/Program.cs` | 188 | P3.1 harness: assertion helper, hard temp assertion, child stub with try/finally reaping, forced-failure, Telemetry.Fatal |
+| `checks/GuardChecks.csproj` | 13 | Unchanged (references Audio, no test packages) |
 
 ## Subtask Results
 
 ### 1. Plain `.cs` entry point, no test packages, referencing production project
 
-GuardChecks.csproj unchanged ΓÇö references `Audio.csproj` (which transitively references `Core`). No xUnit/NUnit/MSTest. Top-level statements with `Main()` implicit.
+GuardChecks.csproj unchanged ΓÇö references `Audio.csproj` (transitively references `Core`). No xUnit/NUnit/MSTest. Top-level statements with `Main()` implicit.
 
-**Output:** Build succeeded. 0 Warning(s) 0 Error(s)
+**Build:** `dotnet build checks/GuardChecks.csproj` ΓåÆ 0 Warning(s) 0 Error(s)
 
 ### 2. Assertion helpers with failure output naming the case
 
-`Assert(string name, bool condition, string? error)` records pass/fail with case name into `results` list. Output format: `PASS: {name}` or `FAIL: {name} ΓÇö {error}`.
+`Assert(string name, bool condition, string? error)` records pass/fail with case name into `results` list. Output: `PASS: {name}` or `FAIL: {name} ΓÇö {error}`.
 
 ### 3. Temp-workspace creation and teardown, hard assertion under system temp
 
 ```
 string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
 ```
 
-Hard assertion: `Path.GetFullPath(tempRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()))`. Teardown in `finally` block: `Directory.Delete(tempRoot, true)`.
+**Hard assertion:** if `Path.GetFullPath(tempRoot)` does not start with `Path.GetFullPath(Path.GetTempPath())`, prints `FAIL: TempRootUnderSystemTemp` then throws `InvalidOperationException`. No media use possible before this check. Teardown in `finally`: `Directory.Delete(tempRoot, true)`.
 
 ### 4. Controllable child-process stub
 
 Self-invocation mode: `--stub --exit <code> --output <lines> --delay <ms> --ignore-termination`.
 
 | Stub arg | Behavior |
 |----------|----------|
 | `--exit N` | Exit with code N |
 | `--output N` | Print N lines of stdout |
 | `--delay N` | Sleep N ms before exit |
 | `--ignore-termination` | Wait forever (until killed) |
 
 ### 5. Nonzero exit on failure; per-case summary; Telemetry Fatal
 
-`Telemetry.Configure(LogEventLevel.Fatal)` at entry. Exit code 1 if any case fails or `--force-fail` present. Summary line: `RESULTS: X passed, Y failed, Z total`.
+`Telemetry.Configure(LogEventLevel.Fatal)` at entry. Exit code 1 if any case fails or `--force-fail` present. Summary: `RESULTS: X passed, Y failed, Z total`.
+
+### 6. Fix: ChildStubIgnoreTermination try/finally (review finding)
+
+Process kill/reap/dispose wrapped in try/finally. If `Kill()` or `WaitForExitAsync` throws, `finally` block kills (if not exited) and disposes ΓÇö no orphan possible. Bounded wait: 5-second `CancellationTokenSource` on `WaitForExitAsync`.
 
 ## Raw Outputs
 
 ### Clean run (no --force-fail)
 
 ```
   PASS: TempRootUnderSystemTemp
   PASS: ChildStubExitZero
   PASS: ChildStubExitNonzero
   PASS: ChildStubOutputVolume
   PASS: ChildStubDelay
   PASS: ChildStubIgnoreTermination
 
 RESULTS: 6 passed, 0 failed, 6 total
-  PASS: TempRootUnderSystemTemp
-  PASS: ChildStubExitZero
-  PASS: ChildStubExitNonzero
-  PASS: ChildStubOutputVolume
-  PASS: ChildStubDelay
-  PASS: ChildStubIgnoreTermination
 EXIT_CODE: 0
 ```
 
 ### Forced-failure run (--force-fail)
 
 ```
   PASS: TempRootUnderSystemTemp
   PASS: ChildStubExitZero
   PASS: ChildStubExitNonzero
   PASS: ChildStubOutputVolume
   PASS: ChildStubDelay
   PASS: ChildStubIgnoreTermination
-  FAIL: ForcedFailure - forced failure mode active
+  FAIL: ForcedFailure ΓÇö forced failure mode active
 
 RESULTS: 6 passed, 1 failed, 7 total
-  PASS: TempRootUnderSystemTemp
-  PASS: ChildStubExitZero
-  PASS: ChildStubExitNonzero
-  PASS: ChildStubOutputVolume
-  PASS: ChildStubDelay
-  PASS: ChildStubIgnoreTermination
-  FAIL: ForcedFailure - Forced failure mode active
 EXIT_CODE: 1
 ```
 
 ## Build Verification
 
 ```
 dotnet build checks/GuardChecks.csproj
   Core -> artifacts\bin\Core\debug\Core.dll
   Audio -> artifacts\bin\Audio\debug\Audio.dll
   GuardChecks -> artifacts\bin\GuardChecks\debug\GuardChecks.dll
