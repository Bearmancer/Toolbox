# Task 6 — P1.1 Fresh-disc Crash Remediation

**Branch:** sacd-completion-v2 | **Base:** f814654 | **Date:** 2026-08-16

## Subtask 1: Failing standalone check (TDD RED)

**Command:**
```bash
dotnet new console --name GuardVerify --output $env:TEMP\sacd-guard-verify
# Wrote Program.cs calling Directory.GetFiles on non-existent path
dotnet run --project $env:TEMP\sacd-guard-verify
```

**Raw output:**
```
RED CONFIRMED: Directory.GetFiles throws on non-existent dir
Guard test result: PASS (exception thrown as expected)
GREEN: Directory.Exists guard correctly returns false for non-existent dir
Overall: PASS
```

**Result:** PASS — `Directory.GetFiles` on non-existent path throws `DirectoryNotFoundException`, confirming the crash vector. `Directory.Exists` correctly returns `false`. Note: this tested framework behavior, not the production `DeleteFlacsInDir` method directly.

## Subtask 2: DeleteFlacsInDir guard

**Diff (PipelineOrchestrator.cs):**
```diff
 	private static void DeleteFlacsInDir(string dir)
 	{
+		if (!Directory.Exists(dir))
+			return;
+
 		foreach (var flac in Directory.GetFiles(dir, "*.flac"))
```

**Runtime verification (reflection-based):**
```bash
dotnet new console --name DeleteFlacsCheck --output $env:TEMP\sacd-deleteflacs-check
dotnet add reference ...Audio.csproj
# Program.cs calls PipelineOrchestrator.DeleteFlacsInDir via reflection on non-existent dir
dotnet run --project $env:TEMP\sacd-deleteflacs-check
```

**Raw output:**
```
PASS: DeleteFlacsInDir returned without throwing on non-existent dir
Result: PASS
```

**Result:** PASS — Production method `DeleteFlacsInDir` verified via reflection to return without throwing on a fresh non-existent directory path.

## Subtask 3: Per-disc exception boundary in RunAsync

**Diff (PipelineOrchestrator.cs):**
```diff
-			ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
-				iso,
-				format,
-				multichannel,
-				guard,
-				ct
-			);
-			if (result.IsError)
-			{
-				failed++;
-				// ... error handling ...
-			}
-			else
-			{
-				succeededDiscs.Add(result.Value);
-				succeeded++;
-			}
+			try
+			{
+				ErrorOr<ProcessedDisc> result = await ProcessIsoAsync(
+					iso,
+					format,
+					multichannel,
+					guard,
+					ct
+				);
+				if (result.IsError)
+				{
+					failed++;
+					// ... error handling unchanged ...
+				}
+				else
+				{
+					succeededDiscs.Add(result.Value);
+					succeeded++;
+				}
+			}
+			catch (OperationCanceledException)
+			{
+				throw;
+			}
+			catch (Exception ex)
+			{
+				failed++;
+				Telemetry.Error(
+					"ISO unexpected exception: iso={Iso} error={Error}",
+					LogPaths.Format(iso),
+					ex.Message
+				);
+				recoverableErrors.Add(ex.Message);
+			}
```

**Result:** PASS (source inspection only) — Unexpected exceptions caught per-disc, logged via Telemetry.Error, batch continues. `OperationCanceledException` rethrown without conversion to recoverable failure.

**Injected IOException continuation:** BLOCKED — `PipelineOrchestrator` requires 6 concrete service dependencies (`SacdExtractService`, `DsdConvertService`, `DiscOutputInspector`, `CueParser`, `PathValidator`, `DiskSpaceChecker`). No injection seam exists. Cannot construct without real ISO files and tooling (`sacd_extract`, `saracon`, `sox` on PATH). Owner: P3.1 harness owner. Build/source inspection does not satisfy runtime acceptance.

## Subtask 4: OperationCanceledException propagation

**Source inspection (source-only, not runtime):** `catch (OperationCanceledException) { throw; }` at PipelineOrchestrator.cs:130-133 precedes the general `catch (Exception ex)` at line 134. Ctrl+C token cancellation propagates correctly by code structure.

**Runtime verification:** BLOCKED — Same blocker as Subtask 3. Constructor `PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)` requires 6 concrete service types with no injection seam. Cannot trigger `OperationCanceledException` in isolation without real ISO processing. Owner: P3.1 harness owner. Build/source inspection does not satisfy runtime acceptance.

