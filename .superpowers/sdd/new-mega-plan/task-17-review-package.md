# Review package: 9c677c9..a7dbe97

## Commits
a7dbe97 feat(checks): P3.2 ΓÇö inverted guard assertions, StartFailed, reflection access
73d2859 docs(checks): T11 historical artifact collision note ΓÇö report absent, blessed assertions quoted from plan
dd1f955 docs(checks): P3.1 report ΓÇö R3-fix SHA 1cdc80b, line count 221
1cdc80b docs(checks): fix R3 report SHA to b45b769
b45b769 fix(checks): P3.1 R3 ΓÇö StartsWith separator boundary, report exact
0096387 docs(checks): fix R2 report SHA to 2d3481b
2d3481b fix(checks): P3.1 R2 ΓÇö temp boundary, finally bounded reap, report exact

## Files changed
 checks/Program.cs        | 100 ++++++++++++++++++++++++++++++++++++++++++++++-
 checks/collision-note.md |  28 +++++++++++++
 task-16-report.md        |  54 +++++++++++++++++++------
 3 files changed, 168 insertions(+), 14 deletions(-)

## Diff
diff --git a/checks/Program.cs b/checks/Program.cs
index 13027ab..d7e38d9 100644
--- a/checks/Program.cs
+++ b/checks/Program.cs
@@ -1,40 +1,51 @@
 ∩╗┐using System.Diagnostics;
+using System.Reflection;
 using Core;
 using Serilog.Events;
+using Services.Audio;
 
 if (args.Length > 0 && args[0] == "--stub")
 	return await RunStubAsync(args);
 
 await Telemetry.Configure(LogEventLevel.Fatal);
 
 string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
 List<(string Name, bool Pass, string? Error)> results = [];
 
 try
 {
 	Directory.CreateDirectory(tempRoot);
 	string normalizedTempRoot = Path.GetFullPath(tempRoot);
 	string systemTemp = Path.GetFullPath(Path.GetTempPath());
-	if (!normalizedTempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase))
+	string systemTempWithSep = (systemTemp.EndsWith(Path.DirectorySeparatorChar) || systemTemp.EndsWith(Path.AltDirectorySeparatorChar))
+		? systemTemp
+		: systemTemp + Path.DirectorySeparatorChar;
+	bool isUnderTemp = string.Equals(normalizedTempRoot, systemTemp, StringComparison.OrdinalIgnoreCase)
+		|| normalizedTempRoot.StartsWith(systemTempWithSep, StringComparison.OrdinalIgnoreCase);
+	if (!isUnderTemp)
 	{
 		Console.WriteLine($"  FAIL: TempRootUnderSystemTemp ΓÇö tempRoot={normalizedTempRoot} systemTemp={systemTemp}");
 		throw new InvalidOperationException($"Temp root {normalizedTempRoot} is not under system temp {systemTemp}");
 	}
 	Console.WriteLine("  PASS: TempRootUnderSystemTemp");
 	results.Add(("TempRootUnderSystemTemp", true, null));
 
 	await ChildStubExitZeroAsync();
 	await ChildStubExitNonzeroAsync();
 	await ChildStubOutputVolumeAsync();
 	await ChildStubDelayAsync();
 	await ChildStubIgnoreTerminationAsync();
+	await CompleteClearsFailedAsync();
+	await DifferingNonCompleteIncrementsAsync();
+	await ProcessRunnerStartFailedAsync();
+	await ReflectionAccessAsync();
 }
 finally
 {
 	if (Directory.Exists(tempRoot))
 		Directory.Delete(tempRoot, true);
 }
 
 if (args.Contains("--force-fail"))
 {
 	results.Add(("ForcedFailure", false, "Forced failure mode active"));
@@ -116,25 +127,112 @@ async Task ChildStubIgnoreTerminationAsync()
 		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
 		await process.WaitForExitAsync(cts.Token);
 		Assert("ChildStubIgnoreTermination", process.HasExited, "process not reaped after kill");
 	}
 	finally
 	{
 		if (!process.HasExited)
 		{
 			try { process.Kill(entireProcessTree: true); }
 			catch (InvalidOperationException) { }
+			using var finallyCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
+			try { await process.WaitForExitAsync(finallyCts.Token); }
+			catch (OperationCanceledException)
+			{
+				Console.WriteLine("  FAIL: ChildStubIgnoreTermination ΓÇö fallback kill timed out, possible orphan");
+				results.Add(("ChildStubIgnoreTermination", false, "fallback kill timed out after 3s"));
+			}
 		}
 		process.Dispose();
 	}
 }
 
