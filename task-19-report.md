# Task 19 — P3.4 DffMetadataStripper Strip Cases

**Branch:** sacd-completion-v2 | **Baseline:** c559b62 → ded695a | **Date:** 2026-08-17

## Summary

Seven requirement-cited cases for P3.4 `DffMetadataStripper` ID3 stripping, PROP rewrite, error handling, size-mismatch repair, and real-media BLOCKED. Cases 1-6 execute synthetic DFF byte fixtures built in memory against `DffMetadataStripper.StripId3TagsAsync` and `HasId3Chunk`, asserting ID3 removal, file size/padding, PROP size rewrite, truncation/zero-size errors, short-form-size warn+repair, and no-throw semantics. Case 7 records real Disc3 streamed test BLOCKED (path absent, `File.ReadAllBytes` prohibited, no 3.33GB evidence). Result: **23 PASS + 2 BLOCKED**. Clean 0, forced nonzero. Telemetry Fatal. Temp teardown in finally. No new null literals, no nullable-forgiving operators, no production source edits.

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| `checks/Program.cs` | 900 | +7 P3.4 cases, +8 fixture helpers, +7 case method invocations |
| `task-19-report.md` | — | This report (repo root) |

## Harness Output

```
RESULTS: 23 passed, 0 failed, 2 blocked, 25 total
EXIT: 0
```

`--force-fail`: `RESULTS: 23 passed, 1 failed, 2 blocked, 26 total` → EXIT: 1 (forced nonzero verified).

## Subtask Results

### 1. P3.4.1 — Strip Four Top-Level ID3

**Citation:** `DffMetadataStripper ScanAsync L136-183, CopyChunksAsync L186-241`
**Fixture:** 104-byte synthetic DFF: FRM8 + DSD type + 4 × ID3 chunks (10 bytes data each, 22 bytes each = 88 bytes total)
**Expected Output:** 16-byte clean DFF: FRM8 [formSize=4] + DSD type only; all ID3 stripped
**Assertions:**
- Output exists, 16 bytes
- `HasId3Chunk` returns false
- FRM8 formSize field = 4, physical length − 12 = 4 (even)
**Result:** PASS

### 2. P3.4.2 — Odd Chunk Pad Preserved

**Citation:** `DffMetadataStripper CopyChunksAsync L186-241, ReadChunkAsync L243-257`
**Fixture:** 78-byte synthetic DFF: FRM8 + DSD type + ID3(10) + DATA(5 odd-pad) + ID3(10)
**Expected Output:** 34-byte clean DFF: FRM8 + DSD type + DATA(18: 12 header + 5 data + 1 pad)
**Assertions:**
- Output exists, 34 bytes (pad byte preserved)
- DATA chunk at offset 16, size field = 5
- Pad byte present (file includes byte 33)
**Result:** PASS

### 3. P3.4.3 — Nested PROP ID3 Removed, PROP Size Changed

**Citation:** `DffMetadataStripper ScanAsync L164-175, CopyChunksAsync L207-234`
**Fixture:** 70-byte synthetic DFF: FRM8 + DSD type + PROP [SND + ID3(10) + FS(4)]
**Expected Output:** 48-byte clean DFF: FRM8 + DSD type + PROP [SND + FS(4)] — ID3 removed, PROP data size rewritten from 42 → 20
**Assertions:**
- Output exists, 48 bytes
- `HasId3Chunk` returns false
- PROP chunk at offset 16, data size field = 20
**Result:** PASS

### 4. P3.4.4 — Truncated Input Error, No Partial Output

**Citation:** `DffMetadataStripper ReadChunkAsync L243-257, ValidateDffHeader L259-278`
**Fixture:** 48-byte truncated DFF: valid FRM8+DSD header, DATA chunk header claims 100 bytes but file ends at byte 48
**Expected:** Scan fails — DATA chunk exceeds parent boundary; `StripId3TagsAsync` returns error; no output directory created
**Assertions:**
- `result.IsError` is true
- Output directory does not exist (no partial output)
**Result:** PASS

