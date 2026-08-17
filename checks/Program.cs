using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Core;
using Serilog.Events;
using Services.Audio;

if (args.Length > 0 && args[0] == "--stub")
	return await RunStubAsync(args);

await Telemetry.Configure(LogEventLevel.Fatal);

string tempRoot = Path.Combine(Path.GetTempPath(), $"p31-harness-{DateTime.UtcNow.Ticks}");
List<(string Name, bool Pass, string? Error)> results = [];
List<string> blocked = [];

try
{
	Directory.CreateDirectory(tempRoot);
	string normalizedTempRoot = Path.GetFullPath(tempRoot);
	string systemTemp = Path.GetFullPath(Path.GetTempPath());
	string systemTempWithSep = (systemTemp.EndsWith(Path.DirectorySeparatorChar) || systemTemp.EndsWith(Path.AltDirectorySeparatorChar))
		? systemTemp
		: systemTemp + Path.DirectorySeparatorChar;
	bool isUnderTemp = string.Equals(normalizedTempRoot, systemTemp, StringComparison.OrdinalIgnoreCase)
		|| normalizedTempRoot.StartsWith(systemTempWithSep, StringComparison.OrdinalIgnoreCase);
	if (!isUnderTemp)
	{
		Console.WriteLine($"  FAIL: TempRootUnderSystemTemp — tempRoot={normalizedTempRoot} systemTemp={systemTemp}");
		throw new InvalidOperationException($"Temp root {normalizedTempRoot} is not under system temp {systemTemp}");
	}
	Console.WriteLine("  PASS: TempRootUnderSystemTemp");
	results.Add(("TempRootUnderSystemTemp", true, null));

	await ChildStubExitZeroAsync();
	await ChildStubExitNonzeroAsync();
	await ChildStubOutputVolumeAsync();
	await ChildStubDelayAsync();
	await ChildStubIgnoreTerminationAsync();
	await CompleteClearsFailedAsync();
	await DifferingNonCompleteIncrementsAsync();
	await ProcessRunnerStartFailedAsync();
	await ReflectionAccessAsync();

	await GetFlacsByTrackNumberEmptyDirAsync();
	await GetFlacsByTrackNumberNumberedFlacsAsync();
	await FindDffDirInnerExistsAsync();
	await FindDffDirFallbackToDffParentAsync();
	await InspectorNoCueNoDffNeedsExtractionAsync();
	await InspectorNoCueInvalidDffNeedsExtractionAsync();
	await InspectorNoCueValidDffInvalidArtifactsAsync();
	await OrchestratorGuardSkipBlockedAsync();

	await P34StripFourTopLevelId3Async();
	await P34OddPadPreservedAsync();
	await P34NestedPropId3RemovedAsync();
	await P34TruncatedErrorNoOutputAsync();
	await P34ZeroSizePropErrorAsync();
	await P34ShortFormSizeWarnsAsync();
	await P34RealDisc3StreamedAsync();

	await P35Exit0WithStdoutAsync();
	await P35Exit3WithStderrAsync();
	await P35CallerCancellationAsync();
	await P35TimeoutAsync();
	await P35CompletionMarkerHangAsync();
	await P35HighVolumeStdoutDrainAsync();
}
finally
{
	if (Directory.Exists(tempRoot))
		Directory.Delete(tempRoot, true);
}

if (args.Contains("--force-fail"))
{
	results.Add(("ForcedFailure", false, "Forced failure mode active"));
	Console.WriteLine("  FAIL: ForcedFailure — forced failure mode active");
}

Console.WriteLine();
int passed = results.Count(r => r.Pass);
int failed = results.Count(r => !r.Pass);
Console.WriteLine($"RESULTS: {passed} passed, {failed} failed, {blocked.Count} blocked, {results.Count + blocked.Count} total");
foreach (var (name, pass, error) in results)
	Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}: {name}{(error is not null ? $" — {error}" : "")}");
foreach (var name in blocked)
	Console.WriteLine($"  BLOCKED: {name}");

return failed > 0 ? 1 : 0;

void Assert(string name, bool condition, string? error = null)
{
	if (condition)
	{
		results.Add((name, true, null));
		Console.WriteLine($"  PASS: {name}");
	}
	else
	{
		results.Add((name, false, error));
		Console.WriteLine($"  FAIL: {name}{(error is not null ? $" — {error}" : "")}");
	}
}

async Task ChildStubExitZeroAsync()
{
	int code = await SpawnStubAsync("--exit 0");
	Assert("ChildStubExitZero", code == 0, $"exit code {code}");
}

async Task ChildStubExitNonzeroAsync()
{
	int code = await SpawnStubAsync("--exit 3");
	Assert("ChildStubExitNonzero", code == 3, $"exit code {code}");
}

async Task ChildStubOutputVolumeAsync()
{
	(int code, string stdout) = await SpawnStubWithOutputAsync("--output 50");
	int lineCount = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
	Assert("ChildStubOutputVolume", code == 0 && lineCount == 50, $"exit {code}, lines {lineCount}");
}

