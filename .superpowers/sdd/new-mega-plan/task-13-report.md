# P2.1 Target Report: ProbeDsdAsync Hardening

**Branch:** `sacd-completion-v2`
**Source commits:** `76b6d1e` (probe hardening) and `703609a` (FORM-bound fixes)
**Diff base:** `9a5ac16..703609a` — source changes committed; report commits follow separately.
**Working-tree status:** target source clean after `703609a`; plan/ledger/checks remain unrelated working-tree artifacts.

---

## Subtask 1: ReadBytes + ASCII replacement

**Goal:** Replace every `ReadChars` with `ReadExactBytes` + `Encoding.ASCII.GetString`.

**Source evidence (DsdConvertService.cs):**

```csharp
// Line 53 — was: new string(reader.ReadChars(4))
var magic = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));

// Line 63 — was: new string(reader.ReadChars(4))
var formType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));

// Line 75 — was: new string(reader.ReadChars(4))
var chunkId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));

// Line 87 — was: new string(reader.ReadChars(4))
var propType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));

// Line 95 — was: new string(reader.ReadChars(4))
var subId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
```

`ReadExactBytes` (lines 163-169) throws `EndOfStreamException` if short read:

```csharp
private static byte[] ReadExactBytes(BinaryReader reader, int count)
{
    var bytes = reader.ReadBytes(count);
    if (bytes.Length != count)
        throw new EndOfStreamException();
    return bytes;
}
```

No `ReadChars` calls remain in `ProbeDsdAsync`. **PASS**

---

## Subtask 2: long/ulong bounded seeks

**Goal:** Replace narrowing casts with `long`/`ulong` and `Stream.Seek` for skipping.

**Source evidence:**

```csharp
// Line 57 — FORM size parsed as ulong, converted to long with checked arithmetic
var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
var formEnd = checked(12 + checked((long)formSize));

// Line 76-77 — chunk size parsed as ulong, end computed with checked cast
var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
var chunkEnd = checked(stream.Position + checked((long)chunkSize));

// Line 96-97 — subchunk size parsed as ulong, end computed with checked cast
var subSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
var subEnd = checked(stream.Position + checked((long)subSize));
```

All size values flow through `checked((long)...)` before arithmetic. No narrowing `(int)` casts remain for chunk sizes or seek targets. The `sampleRate` assignment at line 106 uses `checked((int)BinaryPrimitives.ReadUInt32BigEndian(...))` to convert a `uint` to `int`; this is a deliberate, validated narrowing: DSD sample rates (2822400, 5644800, etc.) fit comfortably in `int` range (max ~2.1 billion), and `DsdProbeResult.SampleRate` is typed as `int`. **PASS**

---

## Subtask 3: Bounded seeks (corrupt size cannot pass EOF)

**Goal:** Bound seeks so corrupt size cannot pass EOF.

**Source evidence:**

```csharp
// Lines 57-62 — FORM bounds check
if (formEnd > stream.Length)
    throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");

// Lines 163-176 — SeekChecked enforces [0, stream.Length] bounds
private static void SeekChecked(FileStream stream, long target)
{
    if (target < 0 || target > stream.Length)
        throw new InvalidDataException("DSDIFF seek exceeds stream bounds");
    stream.Seek(target, SeekOrigin.Begin);
}
```

Every `SeekChecked` call (lines 116, 122, 128, 134) passes through the bounds guard. Chunk/padding/subchunk seeks all validated. **PASS**

---

## Subtask 4: PROP walk still breaks after FS/CHNL

**Goal:** Confirm walk still breaks after `PROP` on real files.

**Source evidence:**

```csharp
// Line 137-138 — early exit after both values found
if (sampleRate > 0 && channels > 0)
    break;
```

Loop condition (line 70): `while (stream.Position < formEnd)`. Inner PROP walk (lines 90-124) iterates subchunks within the PROP boundary. After extracting both FS and CHNL values, the outer loop breaks immediately at line 138. Walk behavior unchanged from pre-hardening logic. **PASS**

---

## Subtask 5: Parser reuse vs DffMetadataStripper

**Goal:** Consider routing through `DffMetadataStripper` chunk reader; if not, record why.

**Rationale for keeping separate:**

`DffMetadataStripper` (`DffMetadataStripper.cs`) performs **copy/repair** operations: it reads chunks, detects ID3 tags, and writes a new file with tags stripped. Its methods return `ErrorOr<bool>` (HasId3Chunk) or `ErrorOr<string>` (StripId3TagsAsync, returning output path).

