# Review package: 335468d..1e5b22b

## Commits
1e5b22b fix(checks): replace legacy ProcessPath! with is-null guards, fix switch indent
b8f35ce feat(checks): P3.5 ΓÇö ProcessRunner 6 termination cases, 29 pass 2 blocked

## Files changed
 checks/Program.cs | 197 ++++++++++++++++++++++++++++++++++++++++++++++++++++--
 task-20-report.md | 145 ++++++++++++++++++++++++++++++++++++++++
 2 files changed, 338 insertions(+), 4 deletions(-)

## Diff
diff --git a/checks/Program.cs b/checks/Program.cs
index f888886..6d08a6b 100644
--- a/checks/Program.cs
+++ b/checks/Program.cs
@@ -52,20 +52,27 @@ try
 	await InspectorNoCueValidDffInvalidArtifactsAsync();
 	await OrchestratorGuardSkipBlockedAsync();
 
 	await P34StripFourTopLevelId3Async();
 	await P34OddPadPreservedAsync();
 	await P34NestedPropId3RemovedAsync();
 	await P34TruncatedErrorNoOutputAsync();
 	await P34ZeroSizePropErrorAsync();
 	await P34ShortFormSizeWarnsAsync();
 	await P34RealDisc3BlockedAsync();
+
+	await P35Exit0WithStdoutAsync();
+	await P35Exit3WithStderrAsync();
+	await P35CallerCancellationAsync();
+	await P35TimeoutAsync();
+	await P35CompletionMarkerHangAsync();
+	await P35HighVolumeStdoutDrainAsync();
 }
 finally
 {
 	if (Directory.Exists(tempRoot))
 		Directory.Delete(tempRoot, true);
 }
 
 if (args.Contains("--force-fail"))
 {
 	results.Add(("ForcedFailure", false, "Forced failure mode active"));
@@ -119,21 +126,26 @@ async Task ChildStubOutputVolumeAsync()
 async Task ChildStubDelayAsync()
 {
 	var sw = Stopwatch.StartNew();
 	int code = await SpawnStubAsync("--delay 200");
 	sw.Stop();
 	Assert("ChildStubDelay", code == 0 && sw.ElapsedMilliseconds < 1000, $"exit {code}, {sw.ElapsedMilliseconds}ms");
 }
 
 async Task ChildStubIgnoreTerminationAsync()
 {
-	string exePath = Environment.ProcessPath!;
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert("ChildStubIgnoreTermination", false, "ProcessPath is null");
+		return;
+	}
 	ProcessStartInfo psi = new()
 	{
 		FileName = exePath,
 		Arguments = "--stub --ignore-termination --delay 60000",
 		UseShellExecute = false,
 		RedirectStandardOutput = true,
 		CreateNoWindow = true,
 		WorkingDirectory = tempRoot,
 	};
 	Process? process = Process.Start(psi);
@@ -243,42 +255,46 @@ async Task ReflectionAccessAsync()
 	await File.WriteAllTextAsync(Path.Combine(testDir, "02. track.flac"), "fake");
 
 	var result = getFlacsMethod.Invoke(null, new object[] { testDir });
 	bool hasEntries = result is Dictionary<int, string> dict && dict.Count == 2;
 
 	Assert("ReflectionAccess", hasEntries, $"result={result?.GetType().Name}");
 }
 
 async Task<int> SpawnStubAsync(string stubArgs)
 {
-	string exePath = Environment.ProcessPath!;
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+		return -1;
 	ProcessStartInfo psi = new()
 	{
 		FileName = exePath,
 		Arguments = $"--stub {stubArgs}",
 		UseShellExecute = false,
 		RedirectStandardOutput = true,
 		CreateNoWindow = true,
 		WorkingDirectory = tempRoot,
 	};
 	Process? process = Process.Start(psi);
 	if (process is null)
 		return -1;
 	await process.WaitForExitAsync();
 	int code = process.ExitCode;
 	process.Dispose();
 	return code;
 }
 
 async Task<(int ExitCode, string Stdout)> SpawnStubWithOutputAsync(string stubArgs)
 {
-	string exePath = Environment.ProcessPath!;
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+		return (-1, string.Empty);
 	ProcessStartInfo psi = new()
 	{
 		FileName = exePath,
 		Arguments = $"--stub {stubArgs}",
 		UseShellExecute = false,
 		RedirectStandardOutput = true,
 		CreateNoWindow = true,
 		WorkingDirectory = tempRoot,
 	};
 	Process? process = Process.Start(psi);
@@ -288,46 +304,63 @@ async Task<(int ExitCode, string Stdout)> SpawnStubWithOutputAsync(string stubAr
 	await process.WaitForExitAsync();
 	int code = process.ExitCode;
 	process.Dispose();
 	return (code, stdout);
 }
 
 static async Task<int> RunStubAsync(string[] args)
 {
 	int exitCode = 0;
 	int outputLines = 0;
+	int stderrLines = 0;
 	int delayMs = 0;
+	int completeAfterMs = 0;
 	bool ignoreTermination = false;
 
 	for (int i = 1; i < args.Length; i++)
 	{
 		switch (args[i])
 		{
 			case "--exit" when i + 1 < args.Length:
 				exitCode = int.Parse(args[++i]);
 				break;
 			case "--output" when i + 1 < args.Length:
 				outputLines = int.Parse(args[++i]);
 				break;
 			case "--delay" when i + 1 < args.Length:
 				delayMs = int.Parse(args[++i]);
 				break;
 			case "--ignore-termination":
 				ignoreTermination = true;
 				break;
+			case "--stderr" when i + 1 < args.Length:
+				stderrLines = int.Parse(args[++i]);
+				break;
+			case "--complete-after" when i + 1 < args.Length:
+				completeAfterMs = int.Parse(args[++i]);
+				break;
 		}
 	}
 
 	for (int i = 0; i < outputLines; i++)
 		Console.WriteLine($"stub-output-{i}");
 
-	if (ignoreTermination)
+	for (int i = 0; i < stderrLines; i++)
+		await Console.Error.WriteLineAsync($"stub-stderr-{i}");
+
+	if (completeAfterMs > 0)
+	{
+		await Task.Delay(completeAfterMs);
+		Console.WriteLine("DONE");
+		await Task.Delay(Timeout.Infinite);
+	}
+	else if (ignoreTermination)
 		await Task.Delay(Timeout.Infinite);
 	else if (delayMs > 0)
 		await Task.Delay(delayMs);
 
 	return exitCode;
 }
 
 async Task GetFlacsByTrackNumberEmptyDirAsync()
 {
 	string testDir = Path.Combine(tempRoot, "p331-empty-flacs");
@@ -891,10 +924,166 @@ async Task P34RealDisc3BlockedAsync()
 		}
 	}
 
 	string blocker = foundPath == "none"
 		? "Real Disc3 DFF path absent; synthetic fixtures cover strip logic"
 		: $"Real Disc3 path found at {foundPath}; no File.ReadAllBytes on media; 3.33GB evidence required for PASS";
 
 	blocked.Add($"{caseName} ΓÇö {blocker} signature={signature}");
 	Console.WriteLine($"  BLOCKED: {caseName} ΓÇö {blocker} signature={signature}");
 }
