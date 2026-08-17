9b005b2 docs(audio): P0.3 falsified-completion audit ΓÇö T1-T11 re-derivation with source evidence
 .superpowers/sdd/new-mega-plan/task-3-report.md | 755 ++++++++++++++++++++++++
 1 file changed, 755 insertions(+)
diff --git a/.superpowers/sdd/new-mega-plan/task-3-report.md b/.superpowers/sdd/new-mega-plan/task-3-report.md
new file mode 100644
index 0000000..5e5dcb2
--- /dev/null
+++ b/.superpowers/sdd/new-mega-plan/task-3-report.md
@@ -0,0 +1,755 @@
+# P0.3 ΓÇö Falsified-Completion Audit: Evidence Report
+
+**Worktree:** `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`
+**Commit (HEAD):** `29f9411` (P0.2)
+**Executed:** 2026-08-16
+
+---
+
+## Master Table
+
+| Task | Claim | Source Location | Status | Later Task |
+|------|-------|----------------|--------|------------|
+| T1 | Sink at `state/logs` | `Telemetry.cs:28` | CONFIRMED | ΓÇö |
+| T1 | File sub-logger Verbose, not shadowed by root `LevelSwitch` | `Telemetry.cs:52` | CONFIRMED | ΓÇö |
+| T1 | Run one command from `C:\Users\Lance` | runtime | BLOCKED | P4.3 |
+| T1 | Mangled temp-root label in `Saracon.ConvertStart` | `LogPaths.cs:28-30`, `SaraconService.cs:121` | CONFIRMED | P4.3 |
+| T1 | Seq-sink level deferral | `Telemetry.cs:40` | CONFIRMED | P4.3 |
+| T3 | Rejection of `24`/`both` in `sacd-convert` | `SacdConvertCommand.cs:37-44` | CONFIRMED | ΓÇö |
+| T3 | `ForDsdRate` intact | `AudioModels.cs:27-57` | CONFIRMED | ΓÇö |
+| T3 | `dsd-convert` builds | build output | CONFIRMED | ΓÇö |
+| T3 | Media conversion never run | no runtime evidence | STATIC-ONLY | P4.3 |
+| T4 | copy-16 (16-byte header) | `DffMetadataStripper.cs:67-68` | CONFIRMED | ΓÇö |
+| T4 | `ckDataSize` rewrite | `DffMetadataStripper.cs:72-80` | CONFIRMED | ΓÇö |
+| T4 | Read-back verify | `DffMetadataStripper.cs:82-87` | CONFIRMED | ΓÇö |
+| T4 | `finally` cleanup | `DffMetadataStripper.cs:108-125` | CONFIRMED | ΓÇö |
+| T4 | `PROP` descent | `DffMetadataStripper.cs:151-161,186-211` | CONFIRMED | ΓÇö |
+| T4 | `HasId3Chunk` throws uncaught by callers | `DffMetadataStripper.cs:31`, `DsdConvertService.cs:22` | FALSE | P1.7 |
+| T6/T7 | Six `TerminationReason` values | `ProcessRunner.cs:9-17` | CONFIRMED | ΓÇö |
+| T6/T7 | No killed process returns exit 0 | `ProcessRunner.cs:134-138` | CONFIRMED | ΓÇö |
+| T6/T7 | Every abnormal path reaps | `ProcessRunner.cs:134-138` via `KillAndReapAsync` | CONFIRMED | ΓÇö |
+| T6/T7 | `inactivityCts` disposed | `ProcessRunner.cs:80` (`using`) | CONFIRMED | ΓÇö |
+| T6/T7 | Estimator receives probed rate/channels | `SaraconService.cs:288-337` | CONFIRMED | ΓÇö |
+| T6/T7 | Real Saracon conversion never run | no runtime evidence | STATIC-ONLY | P4.3 |
+| T8/T9 | Gain probe uses resolved settings | `DsdConvertService.cs:140-168`, `PipelineOrchestrator.cs:387-398` | CONFIRMED | ΓÇö |
+| T8/T9 | `ProbeSampleRate`/`ProbeBitDepth` gone | grep: 0 matches | CONFIRMED | ΓÇö |
+| T8/T9 | `CheckSpaceForConversion` wired both sites | `PipelineOrchestrator.cs:215,277` | CONFIRMED | ΓÇö |
+| T8/T9 | Space check ordered before `DeletePartialFlacs` in case B | `PipelineOrchestrator.cs:215-226` | CONFIRMED | ΓÇö |
+| T8/T9 | Runtime log equality never observed | no runtime evidence | STATIC-ONLY | P4.3 |
+| T10/T11 | F-9: pre-work verdict recording | `PipelineOrchestrator.cs:247,301` | FALSE | P1.2 |
+| T10/T11 | F-10: Failed sticky, Complete can't clear | `ReprocessGuard.cs:64-66` | FALSE | P1.2 |
+| T10/T11 | F-11: off-by-one (N=3 yields 2 attempts) | `PipelineOrchestrator.cs:166-182` | FALSE | P1.2 |
+| T10/T11 | T11 blessed "Complete can't remove Failed" | `task-11-report.md` (absent) | FALSE | P3.2 |
+| T10/T11 | T11 blessed "different verdict resets count" | `task-11-report.md` (absent) | FALSE | P3.2 |
+
+---
+
+## Subtask 1: T1 ΓÇö Logging Sink + Level Architecture
+
+### Claim 1.1: Sink at `state/logs`
+
+**Source:** `Telemetry.cs:28-36`
+```csharp
+var logDir = Path.Combine(PathResolver.RepoRoot, "state", "logs");
+Directory.CreateDirectory(logDir);
+
+foreach (ServiceName service in Enum.GetValues<ServiceName>())
+    AddServiceLogger(
+        config,
+        service,
+        Path.Combine(logDir, $"{service.ToFileSlug()}.jsonl")
+    );
+```
+
+**Status: CONFIRMED** ΓÇö Per-service JSONL files written to `state/logs/`.
+
+### Claim 1.2: File sub-logger explicitly Verbose, not shadowed by root `LevelSwitch`
+
+**Source:** `Telemetry.cs:14,16-26,51-62`
+```csharp
+// Root LevelSwitch (line 14)
+private static LoggingLevelSwitch LevelSwitch { get; set; } = new();
+
+// Configure (line 16-18) ΓÇö LevelSwitch defaults to Information
+LevelSwitch = new LoggingLevelSwitch(level); // level = Information
+
+// Root config (line 21)
+.MinimumLevel.Verbose()
+
+// Spectre sub-logger (line 24) ΓÇö ControlledBy LevelSwitch
+lc.MinimumLevel.ControlledBy(LevelSwitch)
+
+// File sub-logger (line 52) ΓÇö Verbose, no ControlledBy
+lc.MinimumLevel.Verbose()
+```
+
+Root LevelSwitch controls only the Spectre (console) sink. File sub-logger pipeline starts at Verbose independently. The `restrictedToMinimumLevel: LogEventLevel.Debug` on the File sink (line 62) further restricts what the File sink writes ΓÇö Debug and above only, Verbose messages are filtered at the sink.
+
+**Status: CONFIRMED** ΓÇö File sub-logger is Verbose at pipeline level. Root LevelSwitch does not affect it.
+
+### Claim 1.3: Run one command from `C:\Users\Lance`
+
+**Command:**
+```powershell
+dotnet run --project src\App -- audio sacd-convert --help
+```
+
+**Raw Output:**
+```
+Exit: 2
+```
+
+App requires `.env` at repo root (line 37-44 of `Program.cs`). `.env` absent in worktree.
+
+**Status: BLOCKED** ΓÇö No `.env` file. Cannot observe runtime log output. Owner: environment setup. Signature: `Program.cs:37-44` returns 2 if `.env` missing.
+
+### Claim 1.4: Mangled temp-root label in `Saracon.ConvertStart`
+
+**Source:** `LogPaths.cs:28-30` + `SaraconService.cs:121-128`
+```csharp
+// LogPaths.cs:28-30
+var tempRoot = Normalise(Path.GetTempPath());
+if (IsWithin(path, tempRoot))
+    return FormatRooted(path, tempRoot, "TMP");
+```
+
+```csharp
+// SaraconService.cs:121-128
+Telemetry.Debug(
+    "Saracon.ConvertStart input={Input} outputDir={OutputDir} ...",
+    Path.GetFileName(inputDff),
+    LogPaths.Format(outputDir),  // temp paths render as ┬½TMP┬╗\...
+    ...
+);
+```
+
+Temp-directory paths are rendered as `┬½TMP┬╗\...` in logs. This is by-design path shortening. The "mangled" label is `┬½TMP┬╗` which is intentional ΓÇö but makes it impossible to recover the actual temp path from log output alone. Phase 5 gates read this log, so this either needs fixing or formal accounting.
+
+**Status: CONFIRMED** ΓÇö Path shortening is intentional; recovery of actual path from log is impossible.
+
+### Claim 1.5: Seq-sink level deferral
+
+**Source:** `Telemetry.cs:38-41`
+```csharp
+var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341";
+if (await IsSeqReachableAsync(seqUrl))
+    _ = config.WriteTo.Seq(seqUrl);
+```
+
+Seq sink added directly to root config. No `restrictedToMinimumLevel` or `.MinimumLevel` override. Uses root minimum of `Verbose()` (line 21). All events from Verbose up are sent to Seq if reachable.
+
+**Status: CONFIRMED** ΓÇö Seq sink receives all events at root minimum (Verbose). No explicit level restriction.
+
+---
+
+## Subtask 2: T3 ΓÇö Format Rejection + ForDsdRate + dsd-convert
+
+### Claim 2.1: Rejection of `24`/`both` in `sacd-convert`
+
+**Source:** `SacdConvertCommand.cs:37-44`
+```csharp
+if (settings.Format != AudioOutputFormat.Bit16)
+{
+    await Console.Error.WriteLineAsync(
+        "SACD conversion supports only --format 16.",
+        cancellationToken
+    );
+    return 1;
+}
+```
+
+**Status: CONFIRMED** ΓÇö Only `Bit16` accepted. `Bit24` and `Both` rejected with message.
+
+### Claim 2.2: `ForDsdRate` intact
+
+**Source:** `AudioModels.cs:27-57`
+```csharp
+public static (DsdConversionSettings Primary, DsdConversionSettings? Derived) ForDsdRate(
+    int dsdSampleRate,
+    AudioOutputFormat format,
+    double gain
+) =>
+    dsdSampleRate switch
+    {
+        2822400 => format switch
+        {
+            AudioOutputFormat.Bit16 => (new DsdConversionSettings(44100, 16, gain), null),
+            AudioOutputFormat.Bit24 => (new DsdConversionSettings(88200, 24, gain), null),
+            AudioOutputFormat.Both => (
+                new DsdConversionSettings(88200, 24, gain),
+                new DsdConversionSettings(44100, 16, gain)
+            ),
+            _ => throw new InvalidOperationException($"Unsupported format: {format}"),
+        },
+        5644800 => format switch
+        {
+            AudioOutputFormat.Bit16 => (new DsdConversionSettings(88200, 16, gain), null),
+            AudioOutputFormat.Bit24 => (new DsdConversionSettings(176400, 24, gain), null),
+            AudioOutputFormat.Both => (
+                new DsdConversionSettings(176400, 24, gain),
+                new DsdConversionSettings(88200, 16, gain)
+            ),
+            _ => throw new InvalidOperationException($"Unsupported format: {format}"),
+        },
+        _ => throw new InvalidOperationException(...)
+    };
+```
+
+Full switch handles all three formats for both DSD64 and DSD128. Method is intact.
+
+**Status: CONFIRMED** ΓÇö `ForDsdRate` handles `Bit16`, `Bit24`, and `Both` for both DSD sample rates.
+
+### Claim 2.3: `dsd-convert` builds and runs
+
+**Build Command:**
+```powershell
+dotnet build --no-restore
+```
+
+**Raw Output:**
+```
+Build succeeded.
+    0 Warning(s)
+    0 Error(s)
+```
+
+`DsdConvertCommand.cs` exists and builds. Note: `DsdConvertCommand.cs:17` declares `[Description("Input DSF or DFF file")]` ΓÇö the brief claims DFF-only, but the help text says DSF or DFF. The command only calls `ProbeDsdAsync` which parses DSDIFF headers. DSF files would fail at probe.
+
+**Status: PARTIAL** ΓÇö Builds clean. Help text claims DSF support (`DsdConvertCommand.cs:17`) but code only handles DFF.
+
+### Claim 2.4: Media conversion never run ΓÇö STATIC-ONLY
+
+No runtime SACD conversion observed in this worktree. The `state/audio/` directory is absent (confirmed in P0.2). No conversion logs exist.
+
+**Status: STATIC-ONLY** ΓÇö Source code exists and builds; no runtime conversion observed. ΓåÆ **P4.3** (runtime observation)
+
+---
+
+## Subtask 3: T4 ΓÇö ID3 Stripping + throws enumeration
+
+### Claim 3.1: copy-16 (16-byte header copy)
+
+**Source:** `DffMetadataStripper.cs:67-68`
+```csharp
+var dffHeader = await ReadExactlyAsync(input, DffHeaderSize, ct); // DffHeaderSize = 16
+await output.WriteAsync(dffHeader, ct);
+```
+
+**Status: CONFIRMED** ΓÇö 16-byte DFF header copied verbatim.
+
+### Claim 3.2: `ckDataSize` rewrite
+
+**Source:** `DffMetadataStripper.cs:72-80`
+```csharp
+var outputDataSize = output.Length - HeaderSize; // HeaderSize = 12
+if ((outputDataSize & 1) != 0)
+    throw new InvalidDataException("Filtered DFF length is not even");
+
+output.Position = 4;
+var sizeBytes = new byte[8];
+BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputDataSize));
+await output.WriteAsync(sizeBytes, ct);
+```
+
+**Status: CONFIRMED** ΓÇö `ckDataSize` written at offset 4, even-length validated.
+
+### Claim 3.3: Read-back verify
+
+**Source:** `DffMetadataStripper.cs:82-87`
+```csharp
+output.Position = 4;
+var writtenSize = BinaryPrimitives.ReadUInt64BigEndian(
+    await ReadExactlyAsync(output, 8, ct)
+);
+if (writtenSize != (ulong)outputDataSize)
+    throw new InvalidDataException("Filtered DFF FRM8 size does not match output length");
+```
+
+**Status: CONFIRMED** ΓÇö Read-back verification present.
+
+### Claim 3.4: `finally` cleanup
+
+**Source:** `DffMetadataStripper.cs:108-125`
+```csharp
+finally
+{
+    if (outputCreated && !completed)
+    {
+        try
+        {
+            File.Delete(cleanPath);
+        }
+        catch (Exception cleanupError)
+        {
+            Telemetry.Error(
+                "DffMetadataStripper.CleanupFailed file={File} error={Error}",
+                LogPaths.Format(cleanPath),
+                cleanupError.Message
+            );
+        }
+    }
+}
+```
+
+**Status: CONFIRMED** ΓÇö Partial output deleted on failure.
+
+### Claim 3.5: `PROP` descent
+
+**Source:** `DffMetadataStripper.cs:151-161` (scan) + `DffMetadataStripper.cs:186-211` (copy)
+
+```csharp
+// Scan: PROP recursion (line 151-161)
+if (chunk.Id == PropChunkId)
+{
+    if (chunk.Size < 4)
+        throw new InvalidDataException("PROP chunk is missing property type");
+    input.Position += 4;
+    found |= await ScanChunksAsync(input, chunk.DataEnd, ct);
+}
+
+// Copy: PROP rewrite (line 186-211)
+if (chunk.Id == PropChunkId)
+{
+    ...
+    await WriteChunkHeaderAsync(output, chunk.Id, chunk.Size, ct);
+    input.Position = chunk.DataStart;
+    await CopyBytesAsync(input, output, 4, ct);
+    await CopyChunksAsync(input, output, chunk.DataEnd, ct);
+    var outputSize = output.Position - outputHeaderPosition - HeaderSize;
+    ...
+    output.Position = outputHeaderPosition + 4;
+    BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, checked((ulong)outputSize));
+    await output.WriteAsync(sizeBytes, ct);
+    ...
+}
+```
+
+**Status: CONFIRMED** ΓÇö PROP chunks descended recursively; PROP size rewritten after filtering.
+
+### Claim 3.6: `HasId3Chunk` throws uncaught by callers
+
+**Source:** `DffMetadataStripper.cs:14-33` + `DsdConvertService.cs:16-30`
+
+```csharp
+// HasId3Chunk (line 14-33) ΓÇö throws on failure
+public static bool HasId3Chunk(string dffPath)
+{
+    ...
+    try
+    {
+        using FileStream input = File.OpenRead(dffPath);
+        return ScanAsync(input, CancellationToken.None).GetAwaiter().GetResult();
+    }
+    catch (Exception ex)
+    {
+        Telemetry.Error(...);
+        throw;  // line 31: re-throws to caller
+    }
+}
+
+// PrepareDffAsync (line 16-30) ΓÇö calls HasId3Chunk, no catch
+public async Task<ErrorOr<string>> PrepareDffAsync(...)
+{
+    if (!DffMetadataStripper.HasId3Chunk(dffFilePath))  // line 22: can throw
+        return dffFilePath;
+    ...
+    return await DffMetadataStripper.StripId3TagsAsync(dffFilePath, outputDir, ct);
+}
+```
+
+`HasId3Chunk` throws `InvalidDataException`, `EndOfStreamException`, etc. on corrupt DFF. `PrepareDffAsync` does not catch these. `ConvertDiscAsync` (`PipelineOrchestrator.cs:383-384`) does not catch them. `ProcessIsoAsync` does not catch them. The throw propagates to `RunAsync` which only catches `OperationCanceledException`. **One corrupt DFF aborts the entire batch.**
+
+This is the exact defect P1.7 targets: "Strict input validation plus a rethrowing `HasId3Chunk` with no catching caller means one odd DFF aborts the batch."
+
+**Status: FALSE** ΓÇö T4 claimed the stripper was safe; `HasId3Chunk` throws propagate uncaught. ΓåÆ **P1.7**
+
+### Throws Enumeration (DffMetadataStripper)
+
+| Throw Location | Exception | Caught By | Caller |
+|----------------|-----------|-----------|--------|
+| `ScanAsync:131` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ValidateDffHeader:237` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ValidateDffHeader:239` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ValidateDffHeader:243` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ScanChunksAsync:154` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ScanChunksAsync:164` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ReadChunkAsync:221` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ReadChunkAsync:229` | `InvalidDataException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `ReadExactlyAsync:254` | `EndOfStreamException` | `HasId3Chunk:25` (re-throws) | `PrepareDffAsync` (uncaught) |
+| `CopyBytesAsync:270` | `EndOfStreamException` | `StripId3TagsAsync:99` (returns error) | N/A |
+| `StripId3TagsAsync:74` | `InvalidDataException` | `StripId3TagsAsync:99` (returns error) | N/A |
+| `StripId3TagsAsync:87` | `InvalidDataException` | `StripId3TagsAsync:99` (returns error) | N/A |
+| `CopyChunksAsync:194` | `InvalidDataException` | `StripId3TagsAsync:99` (returns error) | N/A |
+| `CopyChunksAsync:203` | `InvalidDataException` | `StripId3TagsAsync:99` (returns error) | N/A |
+| `CopyChunksAsync:215` | `InvalidDataException` | `StripId3TagsAsync:99` (returns error) | N/A |
+
+**Summary:** `StripId3TagsAsync` catches all its throws. `HasId3Chunk` catches, logs, and re-throws. Callers of `HasId3Chunk` do not catch. **11 uncaught throw paths via `HasId3Chunk`.**
+
+---
+
+## Subtask 4: T6/T7 ΓÇö ProcessRunner Termination
+
+### Claim 4.1: Six `TerminationReason` values
+
+**Source:** `ProcessRunner.cs:9-17`
+```csharp
+public enum TerminationReason
+{
+    Exited,
+    CallerCanceled,
+    Timeout,
+    InactivityTimeout,
+    KilledAfterCompletionMarker,
+    StartFailed,
+}
+```
+
+**Status: CONFIRMED** ΓÇö All six values present.
+
+### Claim 4.2: No killed process returns exit code 0
+
+**Source:** `ProcessRunner.cs:134-138`
+```csharp
+async Task<ProcessResult> stopAndBuildAsync(TerminationReason reason)
+{
+    await KillAndReapAsync(process, stdoutDrainTcs.Task, stderrDrainTcs.Task);
+    return new ProcessResult(stdoutSb.ToString(), stderrSb.ToString(), process.ExitCode, reason);
+}
+```
+
+All abnormal paths (`CallerCanceled`, `Timeout`, `InactivityTimeout`, `KilledAfterCompletionMarker`) call `stopAndBuildAsync` which kills the process then returns the actual `process.ExitCode`. After `Kill(entireProcessTree: true)` + `WaitForExitAsync`, the exit code is the OS kill code (non-zero on Windows). No path launders the exit code to 0.
+
+**Status: CONFIRMED** ΓÇö Exit code preserved from actual process state after kill.
+
+### Claim 4.3: Every abnormal path reaps
+
+**Source:** `ProcessRunner.cs:134-138` + `ProcessRunner.cs:313-322`
+```csharp
+// All abnormal paths call stopAndBuildAsync
+async Task<ProcessResult> stopAndBuildAsync(TerminationReason reason)
+{
+    await KillAndReapAsync(process, stdoutDrainTcs.Task, stderrDrainTcs.Task);
+    ...
+}
+
+// KillAndReapAsync (line 313-322)
+private static async Task KillAndReapAsync(Process process, Task? stdoutDrain, Task? stderrDrain)
+{
+    if (!process.HasExited)
+        process.Kill(entireProcessTree: true);
+    await DrainOutputAsync(process, stdoutDrain, stderrDrain);
+}
+```
+
+Also `OperationCanceledException` handler (line 283-286) calls `KillAndReapAsync`. Generic exception handler (line 288-305) calls `KillAndReapAsync`. `finally` (line 307-310) disposes process.
+
+**Status: CONFIRMED** ΓÇö Every abnormal path kills and reaps the process.
+
+### Claim 4.4: `inactivityCts` disposed
+
+**Source:** `ProcessRunner.cs:80`
+```csharp
+using CancellationTokenSource inactivityCts = new();
+```
+
+Disposed via `using` declaration.
+
+**Status: CONFIRMED**
+
+### Claim 4.5: Estimator receives probed rate/channels
+
+**Source:** `SaraconService.cs:288-337` (EstimateExpectedPcmBytes)
+```csharp
+private static long EstimateExpectedPcmBytes(
+    string dffPath,
+    int dsdSampleRate,  // from probe
+    int sampleRate,
+    int channels,        // from probe
+    int bitDepth
+)
+```
+
+Called at line 211: `var expectedPcmBytes = EstimateExpectedPcmBytes(inputDff, dsdSampleRate, sampleRate, channels, bitDepth);`
+
+The `dsdSampleRate` and `channels` parameters come from the caller (`RunConversionAsync`), which receives them from `ConvertDsdToPcmAsync`/`ConvertDsdToFlacAsync` callers, which pass the probed values.
+
+**Status: CONFIRMED** ΓÇö Estimator receives probed DSD sample rate and channel count.
+
+### Claim 4.6: Real Saracon conversion never run ΓÇö STATIC-ONLY
+
+No runtime Saracon conversion observed. `state/logs/audio.jsonl` absent.
+
+**Status: STATIC-ONLY** ΓÇö Source code exists; no runtime conversion observed. ΓåÆ **P4.3**
+
+---
+
+## Subtask 5: T8/T9 ΓÇö Gain Probe + Space Check
+
+### Claim 5.1: Gain probe uses resolved settings
+
+**Source:** `DsdConvertService.cs:140-168`
+```csharp
+public async Task<ErrorOr<double>> CalculateGainAsync(
+    string dffFilePath,
+    DsdProbeResult probe,        // from DFF header parse
+    DsdConversionSettings settings,  // from ForDsdRate
+    CancellationToken ct = default
+)
+{
+    Telemetry.Debug(
+        "DsdConvert.GainCalcStart file={File} rate={Rate} bitDepth={BitDepth}",
+        Path.GetFileName(dffFilePath),
+        settings.SampleRate,
+        settings.BitDepth
+    );
+    ...
+    ErrorOr<string> convertResult = await saracon.ConvertDsdToPcmAsync(
+        dffFilePath,
+        tempDir,
+        settings.SampleRate,    // from resolved settings
+        settings.BitDepth,     // from resolved settings
+        settings.GainDb,
+        probe.SampleRate,      // from probe
+        probe.Channels,        // from probe
+        ...
+    );
+```
+
+Called from `PipelineOrchestrator.cs:393-398`:
+```csharp
+DsdConversionSettings gainSettings = DsdConversionSettings.ForDsdRate(
+    dsdProbe.Value.SampleRate,
+    format,
+    0.0
+).Primary;
+
+ErrorOr<double> gainResult = await convertService.CalculateGainAsync(
+    preparedDff.Value,
+    dsdProbe.Value,
+    gainSettings,
+    ct
+);
+```
+
+**Status: CONFIRMED** ΓÇö Gain probe receives `DsdConversionSettings` from `ForDsdRate` with probed sample rate and requested format.
+
+### Claim 5.2: `ProbeSampleRate`/`ProbeBitDepth` gone
+
+**Search:**
+```
+grep -r "ProbeSampleRate\|ProbeBitDepth" src/
+```
+**Result:** 0 matches.
+
+**Status: CONFIRMED** ΓÇö No occurrences in source.
+
+### Claim 5.3: `CheckSpaceForConversion` wired at both sites
+
+**Source:** `PipelineOrchestrator.cs:215-224` (case B ΓÇö NeedsPrimaryConversion)
+```csharp
+ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
+    assessment.DffDir,
+    new FileInfo(isoPath).Length
+);
+if (conversionSpaceCheck.IsError)
+{
+    ct.ThrowIfCancellationRequested();
+    await guard.RecordAsync(isoPath, assessment.State);
+    return conversionSpaceCheck.Errors;
+}
+DeletePartialFlacs(assessment.DffDir);  // line 226 ΓÇö after space check
+```
+
+**Source:** `PipelineOrchestrator.cs:277-286` (case A ΓÇö post-extraction)
+```csharp
+ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(
+    channelDir,
+    new FileInfo(isoPath).Length
+);
+if (conversionSpaceCheck.IsError)
+{
+    ct.ThrowIfCancellationRequested();
+    await guard.RecordAsync(isoPath, assessment.State);
+    return conversionSpaceCheck.Errors;
+}
+```
+
+**Status: CONFIRMED** ΓÇö `CheckSpaceForConversion` called in both case A (post-extraction) and case B (pre-conversion).
+
+### Claim 5.4: Space check ordered before `DeletePartialFlacs` in case B
+
+**Source:** `PipelineOrchestrator.cs:215-226`
+```csharp
+ErrorOr<Success> conversionSpaceCheck = diskSpaceChecker.CheckSpaceForConversion(...); // line 215
+if (conversionSpaceCheck.IsError) { ... return; }  // line 219-224
+DeletePartialFlacs(assessment.DffDir);  // line 226
+```
+
+Space check at line 215; delete at line 226. If space check fails, delete never runs.
+
+**Status: CONFIRMED** ΓÇö Space check executes before `DeletePartialFlacs`.
+
+### Claim 5.5: Runtime log equality never observed ΓÇö STATIC-ONLY
+
+T8's acceptance criterion: `GainCalcComplete` and `Saracon.ConvertStart` show the same rate and bit depth **in the log**. No runtime log observed.
+
+**Status: STATIC-ONLY** ΓÇö Source code passes the same `settings` object to both log entries; log equality is inferable from source but not observed. ΓåÆ **P4.3**
+
+---
+
+## Subtask 6: T10/T11 ΓÇö Guard Defects + T11 Blessed Assertions
+
+### Claim 6.1: F-9 ΓÇö Pre-work verdict recording
+
+**Source:** `PipelineOrchestrator.cs:247`
+```csharp
+// After successful conversion (NeedsPrimaryConversion case B):
+ErrorOr<List<string>> convertResult = await convertService.ConvertAndSplitAsync(...);
+if (convertResult.IsError)
+{
+    ...
+    await guard.RecordAsync(isoPath, assessment.State);  // line 242 ΓÇö records on error
+    return convertResult.Errors;
+}
+ct.ThrowIfCancellationRequested();
+await guard.RecordAsync(isoPath, assessment.State);  // line 247 ΓÇö records on SUCCESS
+```
+
+**Source:** `PipelineOrchestrator.cs:301`
+```csharp
+// After successful extraction + conversion (case A):
+foreach (var dffDir in extractResult.Value)
+{
+    ErrorOr<Success> dirResult = await ConvertDiscAsync(dffDir, format, ct);
+    if (dirResult.IsError) { ... return; }
+}
+ct.ThrowIfCancellationRequested();
+await guard.RecordAsync(isoPath, assessment.State);  // line 301 ΓÇö records on SUCCESS
+```
+
+Both success paths (line 247 and line 301) record `assessment.State` ΓÇö the **pre-work** verdict (`NeedsPrimaryConversion` or `NeedsExtraction`) ΓÇö not the **outcome** (`Complete`). This means:
+- Success and failure are indistinguishable to the counter
+- A successful conversion records `NeedsPrimaryConversion`, which is treated as a non-`Complete` outcome
+- The consecutive count accumulates even on success
+
+Compare with the `Complete` path at line 209: `await guard.RecordAsync(isoPath, DiscState.Complete);` ΓÇö this one correctly records the outcome.
+
+**Status: FALSE** ΓÇö Success paths record pre-work verdict, not cycle outcome. ΓåÆ **P1.2** (subtask 3)
+
+### Claim 6.2: F-10 ΓÇö Failed sticky, Complete can't clear
+
+**Source:** `ReprocessGuard.cs:64-66`
+```csharp
+if (Entries.TryGetValue(isoPath, out GuardEntry? existing)
+    && existing.Verdict == DiscState.Failed)
+    return;
+```
+
+When the existing entry has `Verdict == Failed`, `RecordAsync` returns immediately without modifying anything. A subsequent `Complete` verdict cannot clear the `Failed` entry. Recovery requires manual JSON deletion.
+
+**Status: FALSE** ΓÇö `Failed` is permanently sticky. `Complete` cannot clear it. ΓåÆ **P1.2** (subtask 6)
+
+### Claim 6.3: F-11 ΓÇö Off-by-one (N=3 yields 2 attempts)
+
+**Source:** `PipelineOrchestrator.cs:166-182`
+```csharp
+if (existing is { Verdict: var v, ConsecutiveCount: var c }
+    && c + 1 >= ReprocessGuard.MaxConsecutiveCount  // c+1 >= 3
+    && v != DiscState.Complete)
+{
+    ct.ThrowIfCancellationRequested();
+    await guard.RecordAsync(isoPath, v);
+    ...
+    return Error.Failure(
+        "Audio.GuardBlocked",
+        $"{discName} reached {c + 1}x {v} ΓÇö transitioning Failed, no process started"
+    );
+}
+```
+
+With `MaxConsecutiveCount = 3` (line 8):
+- Run 1: no entry. Check passes. Process runs. `RecordAsync` creates entry with count=1.
+- Run 2: entry count=1. `c+1=2 < 3`. Check passes. Process runs. `RecordAsync` increments to count=2.
+- Run 3: entry count=2. `c+1=3 >= 3`. **Check blocks.** Process does NOT start.
+
+Only 2 attempts execute. The 3rd is blocked. The check `c + 1 >= N` fires before the Nth attempt, reducing the actual attempt count to N-1.
+
+**Source:** `ReprocessGuard.cs:72-75`
+```csharp
+var count = existing?.Verdict == verdict ? existing.ConsecutiveCount + 1 : 1;
+Entries[isoPath] = count >= MaxConsecutiveCount
+    ? new GuardEntry(DiscState.Failed, count, DateTimeOffset.UtcNow)
+    : new GuardEntry(verdict, count, DateTimeOffset.UtcNow);
+```
+
+If the transition check in `ProcessIsoAsync` is removed (as P1.2 subtask 5 requires), the `RecordAsync` logic itself would transition to `Failed` when count reaches `MaxConsecutiveCount`. But with the pre-check in `ProcessIsoAsync`, the Nth attempt never runs, so `RecordAsync` never receives the Nth verdict.
+
+**Status: FALSE** ΓÇö N=3 yields only 2 actual attempts. ΓåÆ **P1.2** (subtask 5)
+
+### Claim 6.4: T11 blessed "Complete can't remove Failed"
+
+**Source:** `task-11-report.md` ΓÇö **ABSENT from worktree**
+
+The mega-plan ┬º0.2 states: "Two of its passing guard cases were: *'Complete can't remove Failed (sticky)'* and *'different verdict resets count'*."
+
+Per ┬º0.2, the T11 harness encoded F-10 (sticky Failed) as expected behavior and passed. The report recorded 74 passing cases. The T11 driver was deleted after passing (`Artifacts deleted: T11Driver/`).
+
+The assertion "Complete can't remove Failed" directly contradicts P1.2 subtask 6 which requires: "Make `Failed` clearable by a genuine `Complete` outcome."
+
+**Status: FALSE** ΓÇö T11 blessed the defective sticky-Failed behavior. Report absent; evidence from mega-plan ┬º0.2. ΓåÆ **P3.2** (decontamination)
+
+### Claim 6.5: T11 blessed "different verdict resets count"
+
+**Source:** `task-11-report.md` ΓÇö **ABSENT from worktree**
+
+Per ┬º0.2, the T11 harness asserted "different verdict resets count" as correct. This corresponds to `ReprocessGuard.cs:72`:
+```csharp
+var count = existing?.Verdict == verdict ? existing.ConsecutiveCount + 1 : 1;
+```
+
+When the verdict changes, count resets to 1. This enables oscillation escape: alternating `NeedsPrimaryConversion` / `NeedsExtraction` verdicts never accumulate to N=3, so the disc is never blocked. P1.2 subtask 4 requires: "Count consecutive non-`Complete` outcomes regardless of verdict, so oscillation terminates."
+
+**Status: FALSE** ΓÇö T11 blessed the oscillation-escape behavior. Report absent; evidence from mega-plan ┬º0.2. ΓåÆ **P3.2** (decontamination)
+
+---
+
+## 44-Artifact Set Gap
+
+The brief references 44 SDD artifacts (briefs, reports, review packages). Only three exist in the worktree:
+
+| Artifact | Present | Path |
+|----------|---------|------|
+| `task-1-report.md` | Γ£ô | `.superpowers/sdd/new-mega-plan/task-1-report.md` |
+| `task-2-report.md` | Γ£ô | `.superpowers/sdd/new-mega-plan/task-2-report.md` |
+| `task-10.1-report.md` | Γ£ô | `.superpowers/sdd/new-mega-plan/task-10.1-report.md` |
+
+All other referenced task reports (`task-3-report.md` through `task-11-report.md`, review packages) are **absent**. The P0.2 report (┬º0.2) documented this gap. T11 report existence is asserted only by mega-plan ┬º0.2.
+
+---
+
+## Subtask Status Summary
+
+| Subtask | Status | Evidence |
+|---------|--------|----------|
+| 1. T1 logging | **PARTIAL** | Source claims confirmed; runtime observation blocked by missing `.env` |
+| 2. T3 format/dsd-convert | **PASS** | Source claims confirmed; `dsd-convert` help text inexact (claims DSF) |
+| 3. T4 stripper/throws | **FAIL** | `HasId3Chunk` throws uncaught by `PrepareDffAsync` ΓÇö 11 uncaught paths |
+| 4. T6/T7 process runner | **PASS** | All source claims confirmed |
+| 5. T8/T9 gain/space | **PASS** | All source claims confirmed |
+| 6. T10/T11 guard | **FAIL** | F-9, F-10, F-11 confirmed as defects; T11 blessed both as correct |
+
+---
+
+## Concerns
+
+1. **T11 report absent:** Cannot quote the two blessed assertions verbatim. Evidence sourced from mega-plan ┬º0.2 only. P3.2 must reconstruct from plan text.
+
+2. **T4 `HasId3Chunk` throws (11 paths):** `PrepareDffAsync` at `DsdConvertService.cs:22` calls `HasId3Chunk` which throws on corrupt DFF. No catch. Propagates through `ConvertDiscAsync` ΓåÆ `ProcessIsoAsync` ΓåÆ `RunAsync`. One corrupt DFF aborts the entire batch. P1.7 is the remediation.
+
+3. **Pre-work verdict recording (F-9):** Lines 247 and 301 record `assessment.State` on success. This makes the guard counter accumulate even on successful conversion, leading to eventual `Failed` after 3 successes. P1.2 subtask 3 is the remediation.
+
+4. **Static-only claims (4 items):** T3 media conversion, T6/T7 Saracon conversion, T8/T9 log equality, and T1 runtime command all require P4.3 runtime observation. Source code is correct; runtime behavior unverified.
+
+5. **`dsd-convert` help text inaccuracy:** `DsdConvertCommand.cs:17` says "Input DSF or DFF file" but code only handles DFF via `ProbeDsdAsync` which parses DSDIFF headers. DSF files would fail at probe. P2.2 should correct this.
+
+6. **No `.env` in worktree:** App exits with code 2 (`Program.cs:37-44`). Blocks all runtime verification.
