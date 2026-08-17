# Task 9 Report — P1.4 Split error capture

**Branch:** sacd-completion-v2 | **Base HEAD:** dd28089 | **File:** `src/Services/Audio/DsdConvertService.cs`

## Subtask 1 — Capture split failures into `Dictionary<int,string>` keyed by track number

**Command:** `git diff src/Services/Audio/DsdConvertService.cs`

**Diff:**
```diff
 			masterPcm = masterResult.Value;
 			List<string> outputFiles = [];
+			Dictionary<int, string> splitErrors = [];

 			foreach (CueTrack track in cue.Tracks)
 			{
@@
 				);

 				if (splitResult.IsError)
+				{
+					var reason = splitResult.Errors[0].Description;
+					splitErrors[track.TrackNumber] = reason;
+					Telemetry.Warn(
+						"DsdConvert.SplitFailed track={Track} output={Output} error={Error}",
+						track.TrackNumber,
+						outputFlac,
+						reason
+					);
 					continue;
+				}

 				outputFiles.Add(outputFlac);
```

**Evidence (source/static):** `splitErrors` declared before the loop, keyed by `track.TrackNumber`. On `splitResult.IsError` the reason (`splitResult.Errors[0].Description`, which for sox carries `sox split exit code {code}: {stderr}` — stderr preserved, not discarded) is stored and a `Telemetry.Warn` fires naming track number, output path, and error text. `continue` still advances to the next track, so remaining tracks are attempted.

**Result: PASS** (source/static).

## Subtask 2 — Aggregate error names missing track numbers plus per-track reasons

**Diff:**
```diff
 			if (outputFiles.Count < cue.Tracks.Count)
 			{
-				List<int> missing = [.. cue.Tracks
-					.Where(t => !outputFiles.Any(f =>
-						Path.GetFileName(f).StartsWith(
-							$"{t.TrackNumber:D2}. ",
-							StringComparison.Ordinal
-						)))
-					.Select(t => t.TrackNumber)];
+				List<int> missing = [.. splitErrors.Keys.Order()];
+				var reasons = string.Join(
+					"; ",
+					splitErrors
+						.OrderBy(kv => kv.Key)
+						.Select(kv => $"track {kv.Key}: {kv.Value}")
+				);
 				return Errors.Audio.ConversionFailed(
 					dffFile,
-					$"Incomplete conversion: missing tracks {string.Join(", ", missing)}"
+					$"Incomplete conversion: missing tracks {string.Join(", ", missing)}. {reasons}"
 				);
 			}
```

**Evidence (source/static):** Every loop iteration either adds to `outputFiles` (split success) or to `splitErrors` (split failure), so `splitErrors.Keys` is exactly the set of missing track numbers — the aggregate still names them, now sorted ascending. The description appends per-track reasons (`track N: <reason>`), each reason including sox stderr. `Errors.Audio.ConversionFailed(file, reason)` unchanged; only the reason string is enriched.

**Result: PASS** (source/static).

## Subtask 3 — Cancellation behavior preserved

**Evidence (source/static):** The change only handles the returned `splitResult.IsError` case; it adds no `catch`. `sox.SplitTrackAsync` → `ProcessRunner.RunAsync` propagates `OperationCanceledException` on cancellation, which flows up through `ConvertAndSplitAsync` unchanged. Cancellation is not converted into a split failure or stored in `splitErrors`.

**Result: PASS** (source/static).

## Subtask 4 — Build

**Build command:** `dotnet build Toolbox.slnx --no-restore --no-incremental`

**Raw output (tail):**
```
  Core -> …\artifacts\bin\Core\debug\Core.dll
  LastFm -> …\artifacts\bin\LastFm\debug\LastFm.dll
  Azure -> …\artifacts\bin\Azure\debug\Azure.dll
  Audio -> …\artifacts\bin\Audio\debug\Audio.dll
  Google -> …\artifacts\bin\Google\debug\Google.dll
  CLI -> …\artifacts\bin\CLI\debug\CLI.dll
  App -> …\artifacts\bin\App\debug\App.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Result: PASS** (build).

## Subtask 5 — Runtime acceptance (injected failure on track 7 of 19)

**BLOCKED.** No seam to inject a sox failure on a specific track without the external `sox` binary and real DFF/FLAC fixtures.

- Signature: `DsdConvertService.ConvertAndSplitAsync(string dffFile, string outputDir, CueSheet cue, DsdConversionSettings settings, DsdProbeResult probe, CancellationToken ct)`.
- Coupling: `DsdConvertService` is `sealed` with ctor-injected `SaraconService`/`SoxService`/`AudioMetadataService`; `SoxService` is `sealed` and `SplitTrackAsync(string, string, TimeSpan, TimeSpan?, CancellationToken)` is non-virtual and shells out to the `sox` binary via `ProcessRunner`. No injection seam for a stubbed per-track split failure.
- Owner: **P3.4** durable split harness (real sox-backed fixtures + injected failure). Re-run acceptance there.

## Summary

| Subtask | Result |
|---|---|
| 1. Capture + Warn + continue | PASS (source/static) |
| 2. Aggregate missing + reasons | PASS (source/static) |
| 3. Cancellation preserved | PASS (source/static) |
| 4. Build | PASS (0 warn / 0 err) |
| 5. Runtime acceptance | BLOCKED → P3.4 harness |

**Concerns:** Runtime acceptance remains **BLOCKED** pending the P3.4 durable split harness. The acceptance case — injected failure on track 7 of 19 produces a Warn naming track 7 and stderr, aggregate error carries the missing list and per-track reasons, remaining tracks still attempted — is **not** runtime-verified here. Source/static checks and the build PASS; the runtime outcome is owned by **P3.4**. New log key `DsdConvert.SplitFailed`; aggregate description format `Incomplete conversion: missing tracks {n}. track {n}: {reason}; …`. No public model, `Errors.cs`, or unrelated formatting changed.
