# Task 13 Report — ProbeDsdAsync Hardening

**Branch:** sacd-completion-v2 (based on HEAD 9a5ac16 → current master)
**Date:** 2026-08-16
**Status:** PASS (source verification), BLOCKED (real Disc3 media)

---

## Subtask 1: Replace ReadChars with ReadBytes + Encoding.ASCII.GetString

**Command:** Manual code review + build verification
**Diff:** `src/Services/Audio/DsdConvertService.cs` — 5 ReadChars calls replaced

| Line | Before | After |
|------|--------|-------|
| 49 | `new string(reader.ReadChars(4))` | `Encoding.ASCII.GetString(ReadExactly(reader, 4))` |
| 54 | `new string(reader.ReadChars(4))` | `Encoding.ASCII.GetString(ReadExactly(reader, 4))` |
| 63 | `new string(reader.ReadChars(4))` | `Encoding.ASCII.GetString(ReadExactly(reader, 4))` |
| 68 | `new string(reader.ReadChars(4))` | `Encoding.ASCII.GetString(ReadExactly(reader, 4))` |
| 74 | `new string(reader.ReadChars(4))` | `Encoding.ASCII.GetString(ReadExactly(reader, 4))` |

**Rationale for not reusing DffMetadataStripper.ReadChunkAsync:** DffMetadataStripper's `ReadChunkAsync` returns a `Chunk` record with precomputed end positions, which is designed for streaming-copy operations. ProbeDsdAsync only needs to identify FS/CHNL subchunks and extract values — it doesn't need end-position tracking or copy logic. The `ReadExactly` helper added to DsdConvertService is minimal (11 lines) and avoids coupling the probe to the stripper's internal types.

**Short read handling:** `ReadExactly` throws `EndOfStreamException` on partial reads, which the existing `catch (Exception ex) when (ex is not OperationCanceledException)` block converts to `ErrorOr` error.

**Raw output:** Build succeeded, 0 errors, 0 warnings.
**PASS**

---

## Subtask 2: Bound chunk/subchunk skips against stream.Length, remove (int) casts

**Command:** Manual code review + build verification
**Diff:** 6 `(int)chunkSize` / `(int)subSize` casts removed

| Location | Before | After |
|----------|--------|-------|
| PROP subchunk FS skip | `reader.ReadBytes((int)subSize - 4)` | `stream.Seek((long)subSize - 4, SeekOrigin.Current)` |
| PROP subchunk CHNL skip | `reader.ReadBytes((int)subSize - 2)` | `stream.Seek((long)subSize - 2, SeekOrigin.Current)` |
| PROP subchunk default skip | `reader.ReadBytes((int)subSize)` | `stream.Seek((long)subSize, SeekOrigin.Current)` |
| PROP non-SND skip | `reader.ReadBytes((int)chunkSize - 4)` | `stream.Seek((long)chunkSize - 4, SeekOrigin.Current)` |
| Non-PROP chunk skip | `reader.ReadBytes((int)chunkSize)` | `stream.Seek((long)chunkSize, SeekOrigin.Current)` |

**Bounds checks added:**
- Chunk level: `chunkDataEnd = checked(stream.Position + (long)chunkSize); if (chunkDataEnd > stream.Length) → ErrorOr error`
- PROP subchunk level: `subDataEnd = checked(stream.Position + (long)subSize); if (subDataEnd > propEnd) → ErrorOr error`
- PROP property type: `if (chunkSize < 4) → ErrorOr error`

**Raw output:** Build succeeded, 0 errors, 0 warnings.
**PASS**

---

## Subtask 3: TDD — corrupt oversized chunk returns ErrorOr error, no throw/allocation

**Command:** `dotnet run --project tools/ProbeVerify/ProbeVerify.csproj`
**Diff:** New file `tools/ProbeVerify/Program.cs`

**Test results:**

```
  PASS  ValidDff
  PASS  OversizedChunk_ReturnsError
  PASS  OversizedChunk_NoThrow
  PASS  OversizedPropSubchunk_ReturnsError
  PASS  CorruptMagic_ReturnsError
  PASS  TruncatedHeader_ReturnsError
  PASS  TinyProp_ReturnsError
  PASS  NonSndProp_IgnoresNonSnd

Results: 8 passed, 0 failed
```

**Raw output:**
```
Results: 8 passed, 0 failed
Exit code: 0
```
**PASS**

---

## Subtask 4: PROP walk behavior preserved

**Command:** Test #8 (`NonSndProp_IgnoresNonSnd`) verifies non-SND PROP chunks are skipped and the subsequent SND PROP is parsed correctly.

Additionally, Test #1 (`ValidDff`) verifies the full PROP/SND walk extracts FS=2822400 and CHNL=2 from a well-formed DFF.

**Behavior preserved:**
- Non-SND PROP chunks are skipped (propType != "SND " → seek past PROP data)
- SND PROP subchunks are walked until FS and CHNL are found
- Break after both sampleRate > 0 and channels > 0
- Odd-padding bytes consumed after subchunks

**Raw output:** Build succeeded, 0 errors, 0 warnings. All tests pass.
**PASS**

---

## Subtask 5: Build clean, commit source only

**Command:** `dotnet build Toolbox.slnx --no-restore --no-incremental`

**Raw output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.90
```

**Staged files:**
```
 src/Services/Audio/DsdConvertService.cs |  71 +++++--
 tools/ProbeVerify/ProbeVerify.csproj    |  11 ++
 tools/ProbeVerify/Program.cs            | 326 ++++++++++++++++++++++++++++++++
 3 files changed, 394 insertions(+), 14 deletions(-)
```

**Real Disc3 probe:** BLOCKED — no SACD media or sacd_extract tooling available in this environment. Will be tested when P3.4 provides real DFF harness.

**PASS** (build), **BLOCKED** (real media verification)

---

## DffMetadataStripper Reuse Decision

**Decision:** Do NOT reuse DffMetadataStripper's reader. Keep standalone `ReadExactly` in DsdConvertService.

**Rationale:**
1. DffMetadataStripper's `ReadChunkAsync` returns a `Chunk` record with `DataStart`, `DataEnd`, `End` fields designed for streaming-copy operations. ProbeDsdAsync only needs to identify subchunks and extract values.
2. The `Chunk` record is private to DffMetadataStripper. Exposing it would require making it public, adding a coupling between two independent services.
3. The `ReadExactly` helper is 11 lines and trivially correct. It exists in both files as a local utility — not duplication, but independence.
4. ProbeDsdAsync's chunk walk is structurally different from DffMetadataStripper's: it breaks early on FS/CHNL, doesn't track end positions, and returns ErrorOr instead of throwing.

If the probe logic evolves to need full chunk tracking, extracting a shared DffChunkReader would be warranted. Current complexity doesn't justify it.