### 5. P3.4.5 — Zero-Size PROP Midwalk Error, No Partial Output

**Citation:** `DffMetadataStripper ScanAsync L164-168`
**Fixture:** 28-byte synthetic DFF: FRM8 + DSD type + PROP [size=0]
**Expected:** Scan enters PROP, `chunk.Size (0) < 4` triggers "PROP chunk is missing property type" error; no output created
**Assertions:**
- `result.IsError` is true
- Output directory does not exist
**Result:** PASS

### 6. P3.4.6 — Input FRM8 Size Four Short, Warns/Repairs, No Throw

**Citation:** `DffMetadataStripper ValidateDffHeader L259-278, StripId3TagsAsync L39-133`
**Fixture:** 70-byte DFF: FRM8 declares formSize=54 (4 short of actual 58), contains ID3(10) + DATA(20)
**Expected:** `ValidateDffHeader` warns size mismatch but returns Success; scan uses physical bounds; ID3 stripped; output created; no exception thrown
**Assertions:**
- No exception thrown (try-catch around call)
- Output file exists
- `HasId3Chunk` returns false on output
**Result:** PASS

### 7. P3.4.7 — Real Disc3 Streamed Test, BLOCKED

**Citation:** `DffMetadataStripper StripId3TagsAsync L39-133, P3.4/P5 owner`
**Recorded Signature:**
```
DffMetadataStripper.StripId3TagsAsync(string, string, CancellationToken)
```
**Candidate Paths Searched:**
1. `%USERPROFILE%\Music\SACD\Disc3`
2. `%USERPROFILE%\Music\disc3`
3. `D:\SACD\Disc3`
4. `E:\SACD\Disc3`

**BLOCKED Reason:**
1. Real Disc3 DFF path absent from all candidate locations
2. `File.ReadAllBytes` prohibited on real media per task constraint
3. No 3.33GB streaming evidence available to claim PASS
4. Synthetic fixtures cover strip logic; real-media validation deferred to P5

**Result:** BLOCKED (documented)

## Fixture Ownership

| Case | Fixture Root | Cleanup |
|------|-------------|---------|
| P3.4.1 | `tempRoot/p341-four-id3` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.2 | `tempRoot/p342-odd-pad` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.3 | `tempRoot/p343-nested-prop` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.4 | `tempRoot/p344-truncated` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.5 | `tempRoot/p345-zero-prop` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.6 | `tempRoot/p346-short-size` | finally: `Directory.Delete(tempRoot, true)` |
| P3.4.7 | none (BLOCKED — no real media) | n/a |

All fixtures under system temp with hard boundary check (P3.1 R3). No ISO/media mutation. Synthetic DFF constructed from binary header spec via `BuildId3ChunkBytes`/`BuildDataChunkBytes`/`BuildDffWith*` helpers — no external audio files, no `File.ReadAllBytes`.

## Null/Bang Audit

- **0** new `null` literals introduced
- **0** new nullable-forgiving `!` operators
- **0** new `null!` assignments
- Existing legacy `null` in `Assert` parameter default (`string? error = null`) unaltered
- Existing legacy `Environment.ProcessPath!` (lines 129, 253, 274) unaltered
- New boolean negation uses prefix `!` on `bool` values (not nullable-forgiving): `!hasId3.IsError`, `!hasId3.Value`, `!Directory.Exists(...)`, `!result.IsError`
- Pattern matching used throughout: `result.IsError`, `is not null`, `is Type`

## Build

```
dotnet build checks/GuardChecks.csproj → succeeded (0 warnings, 0 errors)
dotnet run (clean) → RESULTS: 23 passed, 0 failed, 2 blocked, 25 total → EXIT: 0
dotnet run -- --force-fail → RESULTS: 23 passed, 1 failed, 2 blocked, 26 total → EXIT: 1
```
