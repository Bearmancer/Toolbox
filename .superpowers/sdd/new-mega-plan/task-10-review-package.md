# Review package: ca7573e..d1ade80

## Commits
d1ade80 fix(audio): P1.5 verify sox split/derive output exists and is non-zero

## Files changed
 .superpowers/sdd/new-mega-plan/task-10-report.md | 110 +++++++++++++++++++++++
 src/Services/Audio/SoxService.cs                 |  12 +++
 2 files changed, 122 insertions(+)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-10-report.md b/.superpowers/sdd/new-mega-plan/task-10-report.md
new file mode 100644
index 0000000..0b15556
--- /dev/null
+++ b/.superpowers/sdd/new-mega-plan/task-10-report.md
@@ -0,0 +1,110 @@
+# P1.5 ΓÇö Split output verification ΓÇö Report
+
+**Branch:** sacd-completion-v2 | **Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
+**Date:** 2026-08-16 | **Status:** PASS (source + runtime stub), Disc 3 real split BLOCKED
+
+## Summary
+
+`SoxService.SplitTrackAsync` and `SoxService.DeriveFlacAsync` returned the output path on exit code 0 alone, without confirming the file existed or was non-empty. Both now verify existence and non-zero length immediately after the exit-code check and return a descriptive `ConversionFailed` naming the expected output path. `SaraconService.RunConversionAsync` already verifies output existence, structure, and non-zero data ΓÇö no change. All other path-returning methods route through these verified paths. Runtime stub check (sox exits 0 writing nothing / empty file) produces the expected error ΓÇö acceptance criterion met.
+
+## Subtask 1 ΓÇö SoxService.SplitTrackAsync
+
+**Command:** `dotnet build Toolbox.slnx --no-restore --no-incremental` ΓåÆ 0 warnings, 0 errors.
+
+**Diff:**
+```diff
+ 		if (result.Value.ExitCode != 0)
+ 			return Errors.Audio.ConversionFailed(
+ 				sourcePcm,
+ 				$"sox split exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
+ 			);
+ 
++		if (!File.Exists(outputFlac) || new FileInfo(outputFlac).Length == 0)
++			return Errors.Audio.ConversionFailed(
++				outputFlac,
++				"sox split exited 0 but produced no output file"
++			);
++
+ 		return outputFlac;
+ 	}
+```
+**Result: PASS** (source + runtime stub, see Subtask 5).
+
+## Subtask 2 ΓÇö SoxService.DeriveFlacAsync
+
+**Command:** same build ΓåÆ 0 warnings, 0 errors.
+
+**Diff:**
+```diff
+ 		if (result.Value.ExitCode != 0)
+ 			return Errors.Audio.ConversionFailed(
+ 				sourceFlac,
+ 				$"sox derive exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
+ 			);
+ 
++		if (!File.Exists(outputFlac) || new FileInfo(outputFlac).Length == 0)
++			return Errors.Audio.ConversionFailed(
++				outputFlac,
++				"sox derive exited 0 but produced no output file"
++			);
++
+ 		return outputFlac;
+ 	}
+```
+**Result: PASS** (source + runtime stub, see Subtask 5).
+
+## Subtask 3 ΓÇö SaraconService.RunConversionAsync (ConvertDsdToFlacAsync path)
+
+**Audit:** `RunConversionAsync` (shared by `ConvertDsdToPcmAsync` and `ConvertDsdToFlacAsync`) already:
+- checks `File.Exists(expectedOutput)` with `-d2p` variant fallback (L173-197),
+- validates structure via `IsExpectedOutput` (FLAC `fLaC` magic / WAV RIFF+fmt+data) (L200),
+- rejects output smaller than half the estimated PCM bytes (L212).
+
+`ConvertDsdToFlacAsync` cannot return an unverified path ΓÇö it routes through `RunConversionAsync`. **No change required.**
+
+**Result: PASS** (audit only).
+
+## Subtask 4 ΓÇö Audit of every path-returning method
+
+| Method | Returns | Verified? | Disposition |
+|---|---|---|---|
+| `SoxService.SplitTrackAsync` | `outputFlac` | now existence + nonzero | **FIXED** |
+| `SoxService.DeriveFlacAsync` | `outputFlac` | now existence + nonzero | **FIXED** |
+| `SaraconService.RunConversionAsync` | `expectedOutput` | existence + structure + nonzero | already verified |
+| `SaraconService.ConvertDsdToPcmAsync` | via `RunConversionAsync` | verified | no change |
+| `SaraconService.ConvertDsdToFlacAsync` | via `RunConversionAsync` | verified | no change |
+| `DsdConvertService.ConvertAndSplitAsync` | `outputFiles` (list) | each entry from verified `SplitTrackAsync`; count check counts verified entries | no change |
+| `DsdConvertService.ConvertFullDffAsync` | `ConversionResult` | saracon verified + sox duration probe + `FileInfo.Length` | no change |
+| `DsdConvertService.DeriveFlacAsync` | `ConversionResult` | sox `DeriveFlacAsync` (now verified) + sox duration probe | no change |
+
+Cancellation preserved ΓÇö no `CancellationToken` handling altered; checks are synchronous file probes after process exit.
+
+**Result: PASS** (audit).
+
+## Subtask 5 ΓÇö Runtime stub check (acceptance criterion)
+
+**Command:** temporary `checks/StubSox` (exits 0; optionally writes empty file at `STUB_OUTPUT_PATH` when `STUB_CREATE_EMPTY=1`) + `checks/SoxVerify` referencing `Audio.csproj`, driving `SoxService` with a real `ProcessRunner` pointed at the stub exe.
+
+**Raw output:**
+```
+Split: stub exits 0, no output -> error... PASS
+Split: stub exits 0, empty file -> error... PASS
+Derive: stub exits 0, no output -> error... PASS
+
+ALL CHECKS PASSED
+```
+
+**Result: PASS.** Acceptance criterion "stub exiting 0 writing nothing produces error" verified at runtime. Temp check projects removed after verification (not committed).
+
+## Subtask 6 ΓÇö Real Disc 3 split
+
+**Result: BLOCKED.** No ISO/harness in this worktree to run a real Disc 3 split end-to-end. Requires the durable P3.4/P3.5 harness (owner: P3.4/P3.5) to exercise the full pipeline against a real disc. Source-level verification (build clean) + runtime stub (Subtask 5) cover the new guard; the real-disc path is unchanged behaviorally for successful splits.
+
+## Concerns
+
+- None blocking. The new guard is a synchronous `File.Exists` + `FileInfo.Length` probe after process exit; negligible cost, no cancellation impact.
+- `ConvertAndSplitAsync`'s count check now counts only verified FLACs (since `SplitTrackAsync` errors on missing/empty output), so a silent zero-byte split can no longer be counted as success.
+
+## Commit
+
+`git commit` of `src/Services/Audio/SoxService.cs` + this report. Temp check projects removed.
diff --git a/src/Services/Audio/SoxService.cs b/src/Services/Audio/SoxService.cs
index 31f0580..832626a 100644
--- a/src/Services/Audio/SoxService.cs
+++ b/src/Services/Audio/SoxService.cs
@@ -28,20 +28,26 @@ public sealed class SoxService(ProcessRunner processRunner, string binaryPath)
 		ErrorOr<ProcessResult> result = await processRunner.RunAsync(binaryPath, [.. args], ct);
 		if (result.IsError)
 			return result.Errors;
 
 		if (result.Value.ExitCode != 0)
 			return Errors.Audio.ConversionFailed(
 				sourcePcm,
 				$"sox split exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
 			);
 
