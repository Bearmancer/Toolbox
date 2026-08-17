# Review package: 51b7723..7100782

## Commits
7100782 P1.7: Stripper exception containment & input-size repair

## Files changed
 src/Core/Errors.cs                        |   3 +
 src/Services/Audio/DffMetadataStripper.cs |  88 +++++++++++++------
 src/Services/Audio/DsdConvertService.cs   |   5 +-
 task-12-report.md                         | 137 ++++++++++++++++++++++++++++++
 4 files changed, 205 insertions(+), 28 deletions(-)

## Diff
diff --git a/src/Core/Errors.cs b/src/Core/Errors.cs
index 7b21866..5d1c7f8 100644
--- a/src/Core/Errors.cs
+++ b/src/Core/Errors.cs
@@ -157,12 +157,15 @@ public static class Errors
 			);
 
 		public static Error ProcessFailed(string binary, string reason) =>
 			Error.Failure("Audio.ProcessFailed", $"{binary} process failed: {reason}");
 
 		public static Error PathTooLong(string path, int length) =>
 			Error.Failure(
 				"Audio.PathTooLong",
 				$"Output path exceeds Windows MAX_PATH ({length} chars): {path}"
 			);
+
+		public static Error StripFailed(string file, string reason) =>
+			Error.Failure("Audio.StripFailed", $"DFF metadata strip failed for {file}: {reason}");
 	}
 }
diff --git a/src/Services/Audio/DffMetadataStripper.cs b/src/Services/Audio/DffMetadataStripper.cs
index 4207f30..36c13ed 100644
--- a/src/Services/Audio/DffMetadataStripper.cs
+++ b/src/Services/Audio/DffMetadataStripper.cs
@@ -4,77 +4,85 @@ using Core;
 
 namespace Services.Audio;
 
 using ErrorOr;
 
 public static class DffMetadataStripper
 {
 	private const int HeaderSize = 12, DffHeaderSize = 16;
 	private const string FormId = "FRM8", FormType = "DSD ", Id3ChunkId = "ID3 ", PropChunkId = "PROP";
 
-	public static bool HasId3Chunk(string dffPath)
+	public static ErrorOr<bool> HasId3Chunk(string dffPath)
 	{
 		if (!File.Exists(dffPath))
 			return false;
 
 		try
 		{
 			using FileStream input = File.OpenRead(dffPath);
 			return ScanAsync(input, CancellationToken.None).GetAwaiter().GetResult();
 		}
+		catch (OperationCanceledException)
+		{
+			throw;
+		}
 		catch (Exception ex)
 		{
 			Telemetry.Error(
 				"DffMetadataStripper.ScanFailed file={File} error={Error}",
 				LogPaths.Format(dffPath),
 				ex.Message
 			);
-			throw;
+			return Errors.Audio.StripFailed(dffPath, ex.Message);
 		}
 	}
 
 	public static async Task<ErrorOr<string>> StripId3TagsAsync(
 		string dffPath,
 		string outputDir,
 		CancellationToken ct = default
 	)
 	{
 		var cleanPath = Path.Combine(
 			outputDir,
 			Path.GetFileNameWithoutExtension(dffPath) + "_clean.dff"
 		);
 		var outputCreated = false;
 		var completed = false;
 
 		try
 		{
 			await using FileStream input = File.OpenRead(dffPath);
-			var hasId3 = await ScanAsync(input, ct);
-			if (!hasId3)
+			ErrorOr<bool> scanResult = await ScanAsync(input, ct);
+			if (scanResult.IsError)
+				return scanResult.Errors;
+			if (!scanResult.Value)
 				return dffPath;
 
 			Directory.CreateDirectory(outputDir);
 			await using FileStream output = new(
 				cleanPath,
 				FileMode.Create,
 				FileAccess.ReadWrite,
 				FileShare.None,
 				81920,
 				FileOptions.Asynchronous | FileOptions.SequentialScan
 			);
 			outputCreated = true;
 
 			input.Position = 0;
 			var dffHeader = await ReadExactlyAsync(input, DffHeaderSize, ct);
 			await output.WriteAsync(dffHeader, ct);
 			input.Position = DffHeaderSize;
-			await CopyChunksAsync(input, output, input.Length, ct);
+			ErrorOr<Success> copyResult = await CopyChunksAsync(input, output, input.Length, ct);
+			if (copyResult.IsError)
+				return copyResult.Errors;
 
 			var outputDataSize = output.Length - HeaderSize;
 			if ((outputDataSize & 1) != 0)
 				throw new InvalidDataException("Filtered DFF length is not even");
 
 			output.Position = 4;
 			var sizeBytes = new byte[8];
 			BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputDataSize));
 			await output.WriteAsync(sizeBytes, ct);
 			await output.FlushAsync(ct);
@@ -118,136 +126,162 @@ public static class DffMetadataStripper
 					Telemetry.Error(
 						"DffMetadataStripper.CleanupFailed file={File} error={Error}",
 						LogPaths.Format(cleanPath),
 						cleanupError.Message
 					);
 				}
 			}
 		}
 	}
 