async Task ChildStubDelayAsync()
{
	var sw = Stopwatch.StartNew();
	int code = await SpawnStubAsync("--delay 200");
	sw.Stop();
	Assert("ChildStubDelay", code == 0 && sw.ElapsedMilliseconds < 1000, $"exit {code}, {sw.ElapsedMilliseconds}ms");
}

async Task ChildStubIgnoreTerminationAsync()
{
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert("ChildStubIgnoreTermination", false, "ProcessPath is null");
		return;
	}
	ProcessStartInfo psi = new()
	{
		FileName = exePath,
		Arguments = "--stub --ignore-termination --delay 60000",
		UseShellExecute = false,
		RedirectStandardOutput = true,
		CreateNoWindow = true,
		WorkingDirectory = tempRoot,
	};
	Process? process = Process.Start(psi);
	if (process is null)
	{
		Assert("ChildStubIgnoreTermination", false, "failed to start");
		return;
	}
	try
	{
		await Task.Delay(200);
		process.Kill(entireProcessTree: true);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await process.WaitForExitAsync(cts.Token);
		Assert("ChildStubIgnoreTermination", process.HasExited, "process not reaped after kill");
	}
	finally
	{
		if (!process.HasExited)
		{
			try { process.Kill(entireProcessTree: true); }
			catch (InvalidOperationException) { }
			using var finallyCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			try { await process.WaitForExitAsync(finallyCts.Token); }
			catch (OperationCanceledException)
			{
				Console.WriteLine("  FAIL: ChildStubIgnoreTermination — fallback kill timed out, possible orphan");
				results.Add(("ChildStubIgnoreTermination", false, "fallback kill timed out after 3s"));
			}
		}
		process.Dispose();
	}
}

async Task CompleteClearsFailedAsync()
{
	string testIso = Path.Combine(tempRoot, "test-complete-clears.iso");
	var guard = await ReprocessGuard.LoadAsync();

	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);
	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);

	ReprocessGuard.GuardEntry? entry = guard.Get(testIso);
	bool isFailed = entry?.Verdict == DiscState.Failed;

	await guard.RecordAsync(testIso, DiscState.Complete);

	entry = guard.Get(testIso);
	bool cleared = entry is null;

	Assert("CompleteClearsFailed", cleared, $"entry still exists: {entry?.Verdict}({entry?.ConsecutiveCount})");

	await guard.ResetAsync(testIso);
}

async Task DifferingNonCompleteIncrementsAsync()
{
	string testIso = Path.Combine(tempRoot, "test-differing-increments.iso");
	var guard = await ReprocessGuard.LoadAsync();

	await guard.RecordAsync(testIso, DiscState.NeedsExtraction);

	await guard.RecordAsync(testIso, DiscState.NeedsPrimaryConversion);

	ReprocessGuard.GuardEntry? entry = guard.Get(testIso);
	int count = entry?.ConsecutiveCount ?? 0;

	Assert("DifferingNonCompleteIncrements", count == 2, $"count={count}, expected=2");

	await guard.ResetAsync(testIso);
}

async Task ProcessRunnerStartFailedAsync()
{
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		"/nonexistent/binary.exe",
		[],
		CancellationToken.None
	);

	bool isStartFailed = result.IsError ||
		(result.Value.TerminationReason == TerminationReason.StartFailed);

	Assert("ProcessRunnerStartFailed", isStartFailed,
		result.IsError ? $"error={result.Errors[0].Description}" : $"reason={result.Value.TerminationReason}");
}

async Task ReflectionAccessAsync()
{
	Type checkerType = typeof(FlacCompletenessChecker);
	MethodInfo? getFlacsMethod = checkerType.GetMethod("GetFlacsByTrackNumber",
		BindingFlags.Static | BindingFlags.NonPublic);
	MethodInfo? findDffMethod = checkerType.GetMethod("FindDffDir",
		BindingFlags.Static | BindingFlags.NonPublic);

	if (getFlacsMethod is null || findDffMethod is null)
	{
		Assert("ReflectionAccess", false, "method not found");
		return;
	}

	string testDir = Path.Combine(tempRoot, "test-flacs");
	Directory.CreateDirectory(testDir);
	await File.WriteAllTextAsync(Path.Combine(testDir, "01. track.flac"), "fake");
	await File.WriteAllTextAsync(Path.Combine(testDir, "02. track.flac"), "fake");

	var result = getFlacsMethod.Invoke(null, new object[] { testDir });
	bool hasEntries = result is Dictionary<int, string> dict && dict.Count == 2;

	Assert("ReflectionAccess", hasEntries, $"result={result?.GetType().Name}");
}

async Task<int> SpawnStubAsync(string stubArgs)
{
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
		return -1;
	ProcessStartInfo psi = new()
	{
		FileName = exePath,
		Arguments = $"--stub {stubArgs}",
		UseShellExecute = false,
		RedirectStandardOutput = true,
		CreateNoWindow = true,
		WorkingDirectory = tempRoot,
	};
	Process? process = Process.Start(psi);
	if (process is null)
		return -1;
	await process.WaitForExitAsync();
	int code = process.ExitCode;
	process.Dispose();
	return code;
}

