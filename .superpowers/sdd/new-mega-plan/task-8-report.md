# Task 8 Report — P1.3 Last-track completeness rule

**Branch:** sacd-completion-v2 | **Base HEAD:** f64b9af | **File:** `src/Services/Audio/FlacCompletenessChecker.cs`

## Subtask 1 — Replace `< 30.0` failure with `<= 0`

**Command:** `git diff src/Services/Audio/FlacCompletenessChecker.cs`

**Diff:**
```diff
 		else if (track == cueTracks[^1])
 		{
-			if (durationResult.Value.TotalSeconds < 30.0)
+			if (durationResult.Value.TotalSeconds <= 0)
 			{
-				Telemetry.Info(
+				Telemetry.Warn(
 					"Pipeline.LastTrackTooShort dir={Dir} duration={Duration:F1}s",
```

**Evidence (source/static):** Failure condition now `TotalSeconds <= 0`. A 0-byte final track (duration 0) hits `<= 0` → returns `DurationCheckResult(false, …)` → non-Complete. A 20 s final track (positive) does not hit `<= 0` → no failure return → falls through to Complete.

**Result: PASS** (source/static). Runtime acceptance BLOCKED — see Subtask 5.

## Subtask 2 — Downgrade short-track observation to Warn; do not fail completeness

**Diff:**
```diff
+			if (durationResult.Value.TotalSeconds < 30.0)
+			{
+				Telemetry.Warn(
+					"Pipeline.LastTrackShort dir={Dir} duration={Duration:F1}s",
+					LogPaths.Format(dffDir),
+					durationResult.Value.TotalSeconds
+				);
+			}
```

**Evidence (source/static):** Positive short final track (e.g. 20 s) logs `Telemetry.Warn` with measured duration and does **not** return a failure → remains Complete. Log level downgraded `Info` → `Warn`; failure return removed for the positive-short path.

**Result: PASS** (source/static). Runtime log emission BLOCKED — see Subtask 5.

## Subtask 3 — Confirm `else if` fires only for final track

**Evidence (source/static):** `else if (track == cueTracks[^1])` is the `else` of `if (track.Duration is { } expectedDur)`. Final CUE track `Duration` is null by construction (CUE `INDEX 01` end-of-disc has no following track to derive duration from), so the `if` arm is skipped and the `else if` fires only for the final track. Unchanged.

**Result: PASS** (source/static).

## Subtask 4 — Confirm non-final ±2.0 s tolerance untouched

**Evidence (source/static):** Non-final branch `if (diff > 2.0)` and its `Telemetry.Info` + failure return are byte-identical to base. A non-final track off by 3 s → `diff > 2.0` → non-Complete. Unchanged.

**Result: PASS** (source/static). Runtime acceptance BLOCKED — see Subtask 5.

## Subtask 5 — Build + runtime acceptance

**Build command:** `dotnet build Toolbox.slnx --no-restore --no-incremental`

**Raw output (tail):**
```
  Core -> …\artifacts\bin\Core\debug\Core.dll
  Audio -> …\artifacts\bin\Audio\debug\Audio.dll
  LastFm -> …\artifacts\bin\LastFm\debug\LastFm.dll
  Azure -> …\artifacts\bin\Azure\debug\Azure.dll
  Google -> …\artifacts\bin\Google\debug\Google.dll
  CLI -> …\artifacts\bin\CLI\debug\CLI.dll
  App -> …\artifacts\bin\App\debug\App.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Result: PASS** (build).

**Runtime acceptance (20 s final → Complete; 0-byte final → non-Complete; non-final off 3 s → non-Complete):**

**BLOCKED.** No seam to exercise `CheckTrackDurationsAsync` without the external `sox` binary and real FLAC files.

- Signature: `FlacCompletenessChecker.CheckTrackDurationsAsync(IReadOnlyList<CueTrack> cueTracks, Dictionary<int,string> primaryFlacs, string dffDir, int trackNumberCount, int primaryFlacCount, CancellationToken ct)`.
- Coupling: `FlacCompletenessChecker` is `sealed` with ctor-injected `SoxService`; `SoxService` is `sealed` and `GetDurationAsync(string, CancellationToken)` is non-virtual and shells out to the `sox` binary. No injection seam for a stubbed duration.
- Owner: **P3.3** durable state/completeness harness (provides real FLAC fixtures + sox-backed duration). Re-run acceptance there.

## Summary

| Subtask | Result |
|---|---|
| 1. `<= 0` failure | PASS (source/static) |
| 2. Short-track Warn, no fail | PASS (source/static) |
| 3. `else if` final-only | PASS (source/static) |
| 4. Non-final ±2.0 untouched | PASS (source/static) |
| 5. Build | PASS (0 warn / 0 err) |
| 5. Runtime acceptance | BLOCKED → P3.3 harness |

**Concerns:** Runtime acceptance remains **BLOCKED** pending the P3.3 harness. The three acceptance cases — 20 s final track → Complete, 0-byte final track → non-Complete, non-final track off by 3 s → non-Complete — are **not** runtime-verified here. Source/static checks and the build PASS; the runtime outcome is owned by **P3.3** (durable state/completeness harness with real FLAC fixtures + sox-backed duration). Positive-short Warn uses new key `Pipeline.LastTrackShort`; failure path keeps `Pipeline.LastTrackTooShort`. No public model or unrelated formatting changed.

---

## Fix Round 1 — report-only review finding

**Prior line (contradictory):**
```
**Concerns:** None. Positive-short Warn uses new key `Pipeline.LastTrackShort`; failure path keeps `Pipeline.LastTrackTooShort`. No public model or unrelated formatting changed.
```

**Replacement:** the `**Concerns:**` paragraph above — states runtime acceptance BLOCKED pending P3.3, preserves static PASS/build evidence, names owner P3.3.

**Command:** `git diff .superpowers/sdd/new-mega-plan/task-8-report.md`

**Raw output:**
```diff
-**Concerns:** None. Positive-short Warn uses new key `Pipeline.LastTrackShort`; failure path keeps `Pipeline.LastTrackTooShort`. No public model or unrelated formatting changed.
+**Concerns:** Runtime acceptance remains **BLOCKED** pending the P3.3 harness. The three acceptance cases — 20 s final track → Complete, 0-byte final track → non-Complete, non-final track off by 3 s → non-Complete — are **not** runtime-verified here. Source/static checks and the build PASS; the runtime outcome is owned by **P3.3** (durable state/completeness harness with real FLAC fixtures + sox-backed duration). Positive-short Warn uses new key `Pipeline.LastTrackShort`; failure path keeps `Pipeline.LastTrackTooShort`. No public model or unrelated formatting changed.
```

**Result: PASS.** Contradictory `Concerns: None` replaced with explicit BLOCKED status; no runtime acceptance claimed; source/plan/media untouched.