+		if (!File.Exists(outputFlac) || new FileInfo(outputFlac).Length == 0)
+			return Errors.Audio.ConversionFailed(
+				outputFlac,
+				"sox split exited 0 but produced no output file"
+			);
+
 		return outputFlac;
 	}
 
 	public async Task<ErrorOr<double>> GetPeakLevelAsync(
 		string filePath,
 		CancellationToken ct = default
 	)
 	{
 		Telemetry.Debug("Sox.StatsStart file={File}", Path.GetFileName(filePath));
 
@@ -127,16 +133,22 @@ public sealed class SoxService(ProcessRunner processRunner, string binaryPath)
 
 		if (result.IsError)
 			return result.Errors;
 
 		if (result.Value.ExitCode != 0)
 			return Errors.Audio.ConversionFailed(
 				sourceFlac,
 				$"sox derive exit code {result.Value.ExitCode}: {result.Value.Stderr[..Math.Min(result.Value.Stderr.Length, 500)]}"
 			);
 
+		if (!File.Exists(outputFlac) || new FileInfo(outputFlac).Length == 0)
+			return Errors.Audio.ConversionFailed(
+				outputFlac,
+				"sox derive exited 0 but produced no output file"
+			);
+
 		return outputFlac;
 	}
 
 	private static string FormatSeconds(TimeSpan t) =>
 		t.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
 }