async Task<(int ExitCode, string Stdout)> SpawnStubWithOutputAsync(string stubArgs)
{
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
		return (-1, string.Empty);
	ProcessStartInfo psi = new()
	{
		FileName = exePath,
		Arguments = $"--stub {stubArgs}",
		UseShellExecute = false,
		RedirectStandardOutput = true,
		CreateNoWindow = true,
		WorkingDirectory = tempRoot,
	};
	Process? process = Process.Start(psi);
	if (process is null)
		return (-1, string.Empty);
	string stdout = await process.StandardOutput.ReadToEndAsync();
	await process.WaitForExitAsync();
	int code = process.ExitCode;
	process.Dispose();
	return (code, stdout);
}

static async Task<int> RunStubAsync(string[] args)
{
	int exitCode = 0;
	int outputLines = 0;
	int stderrLines = 0;
	int delayMs = 0;
	int completeAfterMs = 0;
	bool ignoreTermination = false;

	for (int i = 1; i < args.Length; i++)
	{
		switch (args[i])
		{
			case "--exit" when i + 1 < args.Length:
				exitCode = int.Parse(args[++i]);
				break;
			case "--output" when i + 1 < args.Length:
				outputLines = int.Parse(args[++i]);
				break;
			case "--delay" when i + 1 < args.Length:
				delayMs = int.Parse(args[++i]);
				break;
			case "--ignore-termination":
				ignoreTermination = true;
				break;
			case "--stderr" when i + 1 < args.Length:
				stderrLines = int.Parse(args[++i]);
				break;
			case "--complete-after" when i + 1 < args.Length:
				completeAfterMs = int.Parse(args[++i]);
				break;
		}
	}

	for (int i = 0; i < outputLines; i++)
		Console.WriteLine($"stub-output-{i}");

	for (int i = 0; i < stderrLines; i++)
		await Console.Error.WriteLineAsync($"stub-stderr-{i}");

	if (completeAfterMs > 0)
	{
		await Task.Delay(completeAfterMs);
		Console.WriteLine("DONE");
		await Task.Delay(Timeout.Infinite);
	}
	else if (ignoreTermination)
		await Task.Delay(Timeout.Infinite);
	else if (delayMs > 0)
		await Task.Delay(delayMs);

	return exitCode;
}

async Task GetFlacsByTrackNumberEmptyDirAsync()
{
	string testDir = Path.Combine(tempRoot, "p331-empty-flacs");
	Directory.CreateDirectory(testDir);
	Type checkerType = typeof(FlacCompletenessChecker);
	MethodInfo? getFlacsMethod = checkerType.GetMethod("GetFlacsByTrackNumber",
		BindingFlags.Static | BindingFlags.NonPublic);
	if (getFlacsMethod is null)
	{
		Assert("P3.3.1_GetFlacsByTrackNumber_EmptyDir [FlacCompletenessChecker L108-122]", false, "method not found");
		return;
	}
	var getFlacs = getFlacsMethod.CreateDelegate<Func<string, Dictionary<int, string>>>();
	Dictionary<int, string> dict = getFlacs(testDir);
	bool isEmpty = dict.Count == 0;
	Assert(
		"P3.3.1_GetFlacsByTrackNumber_EmptyDir [FlacCompletenessChecker L108-122]",
		isEmpty,
		$"resultType={dict.GetType().Name} count={dict.Count}"
	);
}

async Task GetFlacsByTrackNumberNumberedFlacsAsync()
{
	string testDir = Path.Combine(tempRoot, "p332-numbered-flacs");
	Directory.CreateDirectory(testDir);
	await File.WriteAllTextAsync(Path.Combine(testDir, "01. First.flac"), "fake");
	await File.WriteAllTextAsync(Path.Combine(testDir, "02. Second.flac"), "fake");
	await File.WriteAllTextAsync(Path.Combine(testDir, "03. Third.flac"), "fake");
	Type checkerType = typeof(FlacCompletenessChecker);
	MethodInfo? getFlacsMethod = checkerType.GetMethod("GetFlacsByTrackNumber",
		BindingFlags.Static | BindingFlags.NonPublic);
	if (getFlacsMethod is null)
	{
		Assert("P3.3.2_GetFlacsByTrackNumber_NumberedFlacs [FlacCompletenessChecker L108-122, TrackNumberPattern L10-13]", false, "method not found");
		return;
	}
	var getFlacs = getFlacsMethod.CreateDelegate<Func<string, Dictionary<int, string>>>();
	Dictionary<int, string> dict = getFlacs(testDir);
	bool correctCount = dict.Count == 3;
	bool hasKeys = dict.ContainsKey(1) && dict.ContainsKey(2) && dict.ContainsKey(3);
	string keys = string.Join(",", dict.Keys.Order());
	Assert(
		"P3.3.2_GetFlacsByTrackNumber_NumberedFlacs [FlacCompletenessChecker L108-122, TrackNumberPattern L10-13]",
		correctCount && hasKeys,
		$"count={dict.Count} keys=[{keys}]"
	);
}