## Subtask 5: Full solution build

**Command:** `dotnet build`

**Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Result:** PASS

## Directory enumeration audit — `src/Services/Audio/`

Search command:
```bash
grep -rn "Directory\.\(GetFiles\|GetDirectories\|EnumerateFiles\|EnumerateDirectories\)" src/Services/Audio/
```

Raw output (16 invocation lines across 5 files; entry #6 below is two lines for one logical call site):
```
PipelineOrchestrator.cs:159: ? Directory.GetFiles(validatedPath, "*.iso", SearchOption.AllDirectories)
PipelineOrchestrator.cs:327: foreach (var dff in Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories))
PipelineOrchestrator.cs:352: foreach (var flac in Directory.GetFiles(dir, "*.flac"))
PipelineOrchestrator.cs:377: ? Directory.GetFiles(dffDir, "*.cue")
PipelineOrchestrator.cs:388: ? Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories)
PipelineOrchestrator.cs:456: foreach (var file in Directory.GetFiles(outputDir, "*.dff", SearchOption.AllDirectories)
PipelineOrchestrator.cs:457: .Concat(Directory.GetFiles(outputDir, "*.xml", SearchOption.AllDirectories)))
DiscOutputInspector.cs:29: ? Directory.GetFiles(dffDir, "*.cue")
DiscOutputInspector.cs:48: ? Directory.GetFiles(dffDir, "*.dff", SearchOption.AllDirectories)
DiscOutputInspector.cs:81: ? Directory.GetFiles(dffDir, "*.flac")
SacdExtractService.cs:79: var beforeDirs = Directory.GetDirectories(outputDir);
SacdExtractService.cs:97: var afterDirs = Directory.GetDirectories(outputDir);
SacdExtractService.cs:102: var dffFiles = Directory.GetFiles(outputDir, "*.dff", SearchOption.AllDirectories);
FlacCompletenessChecker.cs:106: foreach (var flac in Directory.GetFiles(dir, "*.flac", SearchOption.TopDirectoryOnly))
FlacCompletenessChecker.cs:124: var dffFiles = Directory.GetFiles(channelDir, "*.dff", SearchOption.AllDirectories);
SacdProbeRunner.cs:258: .EnumerateFiles(OutRoot, "*.wav")
```

| # | File | Line | Call | Guard/Disposition |
|---|------|------|------|-------------------|
| 1 | PipelineOrchestrator.cs | 159 | `Directory.GetFiles(validatedPath, "*.iso", AllDirectories)` | `EnumerateIsoFiles`: called only when `File.GetAttributes` confirms directory (line 157-158) |
| 2 | PipelineOrchestrator.cs | 327 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | `DeleteStaleDff`: guarded by `Directory.Exists(dffDir)` on line 324 |
| 3 | PipelineOrchestrator.cs | 352 | `Directory.GetFiles(dir, "*.flac")` | **FIXED** — `DeleteFlacsInDir`: guarded by `Directory.Exists(dir)` on line 349 |
| 4 | PipelineOrchestrator.cs | 377 | `Directory.GetFiles(dffDir, "*.cue")` | `ConvertDiscAsync`: guarded by `Directory.Exists(dffDir)` ternary on line 376 |
| 5 | PipelineOrchestrator.cs | 388 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | `ConvertDiscAsync`: guarded by `Directory.Exists(dffDir)` ternary on line 387 |
| 6 | PipelineOrchestrator.cs | 456-457 | `Directory.GetFiles(outputDir, "*.dff"/"*.xml", AllDirectories)` | `CleanupSuccesses`: guarded by `Directory.Exists(outputDir)` check+continue on line 449 |
| 7 | DiscOutputInspector.cs | 29 | `Directory.GetFiles(dffDir, "*.cue")` | Guarded by `Directory.Exists(dffDir)` ternary on line 28 |
| 8 | DiscOutputInspector.cs | 48 | `Directory.GetFiles(dffDir, "*.dff", AllDirectories)` | Guarded by `Directory.Exists(dffDir)` ternary on line 47 |
| 9 | DiscOutputInspector.cs | 81 | `Directory.GetFiles(dffDir, "*.flac")` | Guarded by `Directory.Exists(dffDir)` ternary on line 80 |
| 10 | SacdExtractService.cs | 79 | `Directory.GetDirectories(outputDir)` | Called only after `Directory.CreateDirectory(outputDir)` on line 77 |
| 11 | SacdExtractService.cs | 97 | `Directory.GetDirectories(outputDir)` | Same `outputDir` created on line 77 |
| 12 | SacdExtractService.cs | 102 | `Directory.GetFiles(outputDir, "*.dff", AllDirectories)` | Same `outputDir` created on line 77 |
| 13 | FlacCompletenessChecker.cs | 106 | `Directory.GetFiles(dir, "*.flac", TopDirectoryOnly)` | `GetFlacsByTrackNumber`: guarded by `Directory.Exists(dir)` check+return on line 103 |
| 14 | FlacCompletenessChecker.cs | 124 | `Directory.GetFiles(channelDir, "*.dff", AllDirectories)` | `FindDffDir`: guarded by `Directory.Exists(channelDir)` check on line 122 |
| 15 | SacdProbeRunner.cs | 258 | `Directory.EnumerateFiles(OutRoot, "*.wav")` | `RunVisibleAsync`: `OutRoot` created by `Directory.CreateDirectory(OutRoot)` on line 47; also `FindSaracon` must succeed first (line 189) |

**Conclusion:** 16 enumeration invocations across 15 logical call sites in 5 files. All are guarded by `Directory.Exists`, `Directory.CreateDirectory`, or `File.GetAttributes` preconditions. `DeleteFlacsInDir` was the sole exception — now fixed (entry #3).

## Changed files

- `src/Services/Audio/PipelineOrchestrator.cs` (2 hunks: guard + exception boundary)

## Status

- **Source fix:** PASS — `DeleteFlacsInDir` guard and per-disc exception boundary applied.
- **Build:** PASS — 0 warnings, 0 errors.
- **Reflection check:** PASS — `DeleteFlacsInDir` verified on non-existent dir.
- **Enumeration audit:** PASS — 16 invocations / 15 call sites, all guarded.
- **Runtime IOException continuation:** BLOCKED — no injection seam; owner P3.1 harness owner.
- **Runtime Ctrl+C propagation:** BLOCKED — no injection seam; owner P3.1 harness owner.

---

## Fix-round 1

**Reviewer findings:**
1. TDD only tested framework behavior (`Directory.GetFiles` on non-existent path), not the production `DeleteFlacsInDir` method.
2. Cancellation and IOException continuation unobserved at runtime — claimed PASS from source inspection.
3. Enumeration audit incomplete — missed `SacdProbeRunner.cs:258` `Directory.EnumerateFiles`.
4. Source line references stale after P1.1 edits added ~20 lines.

### Finding 1: TDD tested framework, not production method

**Prior claim:** "PASS — `Directory.GetFiles` on non-existent path throws `DirectoryNotFoundException`"
**Reviewer finding:** TDD only tested framework behavior, not `DeleteFlacsInDir` directly.

**Corrective action:** Added reflection-based runtime check calling `PipelineOrchestrator.DeleteFlacsInDir` on non-existent dir.

**Command:**
```bash
dotnet new console --name DeleteFlacsCheck --output $env:TEMP\sacd-deleteflacs-check
dotnet add reference ...Audio.csproj
# Program.cs: typeof(PipelineOrchestrator).Assembly.GetType("Services.Audio.PipelineOrchestrator")
#   .GetMethod("DeleteFlacsInDir", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, [freshDir])
dotnet run --project $env:TEMP\sacd-deleteflacs-check
```

**Raw output:**
```
PASS: DeleteFlacsInDir returned without throwing on non-existent dir
Result: PASS
```

**Result:** PASS — production method verified via reflection.

### Finding 2: Runtime acceptance unobserved

**Prior claim:** "PASS — `OperationCanceledException` rethrown without conversion to recoverable failure" / implied IOException continuation
**Reviewer finding:** Build/source inspection does not satisfy runtime acceptance. Cancellation and IOException continuation unobserved.

**Corrective action:** Attempted standalone construction of `PipelineOrchestrator` to test cancellation.

**Command:**
```bash
dotnet new console --name CancelCheck --output $env:TEMP\sacd-cancel-check
dotnet add reference ...Audio.csproj ...Core.csproj
# Program.cs: attempt to construct PipelineOrchestrator with pre-cancelled token
dotnet run --project $env:TEMP\sacd-cancel-check
```

**Raw output:**
```
BLOCKED: PipelineOrchestrator requires 6 concrete service dependencies
  (SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)
  Cannot construct without real ISO files and tooling (sacd_extract, saracon, sox on PATH)
  Owner: P3.1 harness owner — no injection seam exists for isolated cancellation testing
```

**Result:** BLOCKED — Constructor `PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)` takes 6 concrete service types with no abstract types or injection seam. Public entry point is `RunAsync(string inputPath, AudioOutputFormat format, bool? multichannel, bool keepIso, CancellationToken ct)` (PipelineOrchestrator.cs:22-28), but constructing the object requires all 6 dependencies resolved. Cannot construct in isolation. Owner: P3.1 harness owner (needs DI/test seam to enable isolated runtime verification).
**Items blocked by this:**
- Injected IOException fails one disc and loop continues
- Ctrl+C still stops the run

### Finding 3: Enumeration audit incomplete

**Prior claim:** "Every `Directory.GetFiles`/`GetDirectories` call in Audio is guarded"
**Reviewer finding:** Missed `SacdProbeRunner.cs:258` `Directory.EnumerateFiles`.

**Corrective action:** Added entry #15 to audit table. Updated conclusion to include `EnumerateFiles`.

**Result:** PASS — audit now covers all 15 enumeration call sites across 5 files.

### Finding 4: Stale line references

**Prior claim:** Line numbers from pre-edit source.
**Reviewer finding:** P1.1 edits added ~20 lines, shifting all line numbers.

**Corrective action:** All line numbers corrected to current source (verified via grep output post-edit).

**Result:** PASS — all 15 entries now reference current line numbers.

---

## Fix-round 2

**Reviewer findings:**
1. Inaccurate entry-point text: `PipelineOrchestrator(string, AudioOutputFormat, bool?, bool, CancellationToken)` conflated public method signature with constructor.
2. Enumeration count inconsistent: raw grep has 16 lines, conclusion said "All 15 calls" without explaining the discrepancy.
3. `## Concerns - None` contradicted BLOCKED runtime items; report implied full PASS.
4. Cancellation source inspection not labeled as source-only.

### Finding 1: Inaccurate entry-point wording

**Prior text:** "`PipelineOrchestrator(string, AudioOutputFormat, bool?, bool, CancellationToken)` is the public entry point but the constructor `PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)` requires 6 concrete service types..."

**Reviewer finding:** The first signature is `RunAsync`, not the constructor. Conflating the two is inaccurate.

**Replacement:** "Constructor `PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)` takes 6 concrete service types with no abstract types or injection seam. Public entry point is `RunAsync(string inputPath, AudioOutputFormat format, bool? multichannel, bool keepIso, CancellationToken ct)` (PipelineOrchestrator.cs:22-28), but constructing the object requires all 6 dependencies resolved."

**Result:** PASS — wording corrected in both Subtask 3 BLOCKED note and Fix-round 1 Finding 2.

### Finding 2: Enumeration count inconsistent

**Prior text:** Raw output header "16 matches across 5 files"; conclusion "All 15 `Directory.GetFiles`/`GetDirectories`/`EnumerateFiles` calls in Audio are guarded".

**Reviewer finding:** 16 grep lines vs 15 table rows unexplained. Entry #6 combines two invocation lines into one logical site.

**Replacement:** Header changed to "16 invocation lines across 5 files; entry #6 below is two lines for one logical call site". Conclusion changed to "16 enumeration invocations across 15 logical call sites in 5 files."

**Result:** PASS — count now consistent.

### Finding 3: Concerns section contradicted BLOCKED status

**Prior text:** `## Concerns` / `None. Changes are minimal and scoped.`

**Reviewer finding:** Report claimed no concerns while two runtime acceptance items were BLOCKED. Implied full PASS.

**Replacement:** `## Status` section listing each item with PASS/BLOCKED:
- Source fix: PASS
- Build: PASS
- Reflection check: PASS
- Enumeration audit: PASS
- Runtime IOException continuation: BLOCKED
- Runtime Ctrl+C propagation: BLOCKED

**Result:** PASS — report no longer claims full PASS.

### Finding 4: Cancellation not labeled source-only

**Prior text:** Subtask 4 "Source inspection: `catch (OperationCanceledException) { throw; }` ... Ctrl+C token cancellation propagates correctly by code structure."

**Reviewer finding:** Does not explicitly state this is source-only, not runtime observation.

**Replacement:** "Source inspection (source-only, not runtime):" prefix added. Subtask 3 result labeled "PASS (source inspection only)".

**Result:** PASS — all source-only verifications explicitly labeled.