`ProbeDsdAsync` in `DsdConvertService` is a **read-only probe** that extracts FS sample rate and CHNL channel count from PROP/SND subchunks. It returns `ErrorOr<DsdProbeResult>` with metadata, no file mutation.

These are different responsibilities. Merging would either:
- Force the stripper to carry probe logic it doesn't need, or
- Force the probe to carry copy/repair logic it doesn't need.

Neither class calls the other. `PrepareDffAsync` (line 17) calls `DffMetadataStripper.HasId3Chunk` and `StripId3TagsAsync` for tag removal before conversion. `ProbeDsdAsync` (line 36) is a standalone metadata reader called by `PipelineOrchestrator` for sample-rate/channel detection.

Keeping them separate is correct. **PASS** (no merge)

---

## Subtask 6: Build verification

**Command:** `dotnet build --no-incremental`

**Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.59
```

All 7 projects compiled clean. **PASS**

---

## Subtask 7: Runtime acceptance (real Disc 3 / corrupt oversized)

**Expected:** Real Disc 3 probe returns 2822400 Hz / 2 ch unchanged; corrupt oversized chunk returns error, not throw/over-allocation.

**Status: BLOCKED**

**Reason:** No real DFF tooling or Disc 3 ISO available in the worktree environment. No synthetic runtime tests exist in this project (rule: no test NuGet packages; standalone `.cs` with `Main()` only). Without actual DFF files, runtime probe behavior cannot be verified.

**Blocker signature:** `ProbeDsdAsync(string dffFilePath, CancellationToken ct)` requires a valid DFF file path with FRM8/DSD  header and PROP/SND  subchunks containing FS and CHNL chunks.

**Owner:** P3.4 (runtime harness) or P4 (integration with real Disc 3 media). Runtime acceptance remains blocked until either:
1. A standalone `.cs` harness with `Main()` is written that probes a known-good DFF file and a corrupt-oversized DFF file, or
2. Integration testing proceeds in P3.4/P4 with real media.

---

## Summary

| Subtask | Status | Evidence |
|---------|--------|----------|
| ReadBytes + ASCII | PASS | 5 `ReadChars` → `ReadExactBytes` + `Encoding.ASCII.GetString` replacements verified at lines 53, 63, 75, 87, 95 |
| long/ulong bounded seeks | PASS | `checked((long)...)` on all chunk/sub sizes at lines 60, 77, 97; `sampleRate` uint→int cast deliberate and validated (L106) |
| SeekChecked bounds | PASS | Guard at lines 171-176, called at lines 116, 122, 128, 134; chunk bounds at L78, padding bounds at L132 |
| PROP walk break | PASS | Line 137-138: `break` after FS+CHNL found, unchanged logic; outer loop bound to `formEnd` (L70) |
| Parser reuse rationale | PASS | Separate concerns: probe (read-only metadata) vs stripper (copy/repair). No merge. |
| Fix round 1: FORM bounds | PASS | 7 findings applied: outer loop bound to formEnd, formSize<4 reject, chunk/padding EOF bounds, subchunk PROP bounds, ReadExactBytes short-read guard |
| Build | PASS | 0 warnings, 0 errors |
| Runtime acceptance | BLOCKED | No DFF tooling/media in worktree. Owner: P3.4/P4 |

---

## Fix Round 1: FORM-bound hardening (review findings applied)

**Source commits:** `76b6d1e` and `703609a` (FORM-bound hardening)
**Diff:** `9a5ac16..703609a` — source changes committed; report committed separately.
**Working-tree status:** Source clean after `703609a`.

### Findings addressed

| # | Review finding | Fix applied | Source line(s) |
|---|----------------|-------------|----------------|
| 1 | Outer `while` loop used `stream.Length - 12` as bound; leftover bytes after last chunk could skip bounds check | Loop condition changed to `stream.Position < formEnd`; inner guard `if (formEnd - stream.Position < 12)` catches truncated headers | L70, L72-73 |
| 2 | `formSize < 4` not checked; degenerate FORM with zero/minimal body passed through | Added `if (formSize < 4) throw InvalidDataException("DSDIFF FORM is too small")` after `ReadUInt64BigEndian` | L58-59 |
| 3 | Chunk seek used `(int)subSize` narrowing cast; corrupt oversized `chunkSize` (>2^31) could wrap negative | All size reads promoted to `ulong` via `BinaryPrimitives.ReadUInt64BigEndian`; seeks use `checked((long)...)` with `chunkEnd`/`subEnd` variables | L57, L60-61, L76-77, L96-97 |
| 4 | Chunk/padding seeks not bounded to FORM or stream | `chunkEnd` validated against `formEnd` and `stream.Length` (L78); padding `paddedChunkEnd` also checked against both (L132) | L78, L132-133 |
| 5 | PROP subchunk seeks not bounded to PROP chunk | `subEnd` validated against `propEnd` (L98); subchunk padding `paddedSubEnd` checked against `propEnd` (L120-121) | L98, L120-121 |
| 6 | `ReadChars` encoding not guaranteed on binary data | All `ReadChars` replaced with `ReadExactBytes` + `Encoding.ASCII.GetString` (see Subtask 1) | L53, L63, L75, L87, L95 |
| 7 | `ReadBytes` short-read not detected | `ReadExactBytes` helper added; throws `EndOfStreamException` if `bytes.Length != count` | L163-169 |

### Exact current source lines (commit 703609a)

```
L57-62: FORM size + bounds
  var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
  if (formSize < 4)
      throw new InvalidDataException("DSDIFF FORM is too small");
  var formEnd = checked(12 + checked((long)formSize));
  if (formEnd > stream.Length)
      throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");