async Task FindDffDirInnerExistsAsync()
{
	string channelDir = Path.Combine(tempRoot, "p333-channel");
	string discName = "TestDisc";
	string inner = Path.Combine(channelDir, discName);
	Directory.CreateDirectory(inner);
	Type checkerType = typeof(FlacCompletenessChecker);
	MethodInfo? findDffMethod = checkerType.GetMethod("FindDffDir",
		BindingFlags.Static | BindingFlags.NonPublic);
	if (findDffMethod is null)
	{
		Assert("P3.3.3_FindDffDir_InnerExists [FlacCompletenessChecker L124-132]", false, "method not found");
		return;
	}
	var findDff = findDffMethod.CreateDelegate<Func<string, string, string>>();
	string result = findDff(channelDir, discName);
	string normalizedResult = Path.GetFullPath(result);
	string normalizedInner = Path.GetFullPath(inner);
	Assert(
		"P3.3.3_FindDffDir_InnerExists [FlacCompletenessChecker L124-132]",
		string.Equals(normalizedResult, normalizedInner, StringComparison.OrdinalIgnoreCase),
		$"expected={normalizedInner} got={normalizedResult}"
	);
}

async Task FindDffDirFallbackToDffParentAsync()
{
	string channelDir = Path.Combine(tempRoot, "p334-fallback");
	string discName = "MissingDisc";
	string dffSubdir = Path.Combine(channelDir, "SomeSubdir");
	Directory.CreateDirectory(dffSubdir);
	await File.WriteAllBytesAsync(Path.Combine(dffSubdir, "test.dff"), [0x00]);
	Type checkerType = typeof(FlacCompletenessChecker);
	MethodInfo? findDffMethod = checkerType.GetMethod("FindDffDir",
		BindingFlags.Static | BindingFlags.NonPublic);
	if (findDffMethod is null)
	{
		Assert("P3.3.4_FindDffDir_FallbackToDffParent [FlacCompletenessChecker L130-138]", false, "method not found");
		return;
	}
	var findDff = findDffMethod.CreateDelegate<Func<string, string, string>>();
	string result = findDff(channelDir, discName);
	string normalizedResult = Path.GetFullPath(result);
	string normalizedExpected = Path.GetFullPath(dffSubdir);
	Assert(
		"P3.3.4_FindDffDir_FallbackToDffParent [FlacCompletenessChecker L130-138]",
		string.Equals(normalizedResult, normalizedExpected, StringComparison.OrdinalIgnoreCase),
		$"expected={normalizedExpected} got={normalizedResult}"
	);
}

async Task InspectorNoCueNoDffNeedsExtractionAsync()
{
	string channelDir = Path.Combine(tempRoot, "p335-no-cue-no-dff");
	string discName = "EmptyDisc";
	Directory.CreateDirectory(Path.Combine(channelDir, discName));

	var processRunner = new ProcessRunner();
	var saracon = new SaraconService(processRunner, "saracon");
	var sox = new SoxService(processRunner, "sox");
	var metadata = new AudioMetadataService();
	var convertService = new DsdConvertService(saracon, sox, metadata);
	var cueParser = new CueParser();
	var flacChecker = new FlacCompletenessChecker(sox);
	var inspector = new DiscOutputInspector(cueParser, convertService, flacChecker);

	DiscOutputInspector.DiscAssessment assessment = await inspector.EvaluateDiscAsync(
		channelDir, discName, CancellationToken.None);

	Assert(
		"P3.3.5_Inspector_NoCueNoDff_NeedsExtraction [DiscOutputInspector L26-77]",
		assessment.State == DiscState.NeedsExtraction
			&& assessment.CueTrackCount == 0
			&& assessment.PrimaryFlacCount == 0,
		$"state={assessment.State} cue={assessment.CueTrackCount} flacs={assessment.PrimaryFlacCount}"
	);
}

async Task InspectorNoCueInvalidDffNeedsExtractionAsync()
{
	string channelDir = Path.Combine(tempRoot, "p336-invalid-dff");
	string discName = "BadDffDisc";
	string dffDir = Path.Combine(channelDir, discName);
	Directory.CreateDirectory(dffDir);
	await File.WriteAllBytesAsync(Path.Combine(dffDir, "garbage.dff"), [0xFF, 0xFE, 0xFD]);

	var processRunner = new ProcessRunner();
	var saracon = new SaraconService(processRunner, "saracon");
	var sox = new SoxService(processRunner, "sox");
	var metadata = new AudioMetadataService();
	var convertService = new DsdConvertService(saracon, sox, metadata);
	var cueParser = new CueParser();
	var flacChecker = new FlacCompletenessChecker(sox);
	var inspector = new DiscOutputInspector(cueParser, convertService, flacChecker);

	DiscOutputInspector.DiscAssessment assessment = await inspector.EvaluateDiscAsync(
		channelDir, discName, CancellationToken.None);

	Assert(
		"P3.3.6_Inspector_NoCueInvalidDff_NeedsExtraction [DiscOutputInspector L47-59, L64-77]",
		assessment.State == DiscState.NeedsExtraction && assessment.CueTrackCount == 0,
		$"state={assessment.State} cue={assessment.CueTrackCount}"
	);
}

