# Review package: 9a5ac16..1b2dd72

## Commits
1b2dd72 docs(audio): P2.1 target report ΓÇö ProbeDsdAsync hardening
76b6d1e fix(audio): harden DSDIFF probing

## Files changed
 .superpowers/sdd/new-mega-plan/task-13-report.md | 177 +++++++++++++++++++++++
 src/Services/Audio/DsdConvertService.cs          |  96 +++++++-----
 2 files changed, 238 insertions(+), 35 deletions(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-13-report.md b/.superpowers/sdd/new-mega-plan/task-13-report.md
new file mode 100644
index 0000000..b3587b8
--- /dev/null
+++ b/.superpowers/sdd/new-mega-plan/task-13-report.md
@@ -0,0 +1,177 @@
+# P2.1 Target Report: ProbeDsdAsync Hardening
+
+**Branch:** `sacd-completion-v2`
+**Source commit:** `76b6d1e` (fix(audio): harden DSDIFF probing)
+**Diff base:** `9a5ac16..HEAD` ΓÇö 1 file changed, +61 / -35 lines
+
+---
+
+## Subtask 1: ReadBytes + ASCII replacement
+
+**Goal:** Replace every `ReadChars` with `ReadExactBytes` + `Encoding.ASCII.GetString`.
+
+**Source evidence (DsdConvertService.cs):**
+
+```csharp
+// Line 53 ΓÇö was: new string(reader.ReadChars(4))
+var magic = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+
+// Line 61 ΓÇö was: new string(reader.ReadChars(4))
+var formType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+
+// Line 73 ΓÇö was: new string(reader.ReadChars(4))
+var chunkId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+
+// Line 83 ΓÇö was: new string(reader.ReadChars(4))
+var propType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+
+// Line 91 ΓÇö was: new string(reader.ReadChars(4))
+var subId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+```
+
+`ReadExactBytes` (lines 154-160) throws `EndOfStreamException` if short read:
+
+```csharp
+private static byte[] ReadExactBytes(BinaryReader reader, int count)
+{
+    var bytes = reader.ReadBytes(count);
+    if (bytes.Length != count)
+        throw new EndOfStreamException();
+    return bytes;
+}
+```
+
+No `ReadChars` calls remain in `ProbeDsdAsync`. **PASS**
+
+---
+
+## Subtask 2: long/ulong bounded seeks
+
+**Goal:** Replace narrowing casts with `long`/`ulong` and `Stream.Seek` for skipping.
+
+**Source evidence:**
+
+```csharp
+// Line 57 ΓÇö FORM size parsed as ulong, converted to long with checked arithmetic
+var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+var formEnd = checked(12 + checked((long)formSize));
+
+// Line 74-75 ΓÇö chunk size parsed as ulong, end computed with checked cast
+var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+var chunkEnd = checked(stream.Position + checked((long)chunkSize));
+
+// Line 92-93 ΓÇö subchunk size parsed as ulong, end computed with checked cast
+var subSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+var subEnd = checked(stream.Position + checked((long)subSize));
+```
+
+All size values flow through `checked((long)...)` before arithmetic. No narrowing `(int)subSize` casts remain for seek targets. **PASS**
+
+---
+
+## Subtask 3: Bounded seeks (corrupt size cannot pass EOF)
+
+**Goal:** Bound seeks so corrupt size cannot pass EOF.
+
+**Source evidence:**
+
+```csharp
+// Line 59 ΓÇö FORM bounds check
+if (formEnd > stream.Length)
+    throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");
+
+// Lines 162-167 ΓÇö SeekChecked enforces [0, stream.Length] bounds
+private static void SeekChecked(FileStream stream, long target)
+{
+    if (target < 0 || target > stream.Length)
+        throw new InvalidDataException("DSDIFF seek exceeds stream bounds");
+    stream.Seek(target, SeekOrigin.Begin);
+}
+```
+
+Every `SeekChecked` call (lines 112, 118, 124, 126) passes through the bounds guard. Chunk/padding/subchunk seeks all validated. **PASS**
+
+---
+
+## Subtask 4: PROP walk still breaks after FS/CHNL
+
+**Goal:** Confirm walk still breaks after `PROP` on real files.
+
+**Source evidence:**
+
+```csharp
+// Line 128-129 ΓÇö early exit after both values found
+if (sampleRate > 0 && channels > 0)
+    break;
+```
+
+Loop condition (line 68): `while (stream.Position < stream.Length)`. Inner PROP walk (lines 86-120) iterates subchunks within the PROP boundary. After extracting both FS and CHNL values, the outer loop breaks immediately at line 129. Walk behavior unchanged from pre-hardening logic. **PASS**
+
+---
+
+## Subtask 5: Parser reuse vs DffMetadataStripper
+
+**Goal:** Consider routing through `DffMetadataStripper` chunk reader; if not, record why.
+
+**Rationale for keeping separate:**
+
+`DffMetadataStripper` (`DffMetadataStripper.cs`) performs **copy/repair** operations: it reads chunks, detects ID3 tags, and writes a new file with tags stripped. Its methods return `ErrorOr<bool>` (HasId3Chunk) or `ErrorOr<string>` (StripId3TagsAsync, returning output path).
+
+`ProbeDsdAsync` in `DsdConvertService` is a **read-only probe** that extracts FS sample rate and CHNL channel count from PROP/SND subchunks. It returns `ErrorOr<DsdProbeResult>` with metadata, no file mutation.
+
+These are different responsibilities. Merging would either:
+- Force the stripper to carry probe logic it doesn't need, or
+- Force the probe to carry copy/repair logic it doesn't need.
+
+Neither class calls the other. `PrepareDffAsync` (line 17) calls `DffMetadataStripper.HasId3Chunk` and `StripId3TagsAsync` for tag removal before conversion. `ProbeDsdAsync` (line 36) is a standalone metadata reader called by `PipelineOrchestrator` for sample-rate/channel detection.
+
+Keeping them separate is correct. **PASS** (no merge)
+
+---
+
+## Subtask 6: Build verification
+
+**Command:** `dotnet build --no-incremental`
+
+**Output:**
+```
+Build succeeded.
+    0 Warning(s)
+    0 Error(s)
+
+Time Elapsed 00:00:04.08
+```
+
+All 7 projects compiled clean. **PASS**
+
+---
+
+## Subtask 7: Runtime acceptance (real Disc 3 / corrupt oversized)
+
+**Expected:** Real Disc 3 probe returns 2822400 Hz / 2 ch unchanged; corrupt oversized chunk returns error, not throw/over-allocation.
+
+**Status: BLOCKED**
+
+**Reason:** No real DFF tooling or Disc 3 ISO available in the worktree environment. No synthetic runtime tests exist in this project (rule: no test NuGet packages; standalone `.cs` with `Main()` only). Without actual DFF files, runtime probe behavior cannot be verified.
+
+**Blocker signature:** `ProbeDsdAsync(string dffFilePath, CancellationToken ct)` requires a valid DFF file path with FRM8/DSD  header and PROP/SND  subchunks containing FS and CHNL chunks.
+
+**Owner:** P3.4 (runtime harness) or P4 (integration with real Disc 3 media). Runtime acceptance remains blocked until either:
+1. A standalone `.cs` harness with `Main()` is written that probes a known-good DFF file and a corrupt-oversized DFF file, or
+2. Integration testing proceeds in P3.4/P4 with real media.
+
+---
+
+## Summary
+
+| Subtask | Status | Evidence |
+|---------|--------|----------|
+| ReadBytes + ASCII | PASS | 5 `ReadChars` ΓåÆ `ReadExactBytes` + `Encoding.ASCII.GetString` replacements verified at lines 53, 61, 73, 83, 91 |
+| long/ulong bounded seeks | PASS | `checked((long)...)` on all size values at lines 58, 75, 93 |
+| SeekChecked bounds | PASS | Guard at lines 162-167, called at lines 112, 118, 124, 126 |
+| PROP walk break | PASS | Line 128-129: `break` after FS+CHNL found, unchanged logic |
+| Parser reuse rationale | PASS | Separate concerns: probe (read-only metadata) vs stripper (copy/repair). No merge. |
+| Build | PASS | 0 warnings, 0 errors |
+| Runtime acceptance | BLOCKED | No DFF tooling/media in worktree. Owner: P3.4/P4 |
+
+**Commit:** Report only. No source edits.
diff --git a/src/Services/Audio/DsdConvertService.cs b/src/Services/Audio/DsdConvertService.cs
index 5d18f35..c706744 100644
--- a/src/Services/Audio/DsdConvertService.cs
+++ b/src/Services/Audio/DsdConvertService.cs
@@ -1,11 +1,12 @@
 using System.Buffers.Binary;
+using System.Text;
 using Core;
 
 namespace Services.Audio;
 
 using ErrorOr;
 
 public sealed class DsdConvertService(
 	SaraconService saracon,
 	SoxService sox,
 	AudioMetadataService metadata
@@ -42,84 +43,94 @@ public sealed class DsdConvertService(
 			Telemetry.Debug(
 				"DsdConvert.ProbeStart file={File} size={Size}MB",
 				Path.GetFileName(dffFilePath),
 				new FileInfo(dffFilePath).Length / 1_048_576.0
 			);
 
 			ct.ThrowIfCancellationRequested();
 			await using FileStream stream = File.OpenRead(dffFilePath);
 			using BinaryReader reader = new(stream);
 
-			var magic = new string(reader.ReadChars(4));
+			var magic = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
 			if (magic != "FRM8")
 				return Errors.Audio.ProbeFailed(dffFilePath, $"Not a DSDIFF file (magic: {magic})");
 
-			reader.ReadBytes(8);
-			var formType = new string(reader.ReadChars(4));
+			var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+			var formEnd = checked(12 + checked((long)formSize));
+			if (formEnd > stream.Length)
+				throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");
+			var formType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
 			if (formType != "DSD ")
 				return Errors.Audio.ProbeFailed(dffFilePath, $"Unexpected form type: {formType}");
 
 			var sampleRate = 0;
 			var channels = 0;
 
-			while (stream.Position < stream.Length - 12)
+			while (stream.Position < stream.Length)
 			{
-				var chunkId = new string(reader.ReadChars(4));
-				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));
+				if (stream.Length - stream.Position < 12)
+					throw new InvalidDataException("Truncated DSDIFF chunk header");
+
+				var chunkId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+				var chunkSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+				var chunkEnd = checked(stream.Position + checked((long)chunkSize));
 
 				if (chunkId == "PROP")
 				{
-					var propType = new string(reader.ReadChars(4));
+					if (chunkSize < 4)
+						throw new InvalidDataException("PROP chunk is too small");
+
+					var propEnd = chunkEnd;
+					var propType = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
 					if (propType == "SND ")
 					{
-						var propEnd = stream.Position + (long)chunkSize - 4;
-						while (stream.Position < propEnd - 12)
+						while (stream.Position < propEnd)
 						{
-							var subId = new string(reader.ReadChars(4));
-							var subSize = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));
+							if (propEnd - stream.Position < 12)
+								throw new InvalidDataException("Truncated PROP subchunk header");
+
+							var subId = Encoding.ASCII.GetString(ReadExactBytes(reader, 4));
+							var subSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
+							var subEnd = checked(stream.Position + checked((long)subSize));
+							if (subEnd > propEnd)
+								throw new InvalidDataException("PROP subchunk exceeds PROP chunk");
 
 							if (subId == "FS  ")
 							{
-								sampleRate = (int)
-									BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
-								if (subSize > 4)
-									reader.ReadBytes((int)subSize - 4);
+								if (subSize < 4)
+									throw new InvalidDataException("FS subchunk is too small");
+
+								sampleRate = checked((int)BinaryPrimitives.ReadUInt32BigEndian(ReadExactBytes(reader, 4)));
 							}
 							else if (subId == "CHNL")
 							{
-								channels = BinaryPrimitives.ReadUInt16BigEndian(
-									reader.ReadBytes(2)
-								);
-								if (subSize > 2)
-									reader.ReadBytes((int)subSize - 2);
+								if (subSize < 2)
+									throw new InvalidDataException("CHNL subchunk is too small");
+
+								channels = BinaryPrimitives.ReadUInt16BigEndian(ReadExactBytes(reader, 2));
 							}
-							else
+
+							SeekChecked(stream, subEnd);
+							if (subSize % 2 != 0)
 							{
-								reader.ReadBytes((int)subSize);
+								var paddedSubEnd = checked(subEnd + 1);
+								if (paddedSubEnd > propEnd)
+									throw new InvalidDataException("PROP subchunk padding exceeds PROP chunk");
+								SeekChecked(stream, paddedSubEnd);
 							}
-
-							if (subSize % 2 != 0 && stream.Position < stream.Length)
-								reader.ReadByte();
 						}
 					}
-					else
-					{
-						reader.ReadBytes((int)chunkSize - 4);
-					}
-				}
-				else
-				{
-					reader.ReadBytes((int)chunkSize);
 				}
 
