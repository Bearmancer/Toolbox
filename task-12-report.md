# Task 12 — P1.7 Stripper Exception Containment & Input-Size Repair

**Branch:** sacd-completion-v2 | **HEAD:** 51b7723 → implementation commit  
**Date:** 2026-08-16

## Summary

Replaced throwing `DffMetadataStripper` internals with `ErrorOr<T>` returns. Corrupt/odd DFF stripper failure now becomes per-disc `ErrorOr` result; batch continues via P1.1 boundary. Input `ckDataSize` mismatch warns and allows scan/copy to proceed (repair path). Output validation remains hard failure. `OperationCanceledException` propagation preserved. Partial output cleanup via `finally` preserved.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `src/Services/Audio/DffMetadataStripper.cs` | 88 | `HasId3Chunk` → `ErrorOr<bool>`, `ScanAsync`/`ScanChunksAsync`/`CopyChunksAsync`/`ReadChunkAsync` → `ErrorOr<T>`, `ValidateDffHeader` warns on size mismatch instead of throw |
| `src/Services/Audio/DsdConvertService.cs` | 5 | `PrepareDffAsync` handles `ErrorOr<bool>` from `HasId3Chunk` |
| `src/Core/Errors.cs` | 3 | Added `Errors.Audio.StripFailed(file, reason)` factory |

## Subtask Results

### 1. HasId3Chunk → ErrorOr\<bool\>

**Diff:** `DffMetadataStripper.cs:14-33`

- `HasId3Chunk(string)` now returns `ErrorOr<bool>` instead of `bool`
- Catches all exceptions except `OperationCanceledException` (re-thrown)
- Returns `Errors.Audio.StripFailed(dffPath, ex.Message)` on failure
- Non-existent file returns `false` (no error) — unchanged

```
Before: public static bool HasId3Chunk(string dffPath)
After:  public static ErrorOr<bool> HasId3Chunk(string dffPath)
```

**Synthetic test:** PASS — valid DFF→false, ID3 DFF→true, tiny→error, no-FRM8→error, missing→false  
**Output evidence:** Test driver 13/13 pass

### 2. ScanAsync/ScanChunksAsync/ReadChunkAsync → ErrorOr

**Diff:** `DffMetadataStripper.cs:128-232`

All internal scanning methods return `ErrorOr<T>` instead of throwing `InvalidDataException`:

| Method | Before | After |
|--------|--------|-------|
| `ScanAsync` | `Task<bool>`, throws | `Task<ErrorOr<bool>>`, returns error |
| `ScanChunksAsync` | `Task<bool>`, throws | `Task<ErrorOr<bool>>`, returns error |
| `ReadChunkAsync` | `Task<Chunk>`, throws | `Task<ErrorOr<Chunk>>`, returns error |
| `CopyChunksAsync` | `Task`, throws | `Task<ErrorOr<Success>>`, returns error |

Error messages preserved: "File too small to be valid DSDIFF", "DSDIFF chunk header is truncated", etc.

**Synthetic test:** PASS — tiny file, no-FRM8 both return errors  
**Build:** `dotnet build Toolbox.slnx --no-restore --no-incremental` → 0 errors, 0 warnings

### 3. Input ckDataSize Mismatch → Warn + Repair

**Diff:** `DffMetadataStripper.cs:234-250`

`ValidateDffHeader` changed from `void` (throwing) to `ErrorOr<Success>`:

- FRM8 magic mismatch → error (hard failure, unchanged)
- DSD form type mismatch → error (hard failure, unchanged)
- **ckDataSize mismatch → `Telemetry.Warn` + continue** (was: throw)

Warning format: `DffMetadataStripper.InputSizeMismatch declared={Declared} actual={Actual} — will scan physical chunk bounds`

The scanner then uses physical chunk walks (which use their own boundary checks) to read/copy data. Output rewrite can repair the header.

**Synthetic test:** PASS — size-mismatch DFF scans and strips without error  
**Output evidence:** `StripId3TagsAsync: size mismatch → no error (warn + repair)`

### 4. Output Validation Remains Hard Failure