async Task InspectorNoCueValidDffInvalidArtifactsAsync()
{
	string channelDir = Path.Combine(tempRoot, "p337-valid-dff");
	string discName = "GoodDffDisc";
	string dffDir = Path.Combine(channelDir, discName);
	Directory.CreateDirectory(dffDir);
	byte[] syntheticDff = BuildSyntheticDff(2822400, 2);
	await File.WriteAllBytesAsync(Path.Combine(dffDir, "test.dff"), syntheticDff);

	var processRunner = new ProcessRunner();
	var saracon = new SaraconService(processRunner, "saracon");
	var sox = new SoxService(processRunner, "sox");
	var metadata = new AudioMetadataService();
	var convertService = new DsdConvertService(saracon, sox, metadata);
	var cueParser = new CueParser();
	var flacChecker = new FlacCompletenessChecker(sox);
	var inspector = new DiscOutputInspector(cueParser, convertService, flacChecker);

	DiscOutputInspector.DiscAssessment assessment = await inspector.EvaluateDiscAsync(
		channelDir, discName, CancellationToken.None);

	Assert(
		"P3.3.7_Inspector_NoCueValidDff_InvalidArtifacts [DiscOutputInspector L50-59, L71-72]",
		assessment.State == DiscState.InvalidArtifacts && assessment.CueTrackCount == 0,
		$"state={assessment.State} cue={assessment.CueTrackCount}"
	);
}

async Task OrchestratorGuardSkipBlockedAsync()
{
	string orchestratorSignature =
		"PipelineOrchestrator(SacdExtractService, DsdConvertService, DiscOutputInspector, CueParser, PathValidator, DiskSpaceChecker)";
	string blocker =
		"sacd_extract/saracon/sox binaries absent; no ISO fixtures; integration requires full toolchain [P3.3 harness/environment]";

	string caseName = "P3.3.8_OrchestratorGuardSkip_Blocked [PipelineOrchestrator L8-15, L84-97]";
	blocked.Add($"{caseName} — {blocker} signature={orchestratorSignature}");
	Console.WriteLine($"  BLOCKED: {caseName} — {blocker} signature={orchestratorSignature}");
}

static byte[] BuildSyntheticDff(int sampleRate, short channels)
{
	int propDataSize = 4 + 16 + 14;
	int formSize = 4 + 12 + propDataSize;
	int totalSize = 12 + formSize;

	byte[] buf = new byte[totalSize];
	Span<byte> span = buf.AsSpan();

	Encoding.ASCII.GetBytes("FRM8").CopyTo(span[0..4]);
	BinaryPrimitives.WriteUInt64BigEndian(span[4..12], (ulong)formSize);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(span[12..16]);
	Encoding.ASCII.GetBytes("PROP").CopyTo(span[16..20]);
	BinaryPrimitives.WriteUInt64BigEndian(span[20..28], (ulong)propDataSize);
	Encoding.ASCII.GetBytes("SND ").CopyTo(span[28..32]);
	Encoding.ASCII.GetBytes("FS  ").CopyTo(span[32..36]);
	BinaryPrimitives.WriteUInt64BigEndian(span[36..44], 4);
	BinaryPrimitives.WriteUInt32BigEndian(span[44..48], (uint)sampleRate);
	Encoding.ASCII.GetBytes("CHNL").CopyTo(span[48..52]);
	BinaryPrimitives.WriteUInt64BigEndian(span[52..60], 2);
	BinaryPrimitives.WriteUInt16BigEndian(span[60..62], (ushort)channels);

	return buf;
}

static byte[] BuildId3ChunkBytes(int dataSize)
{
	int total = 12 + dataSize + (dataSize & 1);
	byte[] chunk = new byte[total];
	Encoding.ASCII.GetBytes("ID3 ").CopyTo(chunk.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(chunk.AsSpan(4, 8), (ulong)dataSize);
	return chunk;
}

static byte[] BuildDataChunkBytes(string id, int dataSize)
{
	int total = 12 + dataSize + (dataSize & 1);
	byte[] chunk = new byte[total];
	Encoding.ASCII.GetBytes(id).CopyTo(chunk.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(chunk.AsSpan(4, 8), (ulong)dataSize);
	return chunk;
}

static byte[] BuildDffWithId3Chunks(int count, int id3DataSize)
{
	byte[] id3 = BuildId3ChunkBytes(id3DataSize);
	int chunksSize = count * id3.Length;
	int formSize = 4 + chunksSize;
	int totalSize = 12 + formSize;
	byte[] buf = new byte[totalSize];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), (ulong)formSize);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	int offset = 16;
	for (int i = 0; i < count; i++)
	{
		id3.CopyTo(buf, offset);
		offset += id3.Length;
	}
	return buf;
}

static byte[] BuildDffWithOddPadBetweenId3s()
{
	byte[] id3 = BuildId3ChunkBytes(10);
	int dataLen = 5;
	int dataChunkTotal = 12 + dataLen + 1;
	int formSize = 4 + id3.Length + dataChunkTotal + id3.Length;
	int totalSize = 12 + formSize;
	byte[] buf = new byte[totalSize];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), (ulong)formSize);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	int offset = 16;
	id3.CopyTo(buf, offset);
	offset += id3.Length;
	Encoding.ASCII.GetBytes("DATA").CopyTo(buf.AsSpan(offset, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(offset + 4, 8), (ulong)dataLen);
	offset += dataChunkTotal;
	id3.CopyTo(buf, offset);
	return buf;
}

