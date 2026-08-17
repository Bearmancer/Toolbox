# Review package: f814654..de94892

## Commits
de94892 fix(audio): P1.1 fresh-disc crash remediation

## Files changed
 .superpowers/sdd/new-mega-plan/task-6-report.md | 143 ++++++++++++++++++++++++
 src/Services/Audio/PipelineOrchestrator.cs      |  64 +++++++----
 2 files changed, 185 insertions(+), 22 deletions(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-6-report.md b/.superpowers/sdd/new-mega-plan/task-6-report.md
new file mode 100644
index 0000000..4b2cc4b
--- /dev/null
+++ b/.superpowers/sdd/new-mega-plan/task-6-report.md
@@ -0,0 +1,143 @@
+# Task 6 ΓÇö P1.1 Fresh-disc Crash Remediation
+
+**Branch:** sacd-completion-v2 | **Base:** f814654 | **Date:** 2026-08-16
+
+## Subtask 1: Failing standalone check (TDD RED)
+
+**Command:**
+```bash
+dotnet new console --name GuardVerify --output $env:TEMP\sacd-guard-verify
+# Wrote Program.cs calling Directory.GetFiles on non-existent path
+dotnet run --project $env:TEMP\sacd-guard-verify
+```
+
+**Raw output:**
+```
+RED CONFIRMED: Directory.GetFiles throws on non-existent dir
+Guard test result: PASS (exception thrown as expected)
+GREEN: Directory.Exists guard correctly returns false for non-existent dir
+Overall: PASS
+```
+
+**Result:** PASS ΓÇö `Directory.GetFiles` on non-existent path throws `DirectoryNotFoundException`, confirming the crash vector. `Directory.Exists` correctly returns `false`.
+
+## Subtask 2: DeleteFlacsInDir guard
+
+**Diff (PipelineOrchestrator.cs):**
+```diff
+ 	private static void DeleteFlacsInDir(string dir)
+ 	{
++		if (!Directory.Exists(dir))
++			return;
++
+ 		foreach (var flac in Directory.GetFiles(dir, "*.flac"))
+```
+
+**Result:** PASS ΓÇö Existence guard added as first statement. Non-existent dir returns early without throwing.
+
+## Subtask 3: Per-disc exception boundary in RunAsync
+
+**Diff (PipelineOrchestrator.cs):**
+```diff
+-			ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
+-				iso,
+-				format,
+-				multichannel,
+-				guard,
+-				ct
+-			);
+-			if (result.IsError)
+-			{
+-				failed++;
+-				// ... error handling ...
+-			}
+-			else
+-			{
+-				succeededDiscs.Add(result.Value);
+-				succeeded++;
+-			}
++			try
++			{
++				ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
++					iso,
++					format,
++					multichannel,
++					guard,
++					ct
++				);
++				if (result.IsError)
++				{
++					failed++;
++					// ... error handling unchanged ...
++				}
++				else
++				{
++					succeededDiscs.Add(result.Value);
++					succeeded++;
++				}
++			}
++			catch (OperationCanceledException)
++			{
++				throw;
++			}
++			catch (Exception ex)
++			{
++				failed++;
++				Telemetry.Error(
++					"ISO unexpected exception: iso={Iso} error={Error}",
++					LogPaths.Format(iso),
++					ex.Message
++				);
++				recoverableErrors.Add(ex.Message);
++			}
+```
+
+**Result:** PASS ΓÇö Unexpected exceptions caught per-disc, logged via Telemetry.Error, batch continues. `OperationCanceledException` rethrown without conversion to recoverable failure.
+
+## Subtask 4: OperationCanceledException propagation
+
+**Verification:** Code inspection confirms `catch (OperationCanceledException) { throw; }` precedes the general `catch (Exception ex)`. Ctrl+C token cancellation propagates correctly.
+
+**Result:** PASS
+
+## Subtask 5: Full solution build
+
+**Command:** `dotnet build`
+
+**Output:**
+```
+Build succeeded.
+    0 Warning(s)
+    0 Error(s)
+```
+
+**Result:** PASS
+
+## Directory enumeration audit ΓÇö `src/Services/Audio/`
+
+| File | Line | Call | Guard/Disposition |
+|------|------|------|-------------------|
+| **PipelineOrchestrator.cs** | 142 | `Directory.GetFiles(validatedPath, "*.iso", AllDirectories)` | `File.GetAttributes` check on line 140 ΓÇö only called when path is validated directory |
+| **PipelineOrchestrator.cs** | 310 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | `DeleteStaleDff` ΓÇö guarded by `Directory.Exists(dffDir)` on line 307 |
+| **PipelineOrchestrator.cs** | 332 | `Directory.GetFiles(dir, "*.flac")` | **FIXED** ΓÇö `DeleteFlacsInDir` now guarded by `Directory.Exists(dir)` |
+| **PipelineOrchestrator.cs** | 357 | `Directory.GetFiles(dffDir, "*.cue")` | `ConvertDiscAsync` ΓÇö guarded by `Directory.Exists(dffDir)` on line 356 |
+| **PipelineOrchestrator.cs** | 368 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | `ConvertDiscAsync` ΓÇö guarded by `Directory.Exists(dffDir)` on line 367 |
+| **PipelineOrchestrator.cs** | 436-437 | `Directory.GetFiles(outputDir, "*.dff" / "*.xml", AllDirectories)` | `CleanupSuccesses` ΓÇö guarded by `Directory.Exists(outputDir)` on line 429 |
+| **DiscOutputInspector.cs** | 29 | `Directory.GetFiles(dffDir, "*.cue")` | Guarded by `Directory.Exists(dffDir)` on line 28 |
+| **DiscOutputInspector.cs** | 48 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | Guarded by `Directory.Exists(dffDir)` on line 47 |
+| **DiscOutputInspector.cs** | 81 | `Directory.GetFiles(dffDir, "*.flac")` | Guarded by `Directory.Exists(dffDir)` on line 80 |
+| **SacdExtractService.cs** | 79 | `Directory.GetDirectories(outputDir)` | Called only after `Directory.CreateDirectory(outputDir)` on line 77 |
+| **SacdExtractService.cs** | 97 | `Directory.GetDirectories(outputDir)` | Same `outputDir` that was just created |
+| **SacdExtractService.cs** | 102 | `Directory.GetFiles(outputDir, "*.dff", AllDirectories)` | Same `outputDir` that was just created |
+| **FlacCompletenessChecker.cs** | 106 | `Directory.GetFiles(dir, "*.flac", TopDirectoryOnly)` | `GetFlacsByTrackNumber` ΓÇö guarded by `Directory.Exists(dir)` on line 103 |
+| **FlacCompletenessChecker.cs** | 124 | `Directory.GetFiles(channelDir, "*.dff", AllDirectories)` | `FindDffDir` ΓÇö guarded by `Directory.Exists(channelDir)` on line 122 |
+
+**Conclusion:** Every `Directory.GetFiles`/`GetDirectories` call in Audio is guarded by `Directory.Exists` or preceded by `Directory.CreateDirectory`. `DeleteFlacsInDir` was the sole exception ΓÇö now fixed.
+
+## Changed files
+
+- `src/Services/Audio/PipelineOrchestrator.cs` (2 hunks: guard + exception boundary)
+
+## Concerns
+
+- None. Changes are minimal and scoped.
diff --git a/src/Services/Audio/PipelineOrchestrator.cs b/src/Services/Audio/PipelineOrchestrator.cs
index 1b6bf07..84e6c6f 100644
--- a/src/Services/Audio/PipelineOrchestrator.cs
+++ b/src/Services/Audio/PipelineOrchestrator.cs
@@ -89,47 +89,64 @@ public sealed class PipelineOrchestrator(
 				{
 					failed++;
 					guardFailedDiscs.Add(iso);
 					Telemetry.Warn(
 						"Guard: {Disc} is Failed ΓÇö skipping",
 						LogPaths.Format(iso)
 					);
 					continue;
 				}
 
-				ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
-					iso,
-					format,
-					multichannel,
-					guard,
-					ct
-				);
-				if (result.IsError)
+				try
 				{
-					failed++;
-					if (result.Errors.Any(error => error.Code == "Audio.GuardBlocked"))
-						guardFailedDiscs.Add(iso);
-
-					foreach (Error error in result.Errors)
+					ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
+						iso,
+						format,
+						multichannel,
+						guard,
+						ct
+					);
+					if (result.IsError)
 					{
-						Telemetry.Error(
-							"ISO failed: iso={Iso} error={Error}",
-							LogPaths.Format(iso),
-							error.Description
-						);
-						recoverableErrors.Add(error.Description);
+						failed++;
+						if (result.Errors.Any(error => error.Code == "Audio.GuardBlocked"))
+							guardFailedDiscs.Add(iso);
+
+						foreach (Error error in result.Errors)
+						{
+							Telemetry.Error(
+								"ISO failed: iso={Iso} error={Error}",
+								LogPaths.Format(iso),
+								error.Description
+							);
+							recoverableErrors.Add(error.Description);
+						}
+					}
+					else
+					{
+						succeededDiscs.Add(result.Value);
+						succeeded++;
 					}
 				}