-	private static async Task<bool> ScanAsync(Stream input, CancellationToken ct)
+	private static async Task<ErrorOr<bool>> ScanAsync(Stream input, CancellationToken ct)
 	{
 		if (input.Length < DffHeaderSize)
-			throw new InvalidDataException("File too small to be valid DSDIFF");
+			return Errors.Audio.StripFailed("input", "File too small to be valid DSDIFF");
 
 		var header = await ReadExactlyAsync(input, DffHeaderSize, ct);
-		ValidateDffHeader(header, input.Length);
+		ErrorOr<Success> headerResult = ValidateDffHeader(header, input.Length);
+		if (headerResult.IsError)
+			return headerResult.Errors;
 		return await ScanChunksAsync(input, input.Length, ct);
 	}
 
-	private static async Task<bool> ScanChunksAsync(Stream input, long end, CancellationToken ct)
+	private static async Task<ErrorOr<bool>> ScanChunksAsync(Stream input, long end, CancellationToken ct)
 	{
 		var found = false;
 		while (input.Position < end)
 		{
-			Chunk chunk = await ReadChunkAsync(input, end, ct);
+			ErrorOr<Chunk> chunkResult = await ReadChunkAsync(input, end, ct);
+			if (chunkResult.IsError)
+				return chunkResult.Errors;
+			Chunk chunk = chunkResult.Value;
+
 			if (chunk.Id == Id3ChunkId)
 			{
 				found = true;
 				input.Position = chunk.End;
 				continue;
 			}
 
 			if (chunk.Id == PropChunkId)
 			{
 				if (chunk.Size < 4)
-					throw new InvalidDataException("PROP chunk is missing property type");
+					return Errors.Audio.StripFailed("input", "PROP chunk is missing property type");
 
 				input.Position += 4;
-				found |= await ScanChunksAsync(input, chunk.DataEnd, ct);
+				ErrorOr<bool> innerResult = await ScanChunksAsync(input, chunk.DataEnd, ct);
+				if (innerResult.IsError)
+					return innerResult.Errors;
+				found |= innerResult.Value;
 			}
 
 			input.Position = chunk.End;
 		}
 
 		if (input.Position != end)
-			throw new InvalidDataException("DSDIFF chunk walk did not end on a chunk boundary");
+			return Errors.Audio.StripFailed("input", "DSDIFF chunk walk did not end on a chunk boundary");
 
 		return found;
 	}
 