static byte[] BuildDffWithNestedPropId3()
{
	byte[] id3 = BuildId3ChunkBytes(10);
	byte[] fsChunk = BuildDataChunkBytes("FS  ", 4);
	int propDataSize = 4 + id3.Length + fsChunk.Length;
	int formSize = 4 + 12 + propDataSize;
	int totalSize = 12 + formSize;
	byte[] buf = new byte[totalSize];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), (ulong)formSize);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	Encoding.ASCII.GetBytes("PROP").CopyTo(buf.AsSpan(16, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(20, 8), (ulong)propDataSize);
	Encoding.ASCII.GetBytes("SND ").CopyTo(buf.AsSpan(28, 4));
	int offset = 32;
	id3.CopyTo(buf, offset);
	offset += id3.Length;
	fsChunk.CopyTo(buf, offset);
	return buf;
}

static byte[] BuildTruncatedDff()
{
	byte[] buf = new byte[48];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), 116);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	Encoding.ASCII.GetBytes("DATA").CopyTo(buf.AsSpan(16, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(20, 8), 100);
	return buf;
}

static byte[] BuildDffWithZeroSizeProp()
{
	int formSize = 4 + 12;
	int totalSize = 12 + formSize;
	byte[] buf = new byte[totalSize];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), (ulong)formSize);
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	Encoding.ASCII.GetBytes("PROP").CopyTo(buf.AsSpan(16, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(20, 8), 0);
	return buf;
}

static byte[] BuildDffWithShortFormSize()
{
	byte[] id3 = BuildId3ChunkBytes(10);
	byte[] data = BuildDataChunkBytes("DATA", 20);
	int actualFormSize = 4 + id3.Length + data.Length;
	int totalSize = 12 + actualFormSize;
	byte[] buf = new byte[totalSize];
	Encoding.ASCII.GetBytes("FRM8").CopyTo(buf.AsSpan(0, 4));
	BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(4, 8), (ulong)(actualFormSize - 4));
	Encoding.ASCII.GetBytes("DSD ").CopyTo(buf.AsSpan(12, 4));
	int offset = 16;
	id3.CopyTo(buf, offset);
	offset += id3.Length;
	data.CopyTo(buf, offset);
	return buf;
}

async Task P34StripFourTopLevelId3Async()
{
	string caseName = "P3.4.1_StripFourTopLevelId3 [DffMetadataStripper ScanAsync L136-183, CopyChunksAsync L186-241]";
	byte[] dffBytes = BuildDffWithId3Chunks(4, 10);
	string dffDir = Path.Combine(tempRoot, "p341-four-id3");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "four_id3.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);

	if (result.IsError)
	{
		Assert(caseName, false, $"strip error: {result.Errors[0].Description}");
		return;
	}

	string cleanPath = result.Value;
	bool outputExists = File.Exists(cleanPath);
	long outputSize = outputExists ? new FileInfo(cleanPath).Length : 0;

	var hasId3 = DffMetadataStripper.HasId3Chunk(cleanPath);
	bool noId3 = !hasId3.IsError && !hasId3.Value;
	bool correctSize = outputSize == 16;
	bool formSizeCorrect = false;
	if (outputExists)
	{
		byte[] header = new byte[12];
		using FileStream fs = File.OpenRead(cleanPath);
		fs.ReadExactly(header);
		ulong formSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
		formSizeCorrect = formSize == 4 && (formSize & 1) == 0;
	}

	Assert(caseName, outputExists && noId3 && correctSize && formSizeCorrect,
		$"exists={outputExists} size={outputSize} noId3={noId3} formSize={formSizeCorrect}");
}

async Task P34OddPadPreservedAsync()
{
	string caseName = "P3.4.2_OddPadPreserved [DffMetadataStripper CopyChunksAsync L186-241, ReadChunkAsync L243-257]";
	byte[] dffBytes = BuildDffWithOddPadBetweenId3s();
	string dffDir = Path.Combine(tempRoot, "p342-odd-pad");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "odd_pad.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);

	if (result.IsError)
	{
		Assert(caseName, false, $"strip error: {result.Errors[0].Description}");
		return;
	}

	string cleanPath = result.Value;
	bool outputExists = File.Exists(cleanPath);
	long outputSize = outputExists ? new FileInfo(cleanPath).Length : 0;

	bool correctSize = outputSize == 34;
	bool dataChunkOk = false;
	if (outputExists && outputSize >= 34)
	{
		byte[] outputBytes = new byte[34];
		using FileStream fs = File.OpenRead(cleanPath);
		fs.ReadExactly(outputBytes);
		bool hasDataId = Encoding.ASCII.GetString(outputBytes, 16, 4) == "DATA";
		ulong dataSize = BinaryPrimitives.ReadUInt64BigEndian(outputBytes.AsSpan(20, 8));
		dataChunkOk = hasDataId && dataSize == 5;
	}

	Assert(caseName, outputExists && correctSize && dataChunkOk,
		$"exists={outputExists} size={outputSize} dataChunk={dataChunkOk}");
}