-				else
+				catch (OperationCanceledException)
 				{
-					succeededDiscs.Add(result.Value);
-					succeeded++;
+					throw;
+				}
+				catch (Exception ex)
+				{
+					failed++;
+					Telemetry.Error(
+						"ISO unexpected exception: iso={Iso} error={Error}",
+						LogPaths.Format(iso),
+						ex.Message
+					);
+					recoverableErrors.Add(ex.Message);
 				}
 			}
 
 			CleanupSuccesses(succeededDiscs, keepIso);
 			return new PipelineResult(succeeded, failed, recoverableErrors, guardFailedDiscs);
 		}
 		finally
 		{
 			LogPaths.Reset();
 		}
@@ -322,20 +339,23 @@ public sealed class PipelineOrchestrator(
 					ex.Message
 				);
 			}
 		}
 	}
 
 	private static void DeletePartialFlacs(string dffDir) => DeleteFlacsInDir(dffDir);
 
 	private static void DeleteFlacsInDir(string dir)
 	{
+		if (!Directory.Exists(dir))
+			return;
+
 		foreach (var flac in Directory.GetFiles(dir, "*.flac"))
 		{
 			try
 			{
 				Telemetry.Info("Pipeline.ResplitFlacDeleted file={File}", LogPaths.Format(flac));
 				File.Delete(flac);
 			}
 			catch (Exception ex)
 			{
 				Telemetry.Warn(
