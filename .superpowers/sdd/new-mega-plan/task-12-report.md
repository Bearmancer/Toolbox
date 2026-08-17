# P1.7 — Stripper Exception Containment & Input-Size Repair — Report

**Branch:** sacd-completion-v2 | **Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
**Date:** 2026-08-16 | **Status:** PASS (source + synthetic checks), runtime BLOCKED

## Summary

Replaced throwing `DffMetadataStripper` internals with `ErrorOr<T>` returns. Corrupt/odd DFF stripper failure now becomes per-disc `ErrorOr` result; batch continues via P1.1 boundary. Input `ckDataSize` mismatch warns and allows scan/copy to proceed (repair path). Output validation remains hard failure. `OperationCanceledException` propagation preserved. Partial output cleanup via `finally` preserved. Synthetic DFF test suite: **13/13 PASS**.

## Changed files

| Commit | File | Lines | Nature |
|---|---|---|---|
| `7100782` | `src/Services/Audio/DffMetadataStripper.cs` | +88/−28 | `HasId3Chunk` → `ErrorOr<bool>`, internal methods → `ErrorOr<T>`, input size mismatch warn+repair |
| `7100782` | `src/Services/Audio/DsdConvertService.cs` | +5/−1 | `PrepareDffAsync` handles `ErrorOr<bool>` from `HasId3Chunk` |
| `7100782` | `src/Core/Errors.cs` | +3 | `Errors.Audio.StripFailed(file, reason)` factory |

## Subtask 1 — ErrorOr containment (`HasId3Chunk`)

**API change:** `public static bool HasId3Chunk(string)` → `public static ErrorOr<bool> HasId3Chunk(string)`

**Diff (DffMetadataStripper.cs:14-37):**
```csharp
public static ErrorOr<bool> HasId3Chunk(string dffPath)
{
    if (!File.Exists(dffPath))
        return false;

    try
    {
        using FileStream input = File.OpenRead(dffPath);
        return ScanAsync(input, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        Telemetry.Error(
            "DffMetadataStripper.ScanFailed file={File} error={Error}",
            LogPaths.Format(dffPath),
            ex.Message
        );
        return Errors.Audio.StripFailed(dffPath, ex.Message);
    }
}
```

Internal methods changed: `ScanAsync`, `ScanChunksAsync`, `ReadChunkAsync`, `CopyChunksAsync` all return `ErrorOr<T>` instead of throwing `InvalidDataException`.

**Synthetic check output:**
```
  valid: IsError=False Value=False
  PASS: T1a: valid DFF no ID3 → false
  id3: IsError=False Value=True
  PASS: T1b: valid DFF with ID3 → true
  tiny: IsError=True
  PASS: T1c: tiny file → error (not throw)
  nofrm8: IsError=True
  PASS: T1d: no FRM8 → error (not throw)
  missing: IsError=False Value=False
  PASS: T1e: missing file → false
```

**Result: PASS**

## Subtask 2 — Input mismatch warning/repair vs output hard validation

**Diff (DffMetadataStripper.cs:259-278):** `ValidateDffHeader` changed from `void` (throwing) to `ErrorOr<Success>`:
```csharp
private static ErrorOr<Success> ValidateDffHeader(byte[] header, long length)
{
    if (Encoding.ASCII.GetString(header, 0, 4) != FormId)
        return Errors.Audio.StripFailed("input", "DSDIFF file does not start with FRM8");
    if (Encoding.ASCII.GetString(header, 12, 4) != FormType)
        return Errors.Audio.StripFailed("input", "DSDIFF FRM8 form type is not DSD");

    var declaredSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
    var actualSize = (ulong)(length - HeaderSize);
    if (declaredSize != actualSize)
    {
        Telemetry.Warn(
            "DffMetadataStripper.InputSizeMismatch declared={Declared} actual={Actual} — will scan physical chunk bounds",
            declaredSize,
            actualSize
        );
    }

    return Result.Success;
}
```