-	private static async Task CopyChunksAsync(Stream input, Stream output, long end, CancellationToken ct)
+	private static async Task<ErrorOr<Success>> CopyChunksAsync(Stream input, Stream output, long end, CancellationToken ct)
 	{
 		while (input.Position < end)
 		{
 			var headerPosition = input.Position;
-			Chunk chunk = await ReadChunkAsync(input, end, ct);
+			ErrorOr<Chunk> chunkResult = await ReadChunkAsync(input, end, ct);
+			if (chunkResult.IsError)
+				return chunkResult.Errors;
+			Chunk chunk = chunkResult.Value;
+
 			if (chunk.Id == Id3ChunkId)
 			{
 				Telemetry.Debug(
 					"DffMetadataStripper.Id3ChunkSkipped offset={Offset} sizeBytes={SizeBytes}",
 					headerPosition,
 					chunk.Size
 				);
 				input.Position = chunk.End;
 				continue;
 			}
 
 			if (chunk.Id != PropChunkId)
 			{
 				input.Position = headerPosition;
 				await CopyBytesAsync(input, output, chunk.End - headerPosition, ct);
 				continue;
 			}
 
 			if (chunk.Size < 4)
-				throw new InvalidDataException("PROP chunk is missing property type");
+				return Errors.Audio.StripFailed("input", "PROP chunk is missing property type");
 
 			var outputHeaderPosition = output.Position;
 			await WriteChunkHeaderAsync(output, chunk.Id, chunk.Size, ct);
 			input.Position = chunk.DataStart;
 			await CopyBytesAsync(input, output, 4, ct);
-			await CopyChunksAsync(input, output, chunk.DataEnd, ct);
+			ErrorOr<Success> innerResult = await CopyChunksAsync(input, output, chunk.DataEnd, ct);
+			if (innerResult.IsError)
+				return innerResult.Errors;
 			var outputSize = output.Position - outputHeaderPosition - HeaderSize;
 			if ((outputSize & 1) != 0)
-				throw new InvalidDataException("Filtered PROP chunk length is not even");
+				return Errors.Audio.StripFailed("input", "Filtered PROP chunk length is not even");
 
 			var endPosition = output.Position;
 			output.Position = outputHeaderPosition + 4;
 			var sizeBytes = new byte[8];
 			BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputSize));
 			await output.WriteAsync(sizeBytes, ct);
 			output.Position = endPosition;
 			input.Position = chunk.End;
 		}
 
 		if (input.Position != end)
-			throw new InvalidDataException("DSDIFF chunk copy did not end on a chunk boundary");
+			return Errors.Audio.StripFailed("input", "DSDIFF chunk copy did not end on a chunk boundary");
+
+		return Result.Success;
 	}
 
-	private static async Task<Chunk> ReadChunkAsync(Stream input, long end, CancellationToken ct)
+	private static async Task<ErrorOr<Chunk>> ReadChunkAsync(Stream input, long end, CancellationToken ct)
 	{
 		if (end - input.Position < HeaderSize)
-			throw new InvalidDataException("DSDIFF chunk header is truncated");
+			return Errors.Audio.StripFailed("input", "DSDIFF chunk header is truncated");
 
 		var header = await ReadExactlyAsync(input, HeaderSize, ct);
 		var id = Encoding.ASCII.GetString(header, 0, 4);
 		var size = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
 		var dataEnd = checked(input.Position + (long)size);
 		var endPosition = checked(dataEnd + (long)(size & 1));
 		if (endPosition > end)
-			throw new InvalidDataException($"DSDIFF chunk {id} exceeds its parent boundary");
+			return Errors.Audio.StripFailed("input", $"DSDIFF chunk {id} exceeds its parent boundary");
 
 		return new Chunk(id, size, input.Position, dataEnd, endPosition);
 	}
 