-				if (chunkSize % 2 != 0 && stream.Position < stream.Length)
-					reader.ReadByte();
+				SeekChecked(stream, chunkEnd);
+				if (chunkSize % 2 != 0)
+					SeekChecked(stream, checked(chunkEnd + 1));
 
 				if (sampleRate > 0 && channels > 0)
 					break;
 			}
 
 			if (sampleRate == 0 || channels == 0)
 				return Errors.Audio.ProbeFailed(
 					dffFilePath,
 					"Could not parse FS or CHNL chunks from DFF header"
 				);
@@ -133,20 +144,35 @@ public sealed class DsdConvertService(
 
 			return new DsdProbeResult(dffFilePath, "dsd", sampleRate, channels);
 		}
 		catch (Exception ex) when (ex is not OperationCanceledException)
 		{
 			Telemetry.Error("DsdConvert.ProbeFailed file={File}: {Error}", LogPaths.Format(dffFilePath), ex.Message);
 			return Errors.Audio.ProbeFailed(dffFilePath, ex.Message);
 		}
 	}
 
+	private static byte[] ReadExactBytes(BinaryReader reader, int count)
+	{
+		var bytes = reader.ReadBytes(count);
+		if (bytes.Length != count)
+			throw new EndOfStreamException();
+		return bytes;
+	}
+
+	private static void SeekChecked(FileStream stream, long target)
+	{
+		if (target < 0 || target > stream.Length)
+			throw new InvalidDataException("DSDIFF seek exceeds stream bounds");
+		stream.Seek(target, SeekOrigin.Begin);
+	}
+
 	public async Task<ErrorOr<double>> CalculateGainAsync(
 		string dffFilePath,
 		DsdProbeResult probe,
 		DsdConversionSettings settings,
 		CancellationToken ct = default
 	)
 	{
 		Telemetry.Debug(
 			"DsdConvert.GainCalcStart file={File} rate={Rate} bitDepth={BitDepth}",
 			Path.GetFileName(dffFilePath),