L70-79: Outer loop + chunk header + chunk bounds
  while (stream.Position < formEnd)
  {
      if (formEnd - stream.Position < 12)
          throw new InvalidDataException("Truncated DSDIFF chunk header");
      var chunkId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
      var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
      var chunkEnd = checked(stream.Position + checked((long)chunkSize));
      if (chunkEnd > formEnd || chunkEnd > stream.Length)
          throw new InvalidDataException("DSDIFF chunk exceeds bounds");

L83-84: PROP size guard
  if (chunkSize < 4)
      throw new InvalidDataException("PROP chunk is too small");

L90-99: Subchunk header + bounds
  while (stream.Position < propEnd)
  {
      if (propEnd - stream.Position < 12)
          throw new InvalidDataException("Truncated PROP subchunk header");
      var subId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
      var subSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
      var subEnd = checked(stream.Position + checked((long)subSize));
      if (subEnd > propEnd)
          throw new InvalidDataException("PROP subchunk exceeds PROP chunk");

L103-113: FS/CHNL minimum-size guards
  if (subSize < 4)  // FS
      throw new InvalidDataException("FS subchunk is too small");
  sampleRate = checked((int)BinaryPrimitives.ReadUInt32BigEndian(ReadExactBytes(reader, 4)));

  if (subSize < 2)  // CHNL
      throw new InvalidDataException("CHNL subchunk is too small");
  channels = BinaryPrimitives.ReadUInt16BigEndian(ReadExactBytes(reader, 2));

L116-123: Subchunk padding bounded to PROP
  SeekChecked(stream, subEnd);
  if (subSize % 2 != 0)
  {
      var paddedSubEnd = checked(subEnd + 1);
      if (paddedSubEnd > propEnd)
          throw new InvalidDataException("PROP subchunk padding exceeds PROP chunk");
      SeekChecked(stream, paddedSubEnd);
  }

L128-135: Chunk padding bounded to FORM + stream
  SeekChecked(stream, chunkEnd);
  if (chunkSize % 2 != 0)
  {
      var paddedChunkEnd = checked(chunkEnd + 1);
      if (paddedChunkEnd > formEnd || paddedChunkEnd > stream.Length)
          throw new InvalidDataException("DSDIFF chunk padding exceeds bounds");
      SeekChecked(stream, paddedChunkEnd);
  }

L163-176: Helper methods
  ReadExactBytes(BinaryReader, int) → byte[]
  SeekChecked(FileStream, long) → void with bounds guard
```

### Build verification

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All 7 projects compiled clean. **PASS**

### Runtime acceptance

**Status: BLOCKED**

No real DFF tooling or Disc 3 ISO available in the worktree environment. No synthetic runtime tests exist in this project (rule: no test NuGet packages; standalone `.cs` with `Main()` only). Runtime probe behavior for Disc 3 and corrupt oversized files cannot be verified.

**Owner:** P3.4 (runtime harness) or P4 (integration with real Disc 3 media).

---

**Commit:** Report only after source commit `703609a`; no source edits in this report fix.