**Classification:**
- Input mismatch (`declaredSize != actualSize`) → `Telemetry.Warn` + continue (repair via physical chunk scan)
- FRM8 magic mismatch → `Error` (hard failure)
- DSD form type mismatch → `Error` (hard failure)
- Output even-length check (L80-82) → `InvalidDataException` (hard failure, caught by outer catch)
- Output FRM8 size round-trip (L94-95) → `InvalidDataException` (hard failure, caught by outer catch)

**Synthetic check output:**
```
  mismatch: IsError=False
  PASS: T2a: size mismatch → no error (warn + continue)
  strip mismatch: IsError=False
  PASS: T2b: strip size mismatch → no error (warn + repair)
  strip tiny: IsError=True
  PASS: T3a: strip tiny → error (hard failure)
  strip nofrm8: IsError=True
  PASS: T3b: strip no-FRM8 → error (hard failure)
```

**Result: PASS**

## Subtask 3 — `finally` partial-output cleanup

**Diff (DffMetadataStripper.cs:116-133):** `finally` block unchanged — deletes `cleanPath` when `outputCreated && !completed`.

Failure paths that trigger cleanup:
1. `ScanAsync` returns error (before output created → no cleanup needed)
2. `CopyChunksAsync` returns error (output created → finally deletes)
3. Output validation throws (output created → finally deletes)
4. `OperationCanceledException` (output created → finally deletes)

**Synthetic check output:**
```
  clean file exists: False
  PASS: T4: partial output cleaned up on failure
```

**Result: PASS**

## Subtask 4 — Cancellation filter

**Diff (DffMetadataStripper.cs:24-27):**
```csharp
catch (OperationCanceledException)
{
    throw;
}
```

Added in `HasId3Chunk` before the general `catch (Exception ex)`. The outer `StripId3TagsAsync` already has `catch (Exception ex) when (ex is not OperationCanceledException)` at L107.

**Synthetic check output:**
```
  PASS: T5: OperationCanceledException propagated (not caught as failure)
```

**Result: PASS**

## Subtask 5 — Batch continuation (per-disc error)

**Diff (DsdConvertService.cs:22-26):**
```csharp
ErrorOr<bool> hasId3Result = DffMetadataStripper.HasId3Chunk(dffFilePath);
if (hasId3Result.IsError)
    return hasId3Result.Errors;
if (!hasId3Result.Value)
    return dffFilePath;
```

`PrepareDffAsync` propagates `ErrorOr` errors. The caller `PipelineOrchestrator.ConvertDiscAsync` (L385-387) already handles `ErrorOr`:
```csharp
ErrorOr<string> preparedDff = await convertService.PrepareDffAsync(dffFile, dffDir, ct);
if (preparedDff.IsError)
    return preparedDff.Errors;
```

This flows back to `ProcessIsoAsync` which returns `ErrorOr<ProcessedDisc>`, and the batch loop in `RunAsync` (L108-128) increments `failed++` and continues to next disc.

**Synthetic check output:**
```
  strip id3: IsError=False Value=C:\...\outClean\with_id3_clean.dff
  PASS: T5b: ID3 present → clean path returned
  strip no-id3: IsError=False Value=C:\...\valid.dff
  PASS: T5c: no ID3 → original path returned
```

**Result: PASS**

## Synthetic test suite (full output)

