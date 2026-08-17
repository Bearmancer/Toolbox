# Review package: 72a3245..384da2f

## Commits
384da2f fix(audio): P1.2 review round 1 ΓÇö threshold, logging, cancellation

## Files changed
 .superpowers/sdd/new-mega-plan/task-7-report.md | 233 ++++++++----------------
 src/CLI/Audio/SacdConvertCommand.cs             |   2 +-
 src/Services/Audio/PipelineOrchestrator.cs      |  16 +-
 src/Services/Audio/ReprocessGuard.cs            |  77 +++++---
 4 files changed, 135 insertions(+), 193 deletions(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-7-report.md b/.superpowers/sdd/new-mega-plan/task-7-report.md
index 70a97f9..0775893 100644
--- a/.superpowers/sdd/new-mega-plan/task-7-report.md
+++ b/.superpowers/sdd/new-mega-plan/task-7-report.md
@@ -1,289 +1,202 @@
-# P1.2 Task 7 Report ΓÇö Reprocess Guard Semantics
+# P1.2 Task 7 Report ΓÇö Reprocess Guard Semantics (Round 2)
 
 ## Subtask 1: Record reversal rationale for stickiness
 
-**Command:** N/A (documentation only)
-
-**Rationale:** The `task-10.2-report.md` artifact is absent from the worktree. The plan preserves the following historical evidence:
+**Rationale (plan-preserved historical evidence):**
 
 > **Decision to reverse (T10.2):** `Failed` is sticky until manual JSON removal.
 
-Source: `new-mega-plan.md` lines 35, 160.
-
-The plan further documents:
-
-> `task-10.2-report.md`: *"`Failed` remains sticky until JSON removal."* Deliberate, and manual JSON deletion is the intended recovery path.
+Source: `new-mega-plan.md` lines 35, 160. `task-10.2-report.md` artifact absent from worktree.
 
-Source: `new-mega-plan.md` line 35.
+**Reversal:** `Failed` clearable by genuine `Complete` outcome (subtask 6). Manual JSON deletion replaced by `--reset-guard` (subtask 7).
 
-**Reversal:** `Failed` is now clearable by a genuine `Complete` outcome (subtask 6). Manual JSON deletion is replaced by `--reset-guard` (subtask 7).
-
-**Status:** PASS (rationale recorded)
+**Status:** PASS
 
 ## Subtask 2: Record re-scoping rationale for off-by-one
 
-**Command:** N/A (documentation only)
-
-**Rationale:** The `task-10.3-report.md` artifact is absent from the worktree. The plan preserves:
+**Rationale (plan-preserved historical evidence):**
 
 > **Decision to re-scope (T10.3 finding #2):** transition fires before the Nth attempt, so N=3 yields two attempts.
 
-Source: `new-mega-plan.md` line 162.
-
-The plan further documents:
+Source: `new-mega-plan.md` line 162. `task-10.3-report.md` artifact absent.
 
 > `task-10.3-report.md` review finding #2, severity Important: *"Transition must happen before processing"* ΓåÆ implemented as `c + 1 >= MaxConsecutiveCount` blocking before `ProbeAsync`. A reviewer asked for this.
 
 Source: `new-mega-plan.md` line 36.
 
-**Re-scoping:** The pre-Nth block in `PipelineOrchestrator.ProcessIsoAsync` is removed. The guard now transitions to `Failed` only when `newCount > MaxConsecutiveCount` (i.e., after N+1 non-Complete records). For N=3: attempts 1ΓÇô3 run, attempt 4 is refused. The reviewer's requirement ΓÇö *"a `Failed` disc starts no process"* ΓÇö remains satisfied: the `Failed` early return at `ProcessIsoAsync` line 176ΓÇô181 is preserved.
+**Re-scoping (round 2 fix):** Guard now transitions to `Failed` when `newCount >= MaxConsecutiveCount` (i.e., after N records). For N=3: records 1ΓÇô2 produce counts 1ΓÇô2 (not Failed); record 3 produces count 3 ΓëÑ 3 ΓåÆ `Failed`. The orchestrator's `Failed` early return (line 177ΓÇô181) then refuses the 4th invocation before `ProbeAsync`. Reviewer requirement ΓÇö *"a `Failed` disc starts no process"* ΓÇö remains satisfied.
 
-**Status:** PASS (rationale recorded, requirement confirmed)
+**Status:** PASS
 
 ## Subtask 3: Success paths record cycle outcome
 
-**Files changed:** `PipelineOrchestrator.cs`
-
-**Diff:**
-```diff
--			await guard.RecordAsync(isoPath, assessment.State);
-+			await guard.RecordAsync(isoPath, DiscState.Complete);
-```
+**Files:** `PipelineOrchestrator.cs`
 
-Applied at two success return paths (after successful conversion at line 245, after successful extraction+conversion at line 299). The `assessment.State == Complete` path at line 208 already recorded `DiscState.Complete`.
-
-**Raw output:** Build succeeded. 0 Warning(s). 0 Error(s).
+Success paths at lines 246 and 300 now record `DiscState.Complete` instead of `assessment.State`. The `assessment.State == Complete` path at line 208 already recorded `DiscState.Complete`.
 
 **Status:** PASS
 
 ## Subtask 4: Count consecutive non-Complete regardless of verdict
 
-**Files changed:** `ReprocessGuard.cs`
-
-**Diff:**
-```diff
--		var count = existing?.Verdict == verdict ? existing.ConsecutiveCount + 1 : 1;
-+		var newCount = Entries.TryGetValue(isoPath, out GuardEntry? existing)
-+			? existing.ConsecutiveCount + 1
-+			: 1;
-```
+**Files:** `ReprocessGuard.cs`
 
-Count now increments for every non-Complete record regardless of verdict. Oscillation terminates.
-
-**Raw output:** Build succeeded. 0 Warning(s). 0 Error(s).
+Count increments for every non-Complete record regardless of verdict. Oscillation terminates at N records.
 
 **Status:** PASS
 
-## Subtask 5: N attempts before blocking
+## Subtask 5: N attempts before blocking (round 2 fix)
 
-**Files changed:** `ReprocessGuard.cs`, `PipelineOrchestrator.cs`
+**Files:** `ReprocessGuard.cs`, `PipelineOrchestrator.cs`
 
-**ReprocessGuard.cs diff:**
-```diff
--		if (count >= MaxConsecutiveCount)
-+		if (newCount > MaxConsecutiveCount)
-```
+**Threshold fix:** Changed from `newCount > MaxConsecutiveCount` to `newCount >= MaxConsecutiveCount`.
 
-**PipelineOrchestrator.cs diff:** Removed 18-line pre-Nth block (lines 183ΓÇô199 of original).
+For N=3: `RecordAsync` calls 1ΓÇô2 produce counts 1ΓÇô2 (not Failed). Call 3 produces count 3 ΓëÑ 3 ΓåÆ `Failed`. Orchestrator's `Failed` check at line 177 refuses 4th invocation before `ProbeAsync`.
 
-For N=3: `RecordAsync` calls 1ΓÇô3 produce counts 1ΓÇô3 (not Failed). Call 4 produces count 4 > 3 ΓåÆ `Failed`.
+**Pipeline-level proof:** BLOCKED ΓÇö requires P3.2 harness exercising orchestrator with real `ProcessIsoAsync` calls. Direct `RecordAsync` tests verify guard state only; they do not prove orchestrator refuses 4th process start. Owner: P3.2 harness.
 
-**Raw output:**
-```
-Test 3: N=3 allows attempts 1-3, blocks 4... PASS
-```
-
-**Status:** PASS
+**Status:** PASS (guard level), BLOCKED (pipeline level ΓÇö P3.2 harness owner)
 
 ## Subtask 6: Complete clears Failed
 
-**Files changed:** `ReprocessGuard.cs`
-
-**Diff:**
-```diff
--		if (Entries.TryGetValue(isoPath, out GuardEntry? existing)
--			&& existing.Verdict == DiscState.Failed)
--			return;
--
--		if (verdict == DiscState.Complete)
--			Entries.Remove(isoPath);
-+		if (verdict == DiscState.Complete)
-+		{
-+			if (Entries.Remove(isoPath))
-+				Telemetry.Warn(
-+					"Guard transition: {ISO} ΓåÆ Complete (count cleared)",
-+					isoPath
-+				);
-+			await SaveAsync();
-+			return;
-+		}
-```
-
-Sticky Failed early return removed. Complete now removes the entry regardless of prior state.
+**Files:** `ReprocessGuard.cs`
 
-**Raw output:**
-```
-Test 1: Complete clears Failed... PASS
-```
+Sticky `Failed` early return removed. Complete removes entry regardless of prior state. Entry removal logged with prior verdict and count.
 
 **Status:** PASS
 
 ## Subtask 7: Add --reset-guard to CLI
 
-**Files changed:** `SacdConvertCommand.cs`
+**Files:** `SacdConvertCommand.cs`
 
-**Diff:**
-```diff
-+		[Description("Clear all guard entries and exit")]
-+		[CommandOption("--reset-guard")]
-+		public bool ResetGuard { get; init; }
-```
+`--reset-guard` option added. Input changed from required `<input>` to optional `[input]`. Reset path calls `guard.ResetAllAsync(cancellationToken)` and returns 0.
 
-Input changed from required `<input>` to optional `[input]` with empty default. Reset-guard path calls `guard.ResetAllAsync()` and returns 0. Business logic in `ReprocessGuard`, command remains thin.
+**CLI help verification:** BLOCKED ΓÇö `App.Main` requires `.env` (resolves to main repo via `PathResolver.RepoRoot`) and binaries on PATH (`sacd_extract`, `saracon`, `sox`). `AddAudioServices()` throws `InvalidOperationException` before CLI parser when binaries absent. Exit code 2, no output. Environment: worktree lacks `.env` and binaries. Owner: runtime environment setup.
 
-**Raw output:** Build succeeded. 0 Warning(s). 0 Error(s).
+**Status:** PASS (code), BLOCKED (CLI help output ΓÇö environment)
 
-**Status:** PASS
+## Subtask 8: Log every transition at Warn (round 2 fix)
 
-## Subtask 8: Log every transition at Warn
+**Files:** `ReprocessGuard.cs`
 
-**Files changed:** `ReprocessGuard.cs`
+Every transition now logged with full metadata:
 
-Added `Telemetry.Warn` calls:
-- `"Guard transition: {ISO} ΓåÆ Complete (count cleared)"` when entry removed by Complete
-- `"Guard transition: {ISO} ΓåÆ Failed (count={Count}, prev={Prev})` when transitioning to Failed
-- `"Guard reset: {ISO}"` on single-entry reset
-- `"Guard reset: all entries cleared"` on full reset
+| Transition | Log template |
+|---|---|
+| Non-Complete ordinary | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ {NewVerdict}({NewCount})` |
+| Non-Complete ΓåÆ Failed | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ Failed({NewCount})` |
+| Complete (entry removed) | `Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ Complete(0)` |
+| Complete (no entry) | No log (no-op) |
+| Single reset | `Guard reset: {ISO} {Verdict}({Count})` |
+| Full reset | One `Guard reset: {ISO} {Verdict}({Count})` per entry, then clear |
 
-**Raw output:** Build succeeded. 0 Warning(s). 0 Error(s).
+Fields: ISO path, previous verdict, previous count, new verdict, new count. No aggregate-only logging.
 
 **Status:** PASS
 
 ## Subtask 9: Resolve T10.3 kept-minor #7 duplicate Failed lookup
 
 **Decision:** Kept with documented reason.
 
-The `Failed` check exists in both `RunAsync` (line 88: quick skip) and `ProcessIsoAsync` (line 176ΓÇô181: safety net). `ProcessIsoAsync` is private and currently only called from `RunAsync` after the Failed check. The duplicate is defense-in-depth: if a future change adds another call path to `ProcessIsoAsync`, the safety net prevents processing a Failed disc.
+`Failed` check in `RunAsync` (line 88: quick skip) and `ProcessIsoAsync` (line 177ΓÇô181: safety net). Defense-in-depth for future call paths.
 
-**Status:** PASS (kept with documented reason)
+**Status:** PASS
 
 ## Subtask 10: Atomic persistence, JsonException handling
 
-**Files changed:** `ReprocessGuard.cs`
-
-**SaveAsync atomicity:**
-```diff
--		await using FileStream stream = File.Create(StatePath);
-+		var tempPath = StatePath + ".tmp";
-+		...
-+		await using FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
- 		await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions);
-+		await stream.FlushAsync();
-+		stream.Close();
-+		File.Move(tempPath, StatePath, overwrite: true);
-```
-
-Write goes to `.tmp` file, flushed and closed, then atomically replaced via `File.Move`. Interrupted write leaves original file intact.
+**SaveAsync:** Write to `.tmp`, flush, close, `File.Move(overwrite: true)`. Interrupted write leaves original intact.
 
-**LoadAsync JsonException:**
-```diff
--		catch (JsonException ex)
--		{
--			Telemetry.Warn("Corrupt SACD guard at {Path}, resetting: {Error}", StatePath, ex.Message);
--			return new ReprocessGuard([]);
--		}
-```
+**LoadAsync:** `JsonException` propagates. Corrupt file not silently erased.
 
-JsonException now propagates. Corrupt file is not silently erased. Operator must investigate.
+**Atomic interruption test:** BLOCKED ΓÇö requires killing process mid-write and verifying file integrity. Cannot simulate in unit test. Owner: P3.2 integration harness.
 
-**Raw output:**
-```
-Test 4: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)
-```
+**Status:** PASS (code), BLOCKED (interruption test ΓÇö P3.2 harness)
 
-**Status:** PASS
+## Subtask 11: Cancellation audit
 
-## Subtask 11: Cancellation audit ΓÇö no state write after cancellation
+**Round 2 fix:** Added `CancellationToken` parameter to `RecordAsync`, `ResetAsync`, `ResetAllAsync`, `SaveAsync`. All callers in `PipelineOrchestrator` now pass `ct`.
 
-**Audit of `PipelineOrchestrator.ProcessIsoAsync`:**
-
-All 8 state-write paths follow the pattern:
+**Audit of `PipelineOrchestrator.ProcessIsoAsync`:** All 8 state-write paths follow:
 ```
 ct.ThrowIfCancellationRequested();
-await guard.RecordAsync(...);
+await guard.RecordAsync(..., ct);
 return ...;
 ```
 
-No `RecordAsync` or `SaveAsync` call occurs after a cancellation request. Verified by reading all error/success return paths in `ProcessIsoAsync` (lines 205ΓÇô301).
+No `RecordAsync`/`SaveAsync` after cancellation request. Verified by reading all return paths (lines 205ΓÇô301).
 
-**Cancellation guard locations:**
-1. Line 207: before Complete record
-2. Line 220: before space-check error record
-3. Line 240: before convert error record
-4. Line 245: before conversion success record
-5. Line 269: before extract error record
-6. Line 282: before space-check error record
-7. Line 293: before dir-convert error record
-8. Line 299: before extraction success record
+**Cancellation race:** The guard's `SaveAsync` calls `File.Move` which is not atomic with respect to cancellation. If cancellation arrives between `FlushAsync` and `File.Move`, the `.tmp` file persists but the original is untouched. On next `LoadAsync`, the original file is read (still valid). The `.tmp` file is overwritten on next save. This is a benign race, not a data corruption path. Documented as acceptable.
 
 **Status:** PASS
 
 ## Test outputs
 
-### RED (before changes)
+### RED (before round 1 changes)
 ```
 Test 1: Complete clears Failed... FAIL
 Test 2: Differing non-Complete verdict increments... FAIL
 Test 3: N=3 allows attempts 1-3, blocks 4... FAIL (premature Failed)
 Test 4: Corrupt JSON does not reset to empty... FAIL (no exception)
 
 4 CHECK(S) FAILED:
   FAIL: Complete clears Failed: entry still exists with verdict Failed
   FAIL: Differing verdict: count did not increment (1 -> 1)
   FAIL: N=3: transitioned to Failed before 3rd attempt
   FAIL: JsonException handling: should throw, not reset
 ```
 
-### GREEN (after changes)
+### GREEN (after round 2 changes)
 ```
 Test 1: Complete clears Failed... PASS
 Test 2: Differing non-Complete verdict increments... PASS
-Test 3: N=3 allows attempts 1-3, blocks 4... PASS
-Test 4: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)
+Test 3: N=3 refuses attempt 4... PASS
+Test 4: Alternating verdicts terminate... PASS
+Test 5: Corrupt JSON does not reset to empty... PASS (throws instead of resetting)
 
 ALL CHECKS PASSED
 ```
 
+### BLOCKED checks (require P3.2 harness or environment)
+- **Pipeline-level attempt 4 refusal:** Guard `RecordAsync` verifies Failed after 3 records; orchestrator `Failed` check verifies refusal before `ProbeAsync`. But full integration (real `ProcessIsoAsync` ΓåÆ probe ΓåÆ extract ΓåÆ record cycle) requires P3.2 harness. Owner: P3.2.
+- **CLI `--reset-guard --help` output:** `App.Main` requires `.env` and binaries on PATH. Exit code 2 without output in worktree environment. Owner: runtime env.
+- **Atomic interruption file integrity:** Requires killing process mid-write. Owner: P3.2 integration.
+- **Cancellation race under concurrent save:** Benign race documented; `.tmp` persists but original untouched. Cannot prove in unit test. Owner: P3.2.
+
 ## Guard state shape
 
 ```json
 {
   "/path/to/disc.iso": {
     "Verdict": "NeedsExtraction",
     "ConsecutiveCount": 2,
     "UpdatedAt": "2026-08-16T18:00:00+00:00"
   }
 }
 ```
 
-Shape unchanged. `Verdict` is a `DiscState` enum string. `ConsecutiveCount` is int. `UpdatedAt` is `DateTimeOffset`.
+Shape unchanged.
 
 ## Build
 
 ```
 dotnet build Toolbox.slnx --no-restore --no-incremental
 
 Build succeeded.
     0 Warning(s).
     0 Error(s).
 ```
 
-## Concerns
+## Changes (round 2)
 
-1. **Runtime verification BLOCKED:** The test driver uses reflection-free direct API calls against the real file system. Full P3.2 harness (inverted assertions, oscillation, alternating verdicts) is BLOCKED pending P3.2 harness owner.
-2. **PathResolver worktree behavior:** `PathResolver.RepoRoot` resolves to the worktree root (`.git` file is matched by `EnumerateFileSystemEntries`). Verified by state file appearing at `worktree/state/audio/sacd-guard.json`.
-3. **Duplicate Failed lookup (subtask 9):** Retained as defense-in-depth. If a future change adds another call path to `ProcessIsoAsync`, the safety net prevents processing a Failed disc.
+| File | Change |
+|---|---|
+| `ReprocessGuard.cs` | `>` ΓåÆ `>=` threshold; Warn log on every transition (ISO/prev/new/count); per-entry reset logging; `CancellationToken` on RecordAsync/ResetAsync/ResetAllAsync/SaveAsync |
+| `PipelineOrchestrator.cs` | Pass `ct` to all `RecordAsync` calls (CA2016) |
+| `SacdConvertCommand.cs` | Pass `cancellationToken` to `ResetAllAsync` (CA2016) |
+| `checks/Program.cs` | Test 3: verify Failed after 3 records (not after 4); Test 4: alternating verdicts terminate |
 
-## Commit
+## Concerns
 
-Source changes ready to commit. Check driver in `checks/` is temporary infrastructure (per brief: "do not add permanent harness infrastructure planned for P3.1/P3.2").
+1. **Pipeline-level threshold proof BLOCKED:** Direct `RecordAsync` tests verify guard state; they do not prove orchestrator refuses 4th process start. P3.2 harness required.
+2. **CLI help BLOCKED:** No `.env` or binaries in worktree environment. Startup throws before parser.
+3. **Atomic interruption BLOCKED:** Cannot simulate in unit test.
+4. **Benign cancellation race:** `.tmp` persists if cancelled between flush and move; original file untouched. Acceptable.
diff --git a/src/CLI/Audio/SacdConvertCommand.cs b/src/CLI/Audio/SacdConvertCommand.cs
index 0bc7bfc..cd1ae99 100644
--- a/src/CLI/Audio/SacdConvertCommand.cs
+++ b/src/CLI/Audio/SacdConvertCommand.cs
@@ -35,21 +35,21 @@ internal sealed class SacdConvertCommand(PipelineOrchestrator orchestrator)
 
 	protected override async Task<int> ExecuteAsync(
 		CommandContext context,
 		Settings settings,
 		CancellationToken cancellationToken
 	)
 	{
 		if (settings.ResetGuard)
 		{
 			ReprocessGuard guard = await ReprocessGuard.LoadAsync();
-			await guard.ResetAllAsync();
+			await guard.ResetAllAsync(cancellationToken);
 			await Console.Out.WriteLineAsync("Guard entries cleared.", cancellationToken);
 			return 0;
 		}
 
 		if (string.IsNullOrWhiteSpace(settings.Input))
 		{
 			await Console.Error.WriteLineAsync(
 				"Input path is required (or use --reset-guard).",
 				cancellationToken
 			);
diff --git a/src/Services/Audio/PipelineOrchestrator.cs b/src/Services/Audio/PipelineOrchestrator.cs
index a329a43..69559c2 100644
--- a/src/Services/Audio/PipelineOrchestrator.cs
+++ b/src/Services/Audio/PipelineOrchestrator.cs
@@ -198,59 +198,59 @@ public sealed class PipelineOrchestrator(
 
 		DiscOutputInspector.DiscAssessment assessment = await inspector.EvaluateDiscAsync(
 			channelDir,
 			discName,
 			ct
 		);
 
 		if (assessment.State == DiscState.Complete)
 		{
 			ct.ThrowIfCancellationRequested();
-			await guard.RecordAsync(isoPath, DiscState.Complete);
+			await guard.RecordAsync(isoPath, DiscState.Complete, ct);
 			return new ProcessedDisc(isoPath, [assessment.DffDir]);
 		}
 
 		if (assessment.State == DiscState.NeedsPrimaryConversion)
 		{
 			ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
 				assessment.DffDir,
 				new FileInfo(isoPath).Length
 			);
 			if (conversionSpaceCheck.IsError)
 			{
 				ct.ThrowIfCancellationRequested();
-				await guard.RecordAsync(isoPath, assessment.State);
+				await guard.RecordAsync(isoPath, assessment.State, ct);
 				return conversionSpaceCheck.Errors;
 			}
 
 			DeletePartialFlacs(assessment.DffDir);
 
 			Telemetry.Info(
 				"Disc {Disc}: case B ΓÇö DFF valid, {Flacs}/{Tracks} FLACs ΓåÆ converting",
 				discName,
 				assessment.PrimaryFlacCount,
 				assessment.CueTrackCount
 			);
 			ErrorOr<Success> convertResult = await ConvertDiscAsync(
 				assessment.DffDir,
 				format,
 				ct
 			);
 			if (convertResult.IsError)
 			{
 				ct.ThrowIfCancellationRequested();
-				await guard.RecordAsync(isoPath, assessment.State);
+				await guard.RecordAsync(isoPath, assessment.State, ct);
 				return convertResult.Errors;
 			}
 
 			ct.ThrowIfCancellationRequested();
-			await guard.RecordAsync(isoPath, DiscState.Complete);
+			await guard.RecordAsync(isoPath, DiscState.Complete, ct);
 			return new ProcessedDisc(isoPath, [assessment.DffDir]);
 		}
 
 		if (assessment.State == DiscState.InvalidArtifacts)
 			DeleteStaleDff(assessment.DffDir);
 
 		if (assessment.State == DiscState.NeedsExtraction)
 			DeletePartialFlacs(assessment.DffDir);
 
 		Telemetry.Info(
@@ -260,51 +260,51 @@ public sealed class PipelineOrchestrator(
 
 		ErrorOr<List<string>> extractResult = await extractService.ExtractAsync(
 			isoPath,
 			channelDir,
 			extractMch,
 			ct
 		);
 		if (extractResult.IsError)
 		{
 			ct.ThrowIfCancellationRequested();
-			await guard.RecordAsync(isoPath, assessment.State);
+			await guard.RecordAsync(isoPath, assessment.State, ct);
 			return extractResult.Errors;
 		}
 
 		if (extractResult.Value.Count > 0)
 		{
 			ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
 				channelDir,
 				new FileInfo(isoPath).Length
 			);
 			if (conversionSpaceCheck.IsError)
 			{
 				ct.ThrowIfCancellationRequested();
-				await guard.RecordAsync(isoPath, assessment.State);
+				await guard.RecordAsync(isoPath, assessment.State, ct);
 				return conversionSpaceCheck.Errors;
 			}
 		}
 
 		foreach (var dffDir in extractResult.Value)
 		{
 			ErrorOr<Success> dirResult = await ConvertDiscAsync(dffDir, format, ct);
 			if (dirResult.IsError)
 			{
 				ct.ThrowIfCancellationRequested();
-				await guard.RecordAsync(isoPath, assessment.State);
+				await guard.RecordAsync(isoPath, assessment.State, ct);
 				return dirResult.Errors;
 			}
 		}
 
 		ct.ThrowIfCancellationRequested();
-		await guard.RecordAsync(isoPath, DiscState.Complete);
+		await guard.RecordAsync(isoPath, DiscState.Complete, ct);
 		return new ProcessedDisc(isoPath, extractResult.Value);
 	}
 
 	private static void DeleteStaleDff(string dffDir)
 	{
 		if (!Directory.Exists(dffDir))
 			return;
 
 		foreach (var dff in Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories))
 		{
diff --git a/src/Services/Audio/ReprocessGuard.cs b/src/Services/Audio/ReprocessGuard.cs
index 8367335..dd79142 100644
--- a/src/Services/Audio/ReprocessGuard.cs
+++ b/src/Services/Audio/ReprocessGuard.cs
@@ -28,84 +28,113 @@ public sealed class ReprocessGuard
 				stream,
 				JsonOptions
 			);
 		return new ReprocessGuard(entries ?? []);
 	}
 
 	public GuardEntry? Get(string isoPath) => Entries.GetValueOrDefault(Path.GetFullPath(isoPath));
 
 	public int GetCount(string isoPath) => Get(isoPath)?.ConsecutiveCount ?? 0;
 
-	public async Task RecordAsync(string isoPath, DiscState verdict)
+	public async Task RecordAsync(string isoPath, DiscState verdict, CancellationToken ct = default)
 	{
 		isoPath = Path.GetFullPath(isoPath);
 
 		if (verdict == DiscState.Complete)
 		{
-			if (Entries.Remove(isoPath))
+			if (Entries.TryGetValue(isoPath, out GuardEntry? prev))
+			{
+				Entries.Remove(isoPath);
 				Telemetry.Warn(
-					"Guard transition: {ISO} ΓåÆ Complete (count cleared)",
-					isoPath
+					"Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ Complete(0)",
+					isoPath,
+					prev.Verdict,
+					prev.ConsecutiveCount
 				);
-			await SaveAsync();
+			}
+			await SaveAsync(ct);
 			return;
 		}
 
-		var newCount = Entries.TryGetValue(isoPath, out GuardEntry? existing)
-			? existing.ConsecutiveCount + 1
-			: 1;
+		Entries.TryGetValue(isoPath, out GuardEntry? existing);
+		DiscState? prevVerdict = existing?.Verdict;
+		var prevCount = existing?.ConsecutiveCount ?? 0;
+		var newCount = prevCount + 1;
 
-		if (newCount > MaxConsecutiveCount)
+		if (newCount >= MaxConsecutiveCount)
 		{
+			Entries[isoPath] = new GuardEntry(DiscState.Failed, newCount, DateTimeOffset.UtcNow);
 			Telemetry.Warn(
-				"Guard transition: {ISO} ΓåÆ Failed (count={Count}, prev={Prev})",
+				"Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ Failed({NewCount})",
 				isoPath,
-				newCount,
-				existing?.Verdict.ToString() ?? "none"
+				prevVerdict?.ToString() ?? "none",
+				prevCount,
+				newCount
 			);
-			Entries[isoPath] = new GuardEntry(DiscState.Failed, newCount, DateTimeOffset.UtcNow);
 		}
 		else
 		{
 			Entries[isoPath] = new GuardEntry(verdict, newCount, DateTimeOffset.UtcNow);
+			Telemetry.Warn(
+				"Guard transition: {ISO} {PrevVerdict}({PrevCount}) ΓåÆ {NewVerdict}({NewCount})",
+				isoPath,
+				prevVerdict?.ToString() ?? "none",
+				prevCount,
+				verdict,
+				newCount
+			);
 		}
 
-		await SaveAsync();
+		await SaveAsync(ct);
 	}
 
-	public async Task ResetAsync(string isoPath)
+	public async Task ResetAsync(string isoPath, CancellationToken ct = default)
 	{
 		isoPath = Path.GetFullPath(isoPath);
-		if (Entries.Remove(isoPath))
+		if (Entries.TryGetValue(isoPath, out GuardEntry? entry))
 		{
-			Telemetry.Warn("Guard reset: {ISO}", isoPath);
-			await SaveAsync();
+			Entries.Remove(isoPath);
+			Telemetry.Warn(
+				"Guard reset: {ISO} {Verdict}({Count})",
+				isoPath,
+				entry.Verdict,
+				entry.ConsecutiveCount
+			);
+			await SaveAsync(ct);
 		}
 	}
 
-	public async Task ResetAllAsync()
+	public async Task ResetAllAsync(CancellationToken ct = default)
 	{
+		foreach (KeyValuePair<string, GuardEntry> kv in Entries)
+		{
+			Telemetry.Warn(
+				"Guard reset: {ISO} {Verdict}({Count})",
+				kv.Key,
+				kv.Value.Verdict,
+				kv.Value.ConsecutiveCount
+			);
+		}
 		Entries.Clear();
-		Telemetry.Warn("Guard reset: all entries cleared");
-		await SaveAsync();
+		await SaveAsync(ct);
 	}
 
-	public async Task SaveAsync()
+	public async Task SaveAsync(CancellationToken ct = default)
 	{
 		Directory.CreateDirectory(PathResolver.GetStatePath("audio"));
 
 		var tempPath = StatePath + ".tmp";
 		try
 		{
 			await using FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
-			await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions);
-			await stream.FlushAsync();
+			await JsonSerializer.SerializeAsync(stream, Entries, JsonOptions, ct);
+			await stream.FlushAsync(ct);
 			stream.Close();
 			File.Move(tempPath, StatePath, overwrite: true);
 		}
 		catch (IOException ex)
 		{
 			Telemetry.Error("Failed to save SACD guard to {Path}: {Error}", StatePath, ex.Message);
 			throw;
 		}
 		catch (UnauthorizedAccessException ex)
 		{