**Diff:** `DffMetadataStripper.cs:69-88` (inside `StripId3TagsAsync`)

Output validation unchanged — exceptions caught by outer `catch (Exception ex) when (ex is not OperationCanceledException)`:

- Even-length check: `throw new InvalidDataException("Filtered DFF length is not even")`
- FRM8 size round-trip: `throw new InvalidDataException("Filtered DFF FRM8 size does not match output length")`
- PROP even-length: `Errors.Audio.StripFailed` (now via ErrorOr return from CopyChunksAsync)

These remain hard failures that produce `ErrorOr<string>` error, cleaning up partial output via `finally`.

**Synthetic test:** N/A — synthetic DFFs are structurally valid; output validation tested with real 3.3GB/Disc3 (P3.4/P4 harness, BLOCKED)

### 5. Cleanup & Cancellation Preservation

**Diff:** `DffMetadataStripper.cs:99-125`

`finally` block unchanged — deletes `cleanPath` when `outputCreated && !completed`. This covers:

- New `ErrorOr` failure paths from `ScanAsync` (pre-strip failure → no output created → no cleanup needed)
- New `ErrorOr` failure paths from `CopyChunksAsync` (mid-strip failure → output created → finally deletes)
- `OperationCanceledException` propagation → `completed` stays false → finally deletes partial output

**Synthetic test:** PASS — `StripId3TagsAsync: partial output cleaned up on failure`  
**Synthetic test:** PASS — `StripId3TagsAsync: cancelled → OperationCanceledException propagated`

## Build Verification

```
dotnet build Toolbox.slnx --no-restore --no-incremental
  Core -> artifacts\bin\Core\debug\Core.dll
  Audio -> artifacts\bin\Audio\debug\Audio.dll
  LastFm -> artifacts\bin\LastFm\debug\LastFm.dll
  Azure -> artifacts\bin\Azure\debug\Azure.dll
  Google -> artifacts\bin\Google\debug\Google.dll
  CLI -> artifacts\bin\CLI\debug\CLI.dll
  App -> artifacts\bin\App\debug\App.dll
Build succeeded. 0 Warning(s) 0 Error(s)
```

## Synthetic Test Summary

| # | Test | Result |
|---|------|--------|
| 1 | HasId3Chunk: valid DFF without ID3 → false | PASS |
| 2 | HasId3Chunk: valid DFF with ID3 → true | PASS |
| 3 | HasId3Chunk: too-small file → error | PASS |
| 4 | HasId3Chunk: no FRM8 → error | PASS |
| 5 | HasId3Chunk: missing file → false | PASS |
| 6 | HasId3Chunk: size mismatch → no error (warn + continue) | PASS |
| 7 | StripId3TagsAsync: too-small → error (not throw) | PASS |
| 8 | StripId3TagsAsync: no-FRM8 → error (not throw) | PASS |
| 9 | StripId3TagsAsync: no ID3 → original path | PASS |
| 10 | StripId3TagsAsync: ID3 present → clean path | PASS |
| 11 | StripId3TagsAsync: cancelled → OperationCanceledException | PASS |
| 12 | StripId3TagsAsync: size mismatch → no error (warn + repair) | PASS |
| 13 | StripId3TagsAsync: partial output cleaned up on failure | PASS |

**13/13 PASS**

## Concerns

1. **Real 3.3GB/Disc3 runtime:** BLOCKED — owner P3.4/P4 harness. Synthetic DFFs cover API contract and error containment, but physical large-file behavior, output validation on real data, and chunk boundary edge cases need P3.4 validation.
2. **PROP chunk internal padding:** Synthetic PROP body assumes CHNL(2 bytes) needs no padding. Real DFFs with odd-length PROP sub-chunks may exercise additional paths. P3.4 covers this.
3. **Input size repair:** Warn + continue means the scanner trusts physical chunk walks over declared size. If physical chunks are also corrupt (truncated data), the scanner returns an error via `ReadChunkAsync` boundary check. This is correct behavior — declared size mismatch is warning, corrupt chunks are errors.