+
+async Task P35Exit0WithStdoutAsync()
+{
+	string caseName = "P3.5.1_Exit0WithStdout [ProcessRunner.RunAsync, TerminationReason.Exited]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		exePath,
+		["--stub", "--exit", "0", "--output", "10"],
+		CancellationToken.None
+	);
+	bool pass = !result.IsError
+		&& result.Value.ExitCode == 0
+		&& result.Value.TerminationReason == TerminationReason.Exited
+		&& result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 10;
+	Assert(caseName, pass,
+		result.IsError
+			? $"error={result.Errors[0].Description}"
+			: $"exit={result.Value.ExitCode} reason={result.Value.TerminationReason} lines={result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}");
+}
+
+async Task P35Exit3WithStderrAsync()
+{
+	string caseName = "P3.5.2_Exit3WithStderr [ProcessRunner.RunAsync, TerminationReason.Exited, stderr]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		exePath,
+		["--stub", "--exit", "3", "--stderr", "5"],
+		CancellationToken.None
+	);
+	bool pass = !result.IsError
+		&& result.Value.ExitCode == 3
+		&& result.Value.TerminationReason == TerminationReason.Exited
+		&& result.Value.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 5;
+	Assert(caseName, pass,
+		result.IsError
+			? $"error={result.Errors[0].Description}"
+			: $"exit={result.Value.ExitCode} reason={result.Value.TerminationReason} stderrLines={result.Value.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}");
+}
+
+async Task P35CallerCancellationAsync()
+{
+	string caseName = "P3.5.3_CallerCancellation [ProcessRunner.RunAsync, TerminationReason.CallerCanceled]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	using CancellationTokenSource cts = new();
+	await cts.CancelAsync();
+	bool threwCanceled = false;
+	TerminationReason capturedReason = default;
+	try
+	{
+		await runner.RunAsync(
+			exePath,
+			["--stub", "--exit", "0", "--delay", "10000"],
+			cts.Token
+		);
+	}
+	catch (ProcessRunnerCanceledException ex)
+	{
+		threwCanceled = true;
+		capturedReason = ex.Result.TerminationReason;
+	}
+	Assert(caseName, threwCanceled && capturedReason == TerminationReason.CallerCanceled,
+		$"threw={threwCanceled} reason={capturedReason}");
+}
+
+async Task P35TimeoutAsync()
+{
+	string caseName = "P3.5.4_Timeout [ProcessRunner.RunAsync, TerminationReason.Timeout]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		exePath,
+		["--stub", "--exit", "0", "--delay", "10000"],
+		CancellationToken.None,
+		timeout: TimeSpan.FromMilliseconds(200)
+	);
+	bool pass = !result.IsError
+		&& result.Value.TerminationReason == TerminationReason.Timeout;
+	Assert(caseName, pass,
+		result.IsError
+			? $"error={result.Errors[0].Description}"
+			: $"reason={result.Value.TerminationReason}");
+}
+
+async Task P35CompletionMarkerHangAsync()
+{
+	string caseName = "P3.5.5_CompletionMarkerHang [ProcessRunner.RunAsync, TerminationReason.KilledAfterCompletionMarker]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		exePath,
+		["--stub", "--complete-after", "100"],
+		CancellationToken.None,
+		completionPattern: "DONE",
+		completionTimeout: TimeSpan.FromMilliseconds(200)
+	);
+	bool pass = !result.IsError
+		&& result.Value.TerminationReason == TerminationReason.KilledAfterCompletionMarker;
+	Assert(caseName, pass,
+		result.IsError
+			? $"error={result.Errors[0].Description}"
+			: $"reason={result.Value.TerminationReason}");
+}
+
+async Task P35HighVolumeStdoutDrainAsync()
+{
+	string caseName = "P3.5.6_HighVolumeStdoutDrain [ProcessRunner.RunAsync, output drain]";
+	string? exePath = Environment.ProcessPath;
+	if (exePath is null)
+	{
+		Assert(caseName, false, "ProcessPath is null");
+		return;
+	}
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		exePath,
+		["--stub", "--exit", "0", "--output", "1000"],
+		CancellationToken.None
+	);
+	int lineCount = result.IsError ? 0 : result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
+	bool pass = !result.IsError
+		&& result.Value.ExitCode == 0
+		&& result.Value.TerminationReason == TerminationReason.Exited
+		&& lineCount == 1000;
+	Assert(caseName, pass,
+		result.IsError
+			? $"error={result.Errors[0].Description}"
+			: $"exit={result.Value.ExitCode} lines={lineCount}");
+}
diff --git a/task-20-report.md b/task-20-report.md
new file mode 100644
index 0000000..c3f0a38
--- /dev/null
+++ b/task-20-report.md
@@ -0,0 +1,145 @@
+# Task 20 ΓÇö P3.5 ProcessRunner Termination Cases
+
+**Branch:** sacd-completion-v2 | **Baseline:** 335468d | **Date:** 2026-08-17
+
+## Summary
+
+Six requirement-cited cases for P3.5 `ProcessRunner` termination reasons via real `ProcessRunner.RunAsync` against self-stub. Cases 1-2 exercise normal exit with stdout/stderr capture. Case 3 exercises caller cancellation via pre-cancelled `CancellationToken`. Case 4 exercises timeout termination. Case 5 exercises completion marker detection with grace kill. Case 6 exercises high-volume stdout drain. Stub extended with `--stderr N` and `--complete-after N` modes. Result: **29 PASS + 2 BLOCKED** (6 new P3.5 all PASS). Clean 0, forced nonzero. Telemetry Fatal. Temp teardown in finally. No new null literals, no nullable-forgiving operators, no production source edits. 3 legacy `Environment.ProcessPath!` sites replaced with `string?` + `is null` guards; switch case indent corrected.
+
+## Files Changed
+
+| File | Lines | Change |
+|------|-------|--------|
+| `checks/Program.cs` | 1089 | +6 P3.5 cases, +2 stub modes (`--stderr`, `--complete-after`), +6 case method invocations, 3 legacy `Environment.ProcessPath!` ΓåÆ `string?` + `is null` guards, switch case indent fix |
+| `task-20-report.md` | ΓÇö | This report (repo root) |
+
+## Harness Output
+
+```
+RESULTS: 29 passed, 0 failed, 2 blocked, 31 total
+EXIT: 0
+```
+
+`--force-fail`: `RESULTS: 29 passed, 1 failed, 2 blocked, 32 total` ΓåÆ EXIT: 1 (forced nonzero verified).
+
+## Subtask Results
+
+### 1. P3.5.1 ΓÇö Exit0 With Stdout Capture
+
+**Citation:** `ProcessRunner.RunAsync, TerminationReason.Exited`
+**Fixture:** Self-stub: `--stub --exit 0 --output 10`
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--output", "10"], CancellationToken.None)`
+**Expected:** ExitCode=0, TerminationReason=Exited, Stdout contains 10 lines (`stub-output-0` through `stub-output-9`)
+**Assertions:**
+- `result.IsError` is false
+- `result.Value.ExitCode == 0`
+- `result.Value.TerminationReason == TerminationReason.Exited`
+- Stdout line count == 10
+**Orphan Check:** ProcessRunner completes normally ΓåÆ child reaped via `KillAndReapAsync` in `stopAndBuildAsync` (not needed for exit-0 path, `DrainOutputAsync` reaps)
+**Result:** PASS
+
+### 2. P3.5.2 ΓÇö Exit3 With Stderr Capture
+
+**Citation:** `ProcessRunner.RunAsync, TerminationReason.Exited, stderr`
+**Fixture:** Self-stub: `--stub --exit 3 --stderr 5`
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "3", "--stderr", "5"], CancellationToken.None)`
+**Expected:** ExitCode=3, TerminationReason=Exited, Stderr contains 5 lines (`stub-stderr-0` through `stub-stderr-4`)
+**Assertions:**
+- `result.IsError` is false
+- `result.Value.ExitCode == 3`
+- `result.Value.TerminationReason == TerminationReason.Exited`
+- Stderr line count == 5
+**Orphan Check:** Same as case 1 ΓÇö normal exit path, drain reaps
+**Result:** PASS
+
+### 3. P3.5.3 ΓÇö Caller Cancellation
+
+**Citation:** `ProcessRunner.RunAsync, TerminationReason.CallerCanceled`
+**Fixture:** Self-stub: `--stub --exit 0 --delay 10000` (long delay, never reached)
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--delay", "10000"], cts.Token)` with pre-cancelled token via `await cts.CancelAsync()`
+**Expected:** `ProcessRunnerCanceledException` thrown, `ex.Result.TerminationReason == TerminationReason.CallerCanceled`
+**Assertions:**
+- Exception caught is `ProcessRunnerCanceledException`
+- `ex.Result.TerminationReason == TerminationReason.CallerCanceled`
+**Orphan Check:** `stopAndBuildAsync` kills and reaps before throwing; `KillAndReapAsync` calls `process.Kill(entireProcessTree: true)` then `DrainOutputAsync`
+**Result:** PASS
+
+### 4. P3.5.4 ΓÇö Timeout
+
+**Citation:** `ProcessRunner.RunAsync, TerminationReason.Timeout`
+**Fixture:** Self-stub: `--stub --exit 0 --delay 10000` (10s delay, exceeds 200ms timeout)
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--delay", "10000"], CancellationToken.None, timeout: TimeSpan.FromMilliseconds(200))`
+**Expected:** `result.Value.TerminationReason == TerminationReason.Timeout`
+**Assertions:**
+- `result.IsError` is false
+- `result.Value.TerminationReason == TerminationReason.Timeout`
+**Orphan Check:** `stopAndBuildAsync` kills and reaps; `KillAndReapAsync` ensures process exited before return
+**Result:** PASS
+
+### 5. P3.5.5 ΓÇö Completion Marker Hang
+
+**Citation:** `ProcessRunner.RunAsync, TerminationReason.KilledAfterCompletionMarker`
+**Fixture:** Self-stub: `--stub --complete-after 100` (outputs "DONE" after 100ms, then hangs forever)
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--complete-after", "100"], CancellationToken.None, completionPattern: "DONE", completionTimeout: TimeSpan.FromMilliseconds(200))`
+**Expected:** ProcessRunner detects "DONE" in stdout, starts 200ms grace timer, kills after grace ΓåÆ `TerminationReason.KilledAfterCompletionMarker`
+**Assertions:**
+- `result.IsError` is false
+- `result.Value.TerminationReason == TerminationReason.KilledAfterCompletionMarker`
+**Orphan Check:** Grace expiry triggers `stopAndBuildAsync(KilledAfterCompletionMarker)`, `KillAndReapAsync` ensures clean termination
+**Result:** PASS
+
+### 6. P3.5.6 ΓÇö High-Volume Stdout Drain
+
+**Citation:** `ProcessRunner.RunAsync, output drain`
+**Fixture:** Self-stub: `--stub --exit 0 --output 1000` (1000 lines to stdout)
+**ProcessRunner Call:** `RunAsync(exePath, ["--stub", "--exit", "0", "--output", "1000"], CancellationToken.None)`
+**Expected:** ExitCode=0, TerminationReason=Exited, Stdout contains all 1000 lines
+**Assertions:**
+- `result.IsError` is false
+- `result.Value.ExitCode == 0`
+- `result.Value.TerminationReason == TerminationReason.Exited`
+- Stdout line count == 1000
+**Orphan Check:** Normal exit path, `DrainOutputAsync` waits for `stdoutDrainTcs` and `stderrDrainTcs` before return
+**Result:** PASS
+
+## Stub Extensions
+
+Two new modes added to `RunStubAsync`:
+
+| Mode | Effect |
+|------|--------|
+| `--stderr N` | Write N lines to `Console.Error` (stderr) before delay logic |
+| `--complete-after N` | Wait N ms, write "DONE" to stdout, then hang forever (priority over `--delay` and `--ignore-termination`) |
+
+Existing modes (`--exit`, `--output`, `--delay`, `--ignore-termination`) unaltered. All prior P3.1-P3.4 cases pass unchanged.
+
+## ProcessRunner API Coverage
+
+| TerminationReason | Case | Verified |
+|---|---|---|
+| `Exited` | P3.5.1, P3.5.2, P3.5.6 | ExitCode + stdout/stderr capture |
+| `CallerCanceled` | P3.5.3 | Pre-cancelled token ΓåÆ exception |
+| `Timeout` | P3.5.4 | 200ms timeout on 10s delay |
+| `KilledAfterCompletionMarker` | P3.5.5 | Pattern detected ΓåÆ grace kill |
+| `InactivityTimeout` | ΓÇö | No caller passes `inactivityTimeout`; latent per C-11 |
+| `StartFailed` | ProcessRunnerStartFailed (P3.1) | Pre-existing case |
+
+`InactivityTimeout` not exercised ΓÇö no production caller passes the parameter; testing it would require ProcessRunner API modification which is out of scope.
+
+## Null/Bang Audit
+
+- **0** new `null` literals introduced
+- **0** new nullable-forgiving `!` operators
+- **0** new `null!` assignments
+- `string? exePath = Environment.ProcessPath` used with `is null` guard in all nine methods (6 P3.5 + 3 legacy spawn sites)
+- 3 legacy `Environment.ProcessPath!` sites (`ChildStubIgnoreTerminationAsync`, `SpawnStubAsync`, `SpawnStubWithOutputAsync`) replaced with `string?` + `is null` guards; **0** `Environment.ProcessPath!` remain
+- Boolean negation uses prefix `!` on `bool` values (not nullable-forgiving): `!result.IsError`
+- Pattern matching: `exePath is null`, `result.IsError`
+
+## Build
+
+```
+dotnet build checks/GuardChecks.csproj ΓåÆ succeeded (0 warnings, 0 errors)
+dotnet run (clean) ΓåÆ RESULTS: 29 passed, 0 failed, 2 blocked, 31 total ΓåÆ EXIT: 0
+dotnet run -- --force-fail ΓåÆ RESULTS: 29 passed, 1 failed, 2 blocked, 32 total ΓåÆ EXIT: 1
+```