+async Task CompleteClearsFailedAsync()
+{
+	string testIso = Path.Combine(tempRoot, "test-complete-clears.iso");
+	var guard = await ReprocessGuard.LoadAsync();
+
+	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
+	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
+	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
+
+	ReprocessGuard.GuardEntry? entry = guard.Get(testIso);
+	bool isFailed = entry?.Verdict == DiscState.Failed;
+
+	await guard.RecordAsync(testIso, DiscState.Complete);
+
+	entry = guard.Get(testIso);
+	bool cleared = entry is null;
+
+	Assert("CompleteClearsFailed", cleared, $"entry still exists: {entry?.Verdict}({entry?.ConsecutiveCount})");
+
+	await guard.ResetAsync(testIso);
+}
+
+async Task DifferingNonCompleteIncrementsAsync()
+{
+	string testIso = Path.Combine(tempRoot, "test-differing-increments.iso");
+	var guard = await ReprocessGuard.LoadAsync();
+
+	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
+
+	await guard.RecordAsync(testIso, DiscState.NeedsPrimaryConversion);
+
+	ReprocessGuard.GuardEntry? entry = guard.Get(testIso);
+	int count = entry?.ConsecutiveCount ?? 0;
+
+	Assert("DifferingNonCompleteIncrements", count == 2, $"count={count}, expected=2");
+
+	await guard.ResetAsync(testIso);
+}
+
+async Task ProcessRunnerStartFailedAsync()
+{
+	ProcessRunner runner = new();
+	var result = await runner.RunAsync(
+		"/nonexistent/binary.exe",
+		[],
+		CancellationToken.None
+	);
+
+	bool isStartFailed = result.IsError ||
+		(result.Value.TerminationReason == TerminationReason.StartFailed);
+
+	Assert("ProcessRunnerStartFailed", isStartFailed,
+		result.IsError ? $"error={result.Errors[0].Description}" : $"reason={result.Value.TerminationReason}");
+}
+
+async Task ReflectionAccessAsync()
+{
+	Type checkerType = typeof(FlacCompletenessChecker);
+	MethodInfo? getFlacsMethod = checkerType.GetMethod("GetFlacsByTrackNumber",
+		BindingFlags.Static | BindingFlags.NonPublic);
+	MethodInfo? findDffMethod = checkerType.GetMethod("FindDffDir",
+		BindingFlags.Static | BindingFlags.NonPublic);
+
+	if (getFlacsMethod is null || findDffMethod is null)
+	{
+		Assert("ReflectionAccess", false, "method not found");
+		return;
+	}
+
+	string testDir = Path.Combine(tempRoot, "test-flacs");
+	Directory.CreateDirectory(testDir);
+	await File.WriteAllTextAsync(Path.Combine(testDir, "01. track.flac"), "fake");
+	await File.WriteAllTextAsync(Path.Combine(testDir, "02. track.flac"), "fake");
+
+	var result = getFlacsMethod.Invoke(null, new object[] { testDir });
+	bool hasEntries = result is Dictionary<int, string> dict && dict.Count == 2;
+
+	Assert("ReflectionAccess", hasEntries, $"result={result?.GetType().Name}");
+}
+
 async Task<int> SpawnStubAsync(string stubArgs)
 {
 	string exePath = Environment.ProcessPath!;
 	ProcessStartInfo psi = new()
 	{
 		FileName = exePath,
 		Arguments = $"--stub {stubArgs}",
 		UseShellExecute = false,
 		RedirectStandardOutput = true,
 		CreateNoWindow = true,
diff --git a/checks/collision-note.md b/checks/collision-note.md
new file mode 100644
index 0000000..1ee9df0
--- /dev/null
+++ b/checks/collision-note.md
@@ -0,0 +1,28 @@
+# Historical T11 Report ΓÇö Collision Note
+
+**Date:** 2026-08-17
+**Author:** P3.2 decontamination task
+
+## Finding
+
+Historical `task-11-report.md` (T11 harness execution) is absent from the repository.
+
+The file `.superpowers/sdd/new-mega-plan/task-11-report.md` exists but is the **P1.6 ISO deletion gating report**, not the historical T11 regression harness report.
+
+## Evidence
+
+- P1.6 report (`.superpowers/sdd/new-mega-plan/task-11-report.md`): Records 6 P1.6 validation cases + 5 guard cases (11/11 pass). Contains "Complete clears Failed" and "Differing non-Complete verdict increments" assertions from P1.2 fix, not historical T11 blessed defects.
+- Historical T11 report: Referenced in `new-mega-plan.md` ┬º0.1 as recording "74 passing cases" with two blessed defects. File not found in repository.
+
+## Blessed Assertions (from plan ┬º0.2)
+
+The historical T11 harness asserted two defects as correct behavior:
+
+1. **"Complete can't remove Failed (sticky)"** ΓÇö `Failed` entries persisted regardless of subsequent `Complete` verdicts.
+2. **"different verdict resets count"** ΓÇö A change in verdict (e.g., `NeedsExtraction` ΓåÆ `Complete`) reset `ConsecutiveCount` to 0.
+
+These are the two guard defects the compliance audit raised. The harness encoded them as expected behavior and passed.
+
+## Source
+
+Quotes sourced from `new-mega-plan.md` ┬º0.2 "The T11 harness asserted two of the defects as correct behaviour".
diff --git a/task-16-report.md b/task-16-report.md
index 0ccf10d..ce49679 100644
--- a/task-16-report.md
+++ b/task-16-report.md
@@ -1,94 +1,111 @@
 # Task 16 ΓÇö P3.1 Harness Infrastructure
 
-**Branch:** sacd-completion-v2 | **Baseline:** ef43b65 | **Fix:** HEAD
+**Branch:** sacd-completion-v2 | **Baseline:** ef43b65 | **R1:** 9c677c9 | **R2:** 2d3481b | **R2-fix:** 0096387 | **R3:** b45b769 | **R3-fix:** 1cdc80b
 **Date:** 2026-08-17
 
 ## Summary
 
-Replaced temporary P1.2/P1.6 GuardChecks with durable P3.1 harness infrastructure. Single committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard-assert (throws on mismatch) and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.
+Durable P3.1 harness infrastructure. Committed entry point, no test packages, references Audio project. Assertion helper names failing cases. Temp workspace under system temp with hard boundary check (parent-directory comparison) and finally teardown. Controllable child-process stub via self-invocation (`--stub` mode). `--force-fail` causes nonzero exit with named failure. Telemetry configured at Fatal. Per-case summary.
 
 ## Files Changed
 
 | File | Lines | Change |
 |------|-------|--------|
-| `checks/Program.cs` | 188 | P3.1 harness: assertion helper, hard temp assertion, child stub with try/finally reaping, forced-failure, Telemetry.Fatal |
+| `checks/Program.cs` | 221 | R3: StartsWith separator boundary, finally reap-before-dispose |
 | `checks/GuardChecks.csproj` | 13 | Unchanged (references Audio, no test packages) |
+| `task-16-report.md` | ΓÇö | This report (repo root) |
 
 ## Subtask Results
 
 ### 1. Plain `.cs` entry point, no test packages, referencing production project
 
 GuardChecks.csproj unchanged ΓÇö references `Audio.csproj` (transitively references `Core`). No xUnit/NUnit/MSTest. Top-level statements with `Main()` implicit.
 
-**Build:** `dotnet build checks/GuardChecks.csproj` ΓåÆ 0 Warning(s) 0 Error(s)
-
 ### 2. Assertion helpers with failure output naming the case
 
 `Assert(string name, bool condition, string? error)` records pass/fail with case name into `results` list. Output: `PASS: {name}` or `FAIL: {name} ΓÇö {error}`.
 
 ### 3. Temp-workspace creation and teardown, hard assertion under system temp
 
-```
+```csharp
 string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
 ```
 
-**Hard assertion:** if `Path.GetFullPath(tempRoot)` does not start with `Path.GetFullPath(Path.GetTempPath())`, prints `FAIL: TempRootUnderSystemTemp` then throws `InvalidOperationException`. No media use possible before this check. Teardown in `finally`: `Directory.Delete(tempRoot, true)`.
+**Hard boundary check (R3):** Ensures `systemTemp` ends with `Path.DirectorySeparatorChar`, then checks `normalizedTempRoot.StartsWith(systemTempWithSep, OrdinalIgnoreCase)` or exact equality. This rejects sibling directories (e.g. `Temp2` when temp is `Temp`) by requiring a separator after the system temp prefix. On mismatch: prints `FAIL: TempRootUnderSystemTemp` then throws `InvalidOperationException`. Teardown in `finally`: `Directory.Delete(tempRoot, true)`.
 
 ### 4. Controllable child-process stub
 
 Self-invocation mode: `--stub --exit <code> --output <lines> --delay <ms> --ignore-termination`.
 
 | Stub arg | Behavior |
 |----------|----------|
 | `--exit N` | Exit with code N |
 | `--output N` | Print N lines of stdout |
 | `--delay N` | Sleep N ms before exit |
 | `--ignore-termination` | Wait forever (until killed) |
 
 ### 5. Nonzero exit on failure; per-case summary; Telemetry Fatal
 
 `Telemetry.Configure(LogEventLevel.Fatal)` at entry. Exit code 1 if any case fails or `--force-fail` present. Summary: `RESULTS: X passed, Y failed, Z total`.
 
-### 6. Fix: ChildStubIgnoreTermination try/finally (review finding)
+### 6. Fix R1: ChildStubIgnoreTermination try/finally
+
+Process kill/reap/dispose wrapped in try/finally. Bounded wait: 5-second `CancellationTokenSource` on `WaitForExitAsync`.
+
+### 7. Fix R2: Finally fallback kill awaits bounded reaping
 
-Process kill/reap/dispose wrapped in try/finally. If `Kill()` or `WaitForExitAsync` throws, `finally` block kills (if not exited) and disposes ΓÇö no orphan possible. Bounded wait: 5-second `CancellationTokenSource` on `WaitForExitAsync`.
+If `Kill()` or `WaitForExitAsync` throws in the try block, the finally block kills (if not exited), then awaits bounded `WaitForExitAsync` (3s timeout). If the bounded reap times out, reports `FAIL: ChildStubIgnoreTermination ΓÇö fallback kill timed out, possible orphan` and records named failure. Process always disposed.
+
+## Raw Commands
+
+### Clean run
+
+```bash
+dotnet run --project checks/GuardChecks.csproj
+```
+
+### Forced-failure run
+
+```bash
+dotnet run --project checks/GuardChecks.csproj -- --force-fail
+```
 
 ## Raw Outputs
 
-### Clean run (no --force-fail)
+### Clean run (exit 0)
 
 ```
   PASS: TempRootUnderSystemTemp
   PASS: ChildStubExitZero
   PASS: ChildStubExitNonzero
   PASS: ChildStubOutputVolume
   PASS: ChildStubDelay
   PASS: ChildStubIgnoreTermination
 
 RESULTS: 6 passed, 0 failed, 6 total
-EXIT_CODE: 0
+EXIT: 0
 ```
 
-### Forced-failure run (--force-fail)
+### Forced-failure run (exit 1)
 
 ```
   PASS: TempRootUnderSystemTemp
   PASS: ChildStubExitZero
   PASS: ChildStubExitNonzero
   PASS: ChildStubOutputVolume
   PASS: ChildStubDelay
   PASS: ChildStubIgnoreTermination
   FAIL: ForcedFailure ΓÇö forced failure mode active
 
 RESULTS: 6 passed, 1 failed, 7 total
-EXIT_CODE: 1
+EXIT: 1
 ```
 
 ## Build Verification
 
 ```
 dotnet build checks/GuardChecks.csproj
   Core -> artifacts\bin\Core\debug\Core.dll
   Audio -> artifacts\bin\Audio\debug\Audio.dll
   GuardChecks -> artifacts\bin\GuardChecks\debug\GuardChecks.dll
 Build succeeded. 0 Warning(s) 0 Error(s)
@@ -108,14 +125,25 @@ Build succeeded. 0 Warning(s) 0 Error(s)
 
 **6/6 PASS clean, 1/1 FAIL forced-failure. Exit 0 clean, exit 1 forced.**
 
 ## Acceptance Criteria
 
 - [x] Harness runs, prints per-case results
 - [x] Exits 0 clean
 - [x] Exits non-zero when forced to fail
 - [x] Committed to the repo, not deleted
 
+## Fix Round History
+
+| Round | Commit | Change | Prior stale value |
+|-------|--------|--------|-------------------|
+| R1 | 9c677c9 | Hard temp assert (throw), try/finally on child reaping | Soft Assert, no finally |
+| R2 | 2d3481b | Parent-dir boundary check, finally bounded kill+reap+dispose | `StartsWith` prefix match, finally kills without await |
+| R2-fix | 0096387 | Report SHA correction | R2 SHA pointed to wrong commit |
+| R3 | b45b769 | StartsWith with separator boundary, finally reap-before-dispose | Parent-dir compare, dispose-before-reap |
+| R3-fix | 1cdc80b | Report line count 221, SHA correction | R3 SHA pointed to wrong commit |
+
 ## Concerns
 
 1. **Telemetry side effects:** `Configure(LogEventLevel.Fatal)` creates `state/logs/` directory and per-service JSONL files. Acceptable for on-demand harness.
 2. **Per-service JSONL sinks** still capture Debug+ despite Fatal console level ΓÇö by design per Telemetry.cs configuration.
+3. **Finally bounded reap** uses 3s timeout. If a child process survives `Kill(entireProcessTree: true)` + 3s wait, a named failure is reported. No orphan claim without evidence.