async Task P34NestedPropId3RemovedAsync()
{
	string caseName = "P3.4.3_NestedPropId3Removed [DffMetadataStripper ScanAsync L164-175, CopyChunksAsync L207-234]";
	byte[] dffBytes = BuildDffWithNestedPropId3();
	string dffDir = Path.Combine(tempRoot, "p343-nested-prop");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "nested_id3.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);

	if (result.IsError)
	{
		Assert(caseName, false, $"strip error: {result.Errors[0].Description}");
		return;
	}

	string cleanPath = result.Value;
	bool outputExists = File.Exists(cleanPath);
	long outputSize = outputExists ? new FileInfo(cleanPath).Length : 0;

	bool correctSize = outputSize == 48;
	var hasId3 = DffMetadataStripper.HasId3Chunk(cleanPath);
	bool noId3 = !hasId3.IsError && !hasId3.Value;

	bool propSizeCorrect = false;
	if (outputExists && outputSize >= 48)
	{
		byte[] outputBytes = new byte[48];
		using FileStream fs = File.OpenRead(cleanPath);
		fs.ReadExactly(outputBytes);
		bool hasPropId = Encoding.ASCII.GetString(outputBytes, 16, 4) == "PROP";
		ulong propSize = BinaryPrimitives.ReadUInt64BigEndian(outputBytes.AsSpan(20, 8));
		propSizeCorrect = hasPropId && propSize == 20;
	}

	Assert(caseName, outputExists && correctSize && noId3 && propSizeCorrect,
		$"exists={outputExists} size={outputSize} noId3={noId3} propSize={propSizeCorrect}");
}

async Task P34TruncatedErrorNoOutputAsync()
{
	string caseName = "P3.4.4_TruncatedErrorNoOutput [DffMetadataStripper ReadChunkAsync L243-257, ValidateDffHeader L259-278]";
	byte[] dffBytes = BuildTruncatedDff();
	string dffDir = Path.Combine(tempRoot, "p344-truncated");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "truncated.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);

	bool isError = result.IsError;
	bool noOutput = !Directory.Exists(outputDir);

	Assert(caseName, isError && noOutput,
		$"isError={isError} noOutput={noOutput} error={(isError ? result.Errors[0].Description : "n/a")}");
}

async Task P34ZeroSizePropErrorAsync()
{
	string caseName = "P3.4.5_ZeroSizePropError [DffMetadataStripper ScanAsync L164-168]";
	byte[] dffBytes = BuildDffWithZeroSizeProp();
	string dffDir = Path.Combine(tempRoot, "p345-zero-prop");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "zero_prop.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);

	bool isError = result.IsError;
	bool noOutput = !Directory.Exists(outputDir);

	Assert(caseName, isError && noOutput,
		$"isError={isError} noOutput={noOutput} error={(isError ? result.Errors[0].Description : "n/a")}");
}

async Task P34ShortFormSizeWarnsAsync()
{
	string caseName = "P3.4.6_ShortFormSizeWarnsRepairs [DffMetadataStripper ValidateDffHeader L259-278, StripId3TagsAsync L39-133]";
	byte[] dffBytes = BuildDffWithShortFormSize();
	string dffDir = Path.Combine(tempRoot, "p346-short-size");
	Directory.CreateDirectory(dffDir);
	string dffPath = Path.Combine(dffDir, "short_size.dff");
	string outputDir = Path.Combine(dffDir, "output");
	await File.WriteAllBytesAsync(dffPath, dffBytes);

	bool threw = false;
	string cleanPath = string.Empty;
	try
	{
		var result = await DffMetadataStripper.StripId3TagsAsync(dffPath, outputDir);
		if (!result.IsError)
			cleanPath = result.Value;
	}
	catch (Exception)
	{
		threw = true;
	}

	if (threw)
	{
		Assert(caseName, false, "method threw exception");
		return;
	}

	bool outputExists = File.Exists(cleanPath);
	bool noId3 = false;
	if (outputExists)
	{
		var hasId3 = DffMetadataStripper.HasId3Chunk(cleanPath);
		noId3 = !hasId3.IsError && !hasId3.Value;
	}

	Assert(caseName, outputExists && noId3,
		$"exists={outputExists} noId3={noId3}");
}

async Task P34RealDisc3StreamedAsync()
{
	string caseName = "P3.4.7_RealDisc3Streamed [DffMetadataStripper StripId3TagsAsync L39-133, P3.4/P5 owner]";
	
	string candidatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "Music", "Karajan 1970-79 Berlin (Stereo)", "Disc 3", "Disc 3");
	string dffFile = Path.Combine(candidatePath, "Disc 3.dff");

	if (!File.Exists(dffFile))
	{
		blocked.Add($"{caseName} — Real Disc3 DFF path absent");
		Console.WriteLine($"  BLOCKED: {caseName} — Real Disc3 DFF path absent");
		return;
	}
	
	long origSize = new FileInfo(dffFile).Length;
	if (origSize != 3332711216)
	{
		Assert(caseName, false, $"Expected orig size 3332711216 but got {origSize}");
		return;
	}

	string outDir = Path.Combine(tempRoot, "p347-real-disc3");
	var result = await DffMetadataStripper.StripId3TagsAsync(dffFile, outDir);
	
	if (result.IsError)
	{
		Assert(caseName, false, $"strip error: {result.Errors[0].Description}");
		return;
	}
	
	string cleanPath = result.Value;
	long outSize = new FileInfo(cleanPath).Length;
	
	byte[] header = new byte[12];
	using FileStream fs = File.OpenRead(cleanPath);
	fs.ReadExactly(header);
	ulong formSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
	
	bool sizeDiffOk = (origSize - outSize) == 1806;
	bool expectedOutSize = outSize == 3332709410;
	bool formSizeOk = formSize == 3332709398;
	
	Assert(caseName, sizeDiffOk && expectedOutSize && formSizeOk, $"sizeDiff={origSize - outSize} expectedOutSize={expectedOutSize} formSize={formSize}");
}