```
=== Build synthetic DFF files ===
  valid.dff: 138 bytes
  with_id3.dff: 166 bytes
  tiny.dff: 3 bytes
  nofrm8.dff: 64 bytes
  mismatch.dff: 138 bytes

=== Subtask 1: ErrorOr containment (HasId3Chunk) ===
  PASS: T1a: valid DFF no ID3 → false
  PASS: T1b: valid DFF with ID3 → true
  PASS: T1c: tiny file → error (not throw)
  PASS: T1d: no FRM8 → error (not throw)
  PASS: T1e: missing file → false

=== Subtask 2: Input mismatch warning/repair ===
  PASS: T2a: size mismatch → no error (warn + continue)
  PASS: T2b: strip size mismatch → no error (warn + repair)

=== Subtask 3: Output validation hard failure (structure) ===
  PASS: T3a: strip tiny → error (hard failure)
  PASS: T3b: strip no-FRM8 → error (hard failure)

=== Subtask 4: Finally partial cleanup ===
  PASS: T4: partial output cleaned up on failure

=== Subtask 5: Cancellation propagation ===
  PASS: T5: OperationCanceledException propagated (not caught as failure)

=== Subtask 5 (batch): StripId3TagsAsync on valid ID3 DFF ===
  PASS: T5b: ID3 present → clean path returned

=== Subtask 5 (batch): No-ID3 passthrough ===
  PASS: T5c: no ID3 → original path returned

=== Results: 13 passed, 0 failed ===
```

**Command:** `dotnet run --project P17Verify.csproj` → exit 0, 13/13 PASS.

## Build evidence

```
dotnet build Toolbox.slnx --no-restore --no-incremental
  Core -> artifacts\bin\Core\debug\Core.dll
  Audio -> artifacts\bin\Audio\debug\Audio.dll
  LastFm -> artifacts\bin\LastFm\debug\LastFm.dll
  Azure -> artifacts\bin\Azure\debug\Azure.dll
  Google -> artifacts\bin\Google\debug\Google.dll
  CLI -> artifacts\bin\CLI\debug\CLI.dll
  App -> artifacts\bin\App\debug\App.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## BLOCKED — Real 3.3GB/Disc3 runtime

`StripId3TagsAsync` is called from `DsdConvertService.PrepareDffAsync` which is called from `PipelineOrchestrator.ConvertDiscAsync` which is called from `ProcessIsoAsync` inside the batch loop. Full pipeline requires: real ISO files, `sacd_extract` / `saracon` / `sox` on PATH, 3.3GB+ DFF files.

**Blocker signature:** `private async Task<ErrorOr<Success>> ConvertDiscAsync(string, AudioOutputFormat, CancellationToken)` requires CUE parsing, DFF probing, gain calculation, and external tool execution. Not callable outside `PipelineOrchestrator`.

**Owner:** P3.4 (durable stripper suite) and P5.x (real media gates) will exercise this path end-to-end with real 3.3GB/Disc3 media.

**What synthetic tests cover:**
- API contract (`ErrorOr<bool>` return, not throw)
- Error containment (corrupt input → error, not exception)
- Input size mismatch (warn + continue)
- Output validation (hard failure on structural errors)
- Partial output cleanup (`finally` block)
- Cancellation propagation (`OperationCanceledException`)
- Batch continuation path (error flows through `PrepareDffAsync` → `ConvertDiscAsync` → `ProcessIsoAsync` → batch loop)

**What synthetic tests cannot cover:**
- Physical 3.3GB file I/O behavior
- Real chunk boundary edge cases at scale
- Output FRM8 size round-trip validation on stripped output
- Interaction with `CalculateGainAsync` / `ConvertAndSplitAsync` downstream

## Concerns

1. **PROP chunk internal padding:** Synthetic PROP body assumes CHNL(2 bytes) needs no padding. Real DFFs with odd-length PROP sub-chunks exercise additional padding paths. P3.4 covers this.

2. **Input size repair semantics:** Warn + continue means the scanner trusts physical chunk walks over declared size. If physical chunks are also corrupt (truncated data), the scanner returns an error via `ReadChunkAsync` boundary check (`endPosition > end`). This is correct — declared size mismatch is a warning, corrupt chunks are errors.

3. **`HasId3Chunk` sync-over-async:** The method calls `.GetAwaiter().GetResult()` on an async scan. This is unchanged from the prior implementation. The `OperationCanceledException` catch ensures cancellation propagates even through the sync-over-async bridge.