-	private static void ValidateDffHeader(byte[] header, long length)
+	private static ErrorOr<Success> ValidateDffHeader(byte[] header, long length)
 	{
 		if (Encoding.ASCII.GetString(header, 0, 4) != FormId)
-			throw new InvalidDataException("DSDIFF file does not start with FRM8");
+			return Errors.Audio.StripFailed("input", "DSDIFF file does not start with FRM8");
 		if (Encoding.ASCII.GetString(header, 12, 4) != FormType)
-			throw new InvalidDataException("DSDIFF FRM8 form type is not DSD");
+			return Errors.Audio.StripFailed("input", "DSDIFF FRM8 form type is not DSD");
 
 		var declaredSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
-		if (declaredSize != (ulong)(length - HeaderSize))
-			throw new InvalidDataException("DSDIFF FRM8 size does not match source length");
+		var actualSize = (ulong)(length - HeaderSize);
+		if (declaredSize != actualSize)
+		{
+			Telemetry.Warn(
+				"DffMetadataStripper.InputSizeMismatch declared={Declared} actual={Actual} ΓÇö will scan physical chunk bounds",
+				declaredSize,
+				actualSize
+			);
+		}
+
+		return Result.Success;
 	}
 
 	private static async Task<byte[]> ReadExactlyAsync(Stream input, int count, CancellationToken ct)
 	{
 		var buffer = new byte[count];
 		var offset = 0;
 		while (offset < count)
 		{
 			var read = await input.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
 			if (read == 0)
diff --git a/src/Services/Audio/DsdConvertService.cs b/src/Services/Audio/DsdConvertService.cs
index 39d4a27..5d18f35 100644
--- a/src/Services/Audio/DsdConvertService.cs
+++ b/src/Services/Audio/DsdConvertService.cs
@@ -12,21 +12,24 @@ public sealed class DsdConvertService(
 )
 {
 	private const double TargetHeadroomDb = -0.5;
 
 	public async Task<ErrorOr<string>> PrepareDffAsync(
 		string dffFilePath,
 		string outputDir,
 		CancellationToken ct = default
 	)
 	{
-		if (!DffMetadataStripper.HasId3Chunk(dffFilePath))
+		ErrorOr<bool> hasId3Result = DffMetadataStripper.HasId3Chunk(dffFilePath);
+		if (hasId3Result.IsError)
+			return hasId3Result.Errors;
+		if (!hasId3Result.Value)
 			return dffFilePath;
 
 		Telemetry.Warn(
 			"Saracon.Id3Detected input={Input} ΓÇö stripping before conversion",
 			Path.GetFileName(dffFilePath)
 		);
 		return await DffMetadataStripper.StripId3TagsAsync(dffFilePath, outputDir, ct);
 	}
 
 	public async Task<ErrorOr<DsdProbeResult>> ProbeDsdAsync(
diff --git a/task-12-report.md b/task-12-report.md
new file mode 100644
index 0000000..8d77583
--- /dev/null
+++ b/task-12-report.md
@@ -0,0 +1,137 @@
+# Task 12 ΓÇö P1.7 Stripper Exception Containment & Input-Size Repair
+
+**Branch:** sacd-completion-v2 | **HEAD:** 51b7723 ΓåÆ implementation commit  
+**Date:** 2026-08-16
+
+## Summary
+
+Replaced throwing `DffMetadataStripper` internals with `ErrorOr<T>` returns. Corrupt/odd DFF stripper failure now becomes per-disc `ErrorOr` result; batch continues via P1.1 boundary. Input `ckDataSize` mismatch warns and allows scan/copy to proceed (repair path). Output validation remains hard failure. `OperationCanceledException` propagation preserved. Partial output cleanup via `finally` preserved.
+
+## Files Changed
+
+| File | Lines | Change |
+|------|-------|--------|
+| `src/Services/Audio/DffMetadataStripper.cs` | 88 | `HasId3Chunk` ΓåÆ `ErrorOr<bool>`, `ScanAsync`/`ScanChunksAsync`/`CopyChunksAsync`/`ReadChunkAsync` ΓåÆ `ErrorOr<T>`, `ValidateDffHeader` warns on size mismatch instead of throw |
+| `src/Services/Audio/DsdConvertService.cs` | 5 | `PrepareDffAsync` handles `ErrorOr<bool>` from `HasId3Chunk` |
+| `src/Core/Errors.cs` | 3 | Added `Errors.Audio.StripFailed(file, reason)` factory |
+
+## Subtask Results
+
+### 1. HasId3Chunk ΓåÆ ErrorOr\<bool\>
+
+**Diff:** `DffMetadataStripper.cs:14-33`
+
+- `HasId3Chunk(string)` now returns `ErrorOr<bool>` instead of `bool`
+- Catches all exceptions except `OperationCanceledException` (re-thrown)
+- Returns `Errors.Audio.StripFailed(dffPath, ex.Message)` on failure
+- Non-existent file returns `false` (no error) ΓÇö unchanged
+
+```
+Before: public static bool HasId3Chunk(string dffPath)
+After:  public static ErrorOr<bool> HasId3Chunk(string dffPath)
+```
+
+**Synthetic test:** PASS ΓÇö valid DFFΓåÆfalse, ID3 DFFΓåÆtrue, tinyΓåÆerror, no-FRM8ΓåÆerror, missingΓåÆfalse  
+**Output evidence:** Test driver 13/13 pass
+
+### 2. ScanAsync/ScanChunksAsync/ReadChunkAsync ΓåÆ ErrorOr
+
+**Diff:** `DffMetadataStripper.cs:128-232`
+
+All internal scanning methods return `ErrorOr<T>` instead of throwing `InvalidDataException`:
+
+| Method | Before | After |
+|--------|--------|-------|
+| `ScanAsync` | `Task<bool>`, throws | `Task<ErrorOr<bool>>`, returns error |
+| `ScanChunksAsync` | `Task<bool>`, throws | `Task<ErrorOr<bool>>`, returns error |
+| `ReadChunkAsync` | `Task<Chunk>`, throws | `Task<ErrorOr<Chunk>>`, returns error |
+| `CopyChunksAsync` | `Task`, throws | `Task<ErrorOr<Success>>`, returns error |
+
+Error messages preserved: "File too small to be valid DSDIFF", "DSDIFF chunk header is truncated", etc.
+
+**Synthetic test:** PASS ΓÇö tiny file, no-FRM8 both return errors  
+**Build:** `dotnet build Toolbox.slnx --no-restore --no-incremental` ΓåÆ 0 errors, 0 warnings
+
+### 3. Input ckDataSize Mismatch ΓåÆ Warn + Repair
+
+**Diff:** `DffMetadataStripper.cs:234-250`
+
+`ValidateDffHeader` changed from `void` (throwing) to `ErrorOr<Success>`:
+
+- FRM8 magic mismatch ΓåÆ error (hard failure, unchanged)
+- DSD form type mismatch ΓåÆ error (hard failure, unchanged)
+- **ckDataSize mismatch ΓåÆ `Telemetry.Warn` + continue** (was: throw)
+
+Warning format: `DffMetadataStripper.InputSizeMismatch declared={Declared} actual={Actual} ΓÇö will scan physical chunk bounds`
+
+The scanner then uses physical chunk walks (which use their own boundary checks) to read/copy data. Output rewrite can repair the header.
+
+**Synthetic test:** PASS ΓÇö size-mismatch DFF scans and strips without error  
+**Output evidence:** `StripId3TagsAsync: size mismatch ΓåÆ no error (warn + repair)`
+
+### 4. Output Validation Remains Hard Failure
+
+**Diff:** `DffMetadataStripper.cs:69-88` (inside `StripId3TagsAsync`)
+
+Output validation unchanged ΓÇö exceptions caught by outer `catch (Exception ex) when (ex is not OperationCanceledException)`:
+
+- Even-length check: `throw new InvalidDataException("Filtered DFF length is not even")`
+- FRM8 size round-trip: `throw new InvalidDataException("Filtered DFF FRM8 size does not match output length")`
+- PROP even-length: `Errors.Audio.StripFailed` (now via ErrorOr return from CopyChunksAsync)
+
+These remain hard failures that produce `ErrorOr<string>` error, cleaning up partial output via `finally`.
+
+**Synthetic test:** N/A ΓÇö synthetic DFFs are structurally valid; output validation tested with real 3.3GB/Disc3 (P3.4/P4 harness, BLOCKED)
+
+### 5. Cleanup & Cancellation Preservation
+
+**Diff:** `DffMetadataStripper.cs:99-125`
+
+`finally` block unchanged ΓÇö deletes `cleanPath` when `outputCreated && !completed`. This covers:
+
+- New `ErrorOr` failure paths from `ScanAsync` (pre-strip failure ΓåÆ no output created ΓåÆ no cleanup needed)
+- New `ErrorOr` failure paths from `CopyChunksAsync` (mid-strip failure ΓåÆ output created ΓåÆ finally deletes)
+- `OperationCanceledException` propagation ΓåÆ `completed` stays false ΓåÆ finally deletes partial output
+
+**Synthetic test:** PASS ΓÇö `StripId3TagsAsync: partial output cleaned up on failure`  
+**Synthetic test:** PASS ΓÇö `StripId3TagsAsync: cancelled ΓåÆ OperationCanceledException propagated`
+
+## Build Verification
+
+```
+dotnet build Toolbox.slnx --no-restore --no-incremental
+  Core -> artifacts\bin\Core\debug\Core.dll
+  Audio -> artifacts\bin\Audio\debug\Audio.dll
+  LastFm -> artifacts\bin\LastFm\debug\LastFm.dll
+  Azure -> artifacts\bin\Azure\debug\Azure.dll
+  Google -> artifacts\bin\Google\debug\Google.dll
+  CLI -> artifacts\bin\CLI\debug\CLI.dll
+  App -> artifacts\bin\App\debug\App.dll
+Build succeeded. 0 Warning(s) 0 Error(s)
+```
+
+## Synthetic Test Summary
+
+| # | Test | Result |
+|---|------|--------|
+| 1 | HasId3Chunk: valid DFF without ID3 ΓåÆ false | PASS |
+| 2 | HasId3Chunk: valid DFF with ID3 ΓåÆ true | PASS |
+| 3 | HasId3Chunk: too-small file ΓåÆ error | PASS |
+| 4 | HasId3Chunk: no FRM8 ΓåÆ error | PASS |
+| 5 | HasId3Chunk: missing file ΓåÆ false | PASS |
+| 6 | HasId3Chunk: size mismatch ΓåÆ no error (warn + continue) | PASS |
+| 7 | StripId3TagsAsync: too-small ΓåÆ error (not throw) | PASS |
+| 8 | StripId3TagsAsync: no-FRM8 ΓåÆ error (not throw) | PASS |
+| 9 | StripId3TagsAsync: no ID3 ΓåÆ original path | PASS |
+| 10 | StripId3TagsAsync: ID3 present ΓåÆ clean path | PASS |
+| 11 | StripId3TagsAsync: cancelled ΓåÆ OperationCanceledException | PASS |
+| 12 | StripId3TagsAsync: size mismatch ΓåÆ no error (warn + repair) | PASS |
+| 13 | StripId3TagsAsync: partial output cleaned up on failure | PASS |
+
+**13/13 PASS**
+
+## Concerns
+
+1. **Real 3.3GB/Disc3 runtime:** BLOCKED ΓÇö owner P3.4/P4 harness. Synthetic DFFs cover API contract and error containment, but physical large-file behavior, output validation on real data, and chunk boundary edge cases need P3.4 validation.
+2. **PROP chunk internal padding:** Synthetic PROP body assumes CHNL(2 bytes) needs no padding. Real DFFs with odd-length PROP sub-chunks may exercise additional paths. P3.4 covers this.
+3. **Input size repair:** Warn + continue means the scanner trusts physical chunk walks over declared size. If physical chunks are also corrupt (truncated data), the scanner returns an error via `ReadChunkAsync` boundary check. This is correct behavior ΓÇö declared size mismatch is warning, corrupt chunks are errors.
