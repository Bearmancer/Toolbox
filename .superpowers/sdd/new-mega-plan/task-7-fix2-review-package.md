# Review package: 384da2f..f64b9af

## Commits
f64b9af fix(audio): P1.2 SaveAsync cancellation check before File.Move

## Files changed
 .superpowers/sdd/new-mega-plan/task-7-report.md | 31 ++++++++++++++++++++++++-
 src/Services/Audio/ReprocessGuard.cs            |  1 +
 2 files changed, 31 insertions(+), 1 deletion(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-7-report.md b/.superpowers/sdd/new-mega-plan/task-7-report.md
index 0775893..6ae0c1e 100644
--- a/.superpowers/sdd/new-mega-plan/task-7-report.md
+++ b/.superpowers/sdd/new-mega-plan/task-7-report.md
@@ -187,16 +187,45 @@ Build succeeded.
 
 ## Changes (round 2)
 
 | File | Change |
 |---|---|
 | `ReprocessGuard.cs` | `>` ΓåÆ `>=` threshold; Warn log on every transition (ISO/prev/new/count); per-entry reset logging; `CancellationToken` on RecordAsync/ResetAsync/ResetAllAsync/SaveAsync |
 | `PipelineOrchestrator.cs` | Pass `ct` to all `RecordAsync` calls (CA2016) |
 | `SacdConvertCommand.cs` | Pass `cancellationToken` to `ResetAllAsync` (CA2016) |
 | `checks/Program.cs` | Test 3: verify Failed after 3 records (not after 4); Test 4: alternating verdicts terminate |
 
+## Changes (round 3) ΓÇö SaveAsync cancellation check
+
+**Reviewer finding (Critical):** `File.Move` can execute after cancellation requested because no final `ThrowIfCancellationRequested` between stream close and atomic move.
+
+**Fix:** Added `ct.ThrowIfCancellationRequested()` immediately after `stream.Close()` and before `File.Move(tempPath, StatePath, overwrite: true)`.
+
+**Diff:**
+```diff
+ 			await stream.FlushAsync(ct);
+ 			stream.Close();
++			ct.ThrowIfCancellationRequested();
+ 			File.Move(tempPath, StatePath, overwrite: true);
+```
+
+**Cancellation race after fix:** No deliberate `File.Move` after an observed cancellation. A cancellation arriving *during* `File.Move` (between OS kernel entry and completion) remains an OS atomic-operation boundary ΓÇö the move either completes or doesn't, and the original file is untouched either way. This race is documented as BLOCKED/benign; it cannot be eliminated in userspace and does not corrupt state.
+
+**Raw output:**
+```
+Test 1: Complete clears Failed... PASS
+Test 2: Differing non-Complete verdict increments... PASS
+Test 3: N=3 refuses attempt 4... PASS
+Test 4: Alternating verdicts terminate... PASS
+Test 5: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)
+
+ALL CHECKS PASSED
+```
+
+**Status:** PASS
+
 ## Concerns
 
 1. **Pipeline-level threshold proof BLOCKED:** Direct `RecordAsync` tests verify guard state; they do not prove orchestrator refuses 4th process start. P3.2 harness required.
 2. **CLI help BLOCKED:** No `.env` or binaries in worktree environment. Startup throws before parser.
 3. **Atomic interruption BLOCKED:** Cannot simulate in unit test.
-4. **Benign cancellation race:** `.tmp` persists if cancelled between flush and move; original file untouched. Acceptable.
+4. **OS-level cancellation race (benign):** Cancellation during `File.Move` is an OS atomic-operation boundary. Move either completes or doesn't; original file untouched. Cannot be eliminated in userspace. Documented, not claimed impossible.
diff --git a/src/Services/Audio/ReprocessGuard.cs b/src/Services/Audio/ReprocessGuard.cs
index dd79142..16336e8 100644
--- a/src/Services/Audio/ReprocessGuard.cs
+++ b/src/Services/Audio/ReprocessGuard.cs
@@ -122,20 +122,21 @@ public sealed class ReprocessGuard
 	{
 		Directory.CreateDirectory(PathResolver.GetStatePath("audio"));
 
 		var tempPath = StatePath + ".tmp";
 		try
 		{
 			await using FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
 			await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions, ct);
 			await stream.FlushAsync(ct);
 			stream.Close();
+			ct.ThrowIfCancellationRequested();
 			File.Move(tempPath, StatePath, overwrite: true);
 		}
 		catch (IOException ex)
 		{
 			Telemetry.Error("Failed to save SACD guard to {Path}: {Error}", StatePath, ex.Message);
 			throw;
 		}
 		catch (UnauthorizedAccessException ex)
 		{
 			Telemetry.Error(