async Task P35Exit0WithStdoutAsync()
{
	string caseName = "P3.5.1_Exit0WithStdout [ProcessRunner.RunAsync, TerminationReason.Exited]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		exePath,
		["--stub", "--exit", "0", "--output", "10"],
		CancellationToken.None
	);
	bool pass = !result.IsError
		&& result.Value.ExitCode == 0
		&& result.Value.TerminationReason == TerminationReason.Exited
		&& result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 10;
	Assert(caseName, pass,
		result.IsError
			? $"error={result.Errors[0].Description}"
			: $"exit={result.Value.ExitCode} reason={result.Value.TerminationReason} lines={result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}");
}

async Task P35Exit3WithStderrAsync()
{
	string caseName = "P3.5.2_Exit3WithStderr [ProcessRunner.RunAsync, TerminationReason.Exited, stderr]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		exePath,
		["--stub", "--exit", "3", "--stderr", "5"],
		CancellationToken.None
	);
	bool pass = !result.IsError
		&& result.Value.ExitCode == 3
		&& result.Value.TerminationReason == TerminationReason.Exited
		&& result.Value.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 5;
	Assert(caseName, pass,
		result.IsError
			? $"error={result.Errors[0].Description}"
			: $"exit={result.Value.ExitCode} reason={result.Value.TerminationReason} stderrLines={result.Value.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}");
}

async Task P35CallerCancellationAsync()
{
	string caseName = "P3.5.3_CallerCancellation [ProcessRunner.RunAsync, TerminationReason.CallerCanceled]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	using CancellationTokenSource cts = new();
	await cts.CancelAsync();
	bool threwCanceled = false;
	TerminationReason capturedReason = default;
	try
	{
		await runner.RunAsync(
			exePath,
			["--stub", "--exit", "0", "--delay", "10000"],
			cts.Token
		);
	}
	catch (ProcessRunnerCanceledException ex)
	{
		threwCanceled = true;
		capturedReason = ex.Result.TerminationReason;
	}
	Assert(caseName, threwCanceled && capturedReason == TerminationReason.CallerCanceled,
		$"threw={threwCanceled} reason={capturedReason}");
}

async Task P35TimeoutAsync()
{
	string caseName = "P3.5.4_Timeout [ProcessRunner.RunAsync, TerminationReason.Timeout]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		exePath,
		["--stub", "--exit", "0", "--delay", "10000"],
		CancellationToken.None,
		timeout: TimeSpan.FromMilliseconds(200)
	);
	bool pass = !result.IsError
		&& result.Value.TerminationReason == TerminationReason.Timeout;
	Assert(caseName, pass,
		result.IsError
			? $"error={result.Errors[0].Description}"
			: $"reason={result.Value.TerminationReason}");
}

async Task P35CompletionMarkerHangAsync()
{
	string caseName = "P3.5.5_CompletionMarkerHang [ProcessRunner.RunAsync, TerminationReason.KilledAfterCompletionMarker]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		exePath,
		["--stub", "--complete-after", "100"],
		CancellationToken.None,
		completionPattern: "DONE",
		completionTimeout: TimeSpan.FromMilliseconds(200)
	);
	bool pass = !result.IsError
		&& result.Value.TerminationReason == TerminationReason.KilledAfterCompletionMarker;
	Assert(caseName, pass,
		result.IsError
			? $"error={result.Errors[0].Description}"
			: $"reason={result.Value.TerminationReason}");
}

async Task P35HighVolumeStdoutDrainAsync()
{
	string caseName = "P3.5.6_HighVolumeStdoutDrain [ProcessRunner.RunAsync, output drain]";
	string? exePath = Environment.ProcessPath;
	if (exePath is null)
	{
		Assert(caseName, false, "ProcessPath is null");
		return;
	}
	ProcessRunner runner = new();
	var result = await runner.RunAsync(
		exePath,
		["--stub", "--exit", "0", "--output", "1000"],
		CancellationToken.None
	);
	int lineCount = result.IsError ? 0 : result.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
	bool pass = !result.IsError
		&& result.Value.ExitCode == 0
		&& result.Value.TerminationReason == TerminationReason.Exited
		&& lineCount == 1000;
	Assert(caseName, pass,
		result.IsError
			? $"error={result.Errors[0].Description}"
			: $"exit={result.Value.ExitCode} lines={lineCount}");
}
