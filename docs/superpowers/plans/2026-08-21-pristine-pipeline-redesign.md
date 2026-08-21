# Pristine Album Pipeline Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Pristine API-path album pipeline's per-track resume-then-directory-scan-transcode shape with an explicit upfront per-album decision (local-state-first, live-probe only when something needs fetching), a bit-depth-and-rate target matrix, unified corrupt-download/corrupt-transcode retry handling, and a three-bucket final batch summary.

**Architecture:** A new pure `AudioTranscodeTarget` helper in `Services.Audio` centralizes the bits/rate target rule, shared by both the transcode engine and the new album classifier. A new pure `PristineAlbumClassifier` in `Services.Pristine` decides, from local file state alone, whether an album is already complete or needs work — no network call for a fully-resumed album. `PristineApiPollService` is rewritten around that classifier: live-probe only fires for tracks actually being fetched, downloads and transcodes are strictly sequential phases (never interleaved), and both corrupt downloads and broken transcodes get an internal 3-attempt retry before being recorded as a named per-track failure rather than crashing anything.

**Tech Stack:** C#/.NET 11, ErrorOr, Serilog (via `Telemetry`), ffprobe/sox (external processes via `ProcessRunner`). No test framework in this repo — verification is `dotnet build --no-incremental` (scorched-earth per project convention) plus throwaway scratch scripts for pure logic, never committed.

**Spec:** `docs/superpowers/specs/2026-08-21-pristine-pipeline-redesign-design.md`

## Global Constraints

- No test NuGet packages (no xUnit/NUnit/MSTest) — pure-logic verification via a throwaway scratch console script in the scratchpad, run once, deleted before committing. Never commit a `Main()` verification file into `src/`.
- One class per file. No `Constants.cs`/`Helpers.cs`.
- Tabs for indentation, matching `.editorconfig` (enforced as build errors — local functions and private members must be camelCase, per this session's own IDE1006 lesson).
- `ErrorOr<T>` via `Errors.{Domain}` factories for anything fallible that crosses a method boundary meant to be soft-failed. Internal invariant violations on our own generated files may still throw where the spec says so — but per the spec, this redesign removes the one place that used to throw (`TranscodePipelineException`) in favor of retry-then-soft-fail.
- `Telemetry.Info`/`Telemetry.Warn` are plain-sentence, user-facing console lines (no `key={Value}` field-soup) — that style is `Telemetry.Debug`-only. Per-track lines indented two spaces; album-level lines unindented.
- Before every commit: `dotnet clean` → delete `artifacts/bin`, `artifacts/obj` → `dotnet build --no-incremental` → `dprint fmt .` → re-verify build clean. This is a standing project mandate (`~/.claude/CLAUDE.md`), not optional per-task busywork — every task below ends with it.
- Commit after each task. 1-3 files per commit, descriptive conventional-commit message.
- Scope: `PristinePollService.cs` (browser-fallback path) is explicitly **out of scope** — do not modify its control flow. `PristineVerification.cs`/`PristineAudioVerifier.cs` changes in Task 4 are shared by both paths and are an in-scope bugfix (removes an incorrect bit-depth gate), not a browser-path redesign.

---

## Task 1: `AudioTranscodeTarget` — shared target-computation helper

**Files:**

- Create: `src/Services/Audio/AudioTranscodeTarget.cs`

**Interfaces:**

- Produces: `AudioTranscodeTarget.Resolve(int sourceBits, int sourceRate) -> (int Bits, int Rate)`, `AudioTranscodeTarget.NeedsTranscode(int bits, int rate) -> bool`. Both are used by Task 2 (`FlacTranscodeService`) and Task 6 (`PristineAlbumClassifier`).

- [ ] **Step 1: Write the file**

```csharp
namespace Services.Audio;

public static class AudioTranscodeTarget
{
	public static (int Bits, int Rate) Resolve(int sourceBits, int sourceRate)
	{
		var targetBits = sourceBits == 24 ? 16 : sourceBits;
		var targetRate = sourceRate;
		while (targetRate > 48000)
			targetRate /= 2;
		return (targetBits, targetRate);
	}

	public static bool NeedsTranscode(int bits, int rate)
	{
		(int targetBits, int targetRate) = Resolve(bits, rate);
		return targetBits != bits || targetRate != rate;
	}
}
```

- [ ] **Step 2: Scratch-verify against the spec's full matrix**

Create a throwaway file at the scratchpad path (NOT under `src/`), e.g.
`C:\Users\Lance\AppData\Local\Temp\claude\...\scratchpad\verify_target.csx`-equivalent — simplest is a one-off console project. From the repo root:

```bash
mkdir -p /tmp/audiotarget_verify && cd /tmp/audiotarget_verify
cat > Program.cs <<'EOF'
(int, int)[] cases =
[
    (16, 44100), (16, 48000), (16, 88200), (16, 96000), (16, 176400), (16, 192000),
    (24, 44100), (24, 48000), (24, 88200), (24, 96000), (24, 176400), (24, 192000),
];
foreach ((int bits, int rate) in cases)
{
    var target = Services.Audio.AudioTranscodeTarget.Resolve(bits, rate);
    var needs = Services.Audio.AudioTranscodeTarget.NeedsTranscode(bits, rate);
    Console.WriteLine($"{bits}/{rate} -> {target.Bits}/{target.Rate} (transcode={needs})");
}
EOF
```

This needs a reference to the real `Services.Audio` project — easiest is to temporarily copy `AudioTranscodeTarget.cs` alongside this scratch `Program.cs` and compile them together with `csc` or `dotnet run` in an ad-hoc project, rather than fighting project references for a throwaway check. Compare output against the spec's 12-row table (reproduced below for this step only):

```
16/44100 -> 16/44100 (transcode=False)
16/48000 -> 16/48000 (transcode=False)
16/88200 -> 16/44100 (transcode=True)
16/96000 -> 16/48000 (transcode=True)
16/176400 -> 16/44100 (transcode=True)
16/192000 -> 16/48000 (transcode=True)
24/44100 -> 16/44100 (transcode=True)
24/48000 -> 16/48000 (transcode=True)
24/88200 -> 16/44100 (transcode=True)
24/96000 -> 16/48000 (transcode=True)
24/176400 -> 16/44100 (transcode=True)
24/192000 -> 16/48000 (transcode=True)
```

Delete the scratch directory once confirmed.

- [ ] **Step 3: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Format and commit**

```bash
dprint fmt .
git add src/Services/Audio/AudioTranscodeTarget.cs
git commit -m "feat(audio): add shared bit-depth/rate target resolver

Pulls the target-format rule (24-bit->16-bit; rate folds to the 44.1/48kHz
floor independent of bit depth) out of FlacTranscodeService into a pure,
reusable function the upcoming Pristine album classifier also needs."
```

---

## Task 2: `FlacTranscodeService` — consume the shared target, retry-then-soft-fail

**Files:**

- Modify: `src/Services/Audio/FlacTranscodeService.cs` (full rewrite of `TranscodeFileAsync`, remove `ResolveTargetSampleRate`, remove `TranscodePipelineException`)

**Interfaces:**

- Consumes: `AudioTranscodeTarget.Resolve`, `AudioTranscodeTarget.NeedsTranscode` (Task 1)
- Produces: `TranscodeOutcome` enum unchanged in shape (`Converted`, `SkippedNotFlac`, `SkippedAlreadyMp3`, `SkippedAlready16Bit`, `Failed`) — `SkippedAlready16Bit` now means "already target form" (bits AND rate both correct), not just "bits <= 16". `FlacTranscodeResult` unchanged.

- [ ] **Step 1: Replace the skip check and the whole convert/verify/move block**

Replace this block in `TranscodeFileAsync` (currently checks `probe.Bits is > 0 and <= 16` to skip, and calls a private `ResolveTargetSampleRate`):

```csharp
		if (probe.Bits is > 0 and <= 16)
		{
			Telemetry.Info("{File:l}: already {Bits}-bit, skipping", fileName, probe.Bits);
			return TranscodeOutcome.SkippedAlready16Bit;
		}

		var targetSampleRate = ResolveTargetSampleRate(probe.SampleRate);
		Telemetry.Info("Transcoding: {File:l}", fileName);
		var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.flac");
		ErrorOr<string> convertOr = await sox.DownsampleTo16BitAsync(
			file,
			tempDest,
			targetSampleRate,
			ct
		);
		if (convertOr.IsError)
		{
			Telemetry.Warn(
				"Audio.Transcode.SoxFailed file={File} err={Err}",
				fileName,
				convertOr.FirstError.Description
			);
			DeleteIfExists(tempDest);
			return TranscodeOutcome.Failed;
		}

		ErrorOr<FlacProbeResult> verifyOr = await ProbeAsync(tempDest, ct);
		if (
			verifyOr.IsError
			|| verifyOr.Value.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase) is false
			|| verifyOr.Value.Bits != 16
			|| verifyOr.Value.SampleRate != targetSampleRate
		)
		{
			var got = verifyOr.IsError
				? verifyOr.FirstError.Description
				: $"bits={verifyOr.Value.Bits} rate={verifyOr.Value.SampleRate}";
			DeleteIfExists(tempDest);
			DeleteIfExists(file);
			throw new TranscodePipelineException(
				$"sox reported success but its own output for {fileName} failed verification (wanted 16-bit/{targetSampleRate}Hz flac, got {got}) — our transcode pipeline is broken, not the source file"
			);
		}

		try
		{
			File.Move(tempDest, file, overwrite: true);
		}
		catch (Exception ex)
		{
			throw new TranscodePipelineException(
				$"could not move our own verified-good transcode into place for {fileName}: {ex.Message}"
			);
		}

		Telemetry.Info("{File:l}: {FromBits}-bit → 16-bit", fileName, probe.Bits);
		return TranscodeOutcome.Converted;
	}
```

with:

```csharp
		if (AudioTranscodeTarget.NeedsTranscode(probe.Bits, probe.SampleRate) is false)
		{
			Telemetry.Info(
				"{File:l}: already {Bits}-bit/{Rate}Hz, skipping",
				fileName,
				probe.Bits,
				probe.SampleRate
			);
			return TranscodeOutcome.SkippedAlready16Bit;
		}

		(int targetBits, int targetRate) = AudioTranscodeTarget.Resolve(probe.Bits, probe.SampleRate);
		Telemetry.Info("Transcoding: {File:l}", fileName);

		const int maxAttempts = 3;
		for (var attempt = 1; attempt <= maxAttempts; attempt++)
		{
			var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.flac");
			ErrorOr<string> convertOr = await sox.DownsampleTo16BitAsync(
				file,
				tempDest,
				targetRate,
				ct
			);
			if (convertOr.IsError)
			{
				Telemetry.Warn(
					"Audio.Transcode.SoxFailed file={File} attempt={Attempt}/{Max} err={Err}",
					fileName,
					attempt,
					maxAttempts,
					convertOr.FirstError.Description
				);
				DeleteIfExists(tempDest);
				continue;
			}

			ErrorOr<FlacProbeResult> verifyOr = await ProbeAsync(tempDest, ct);
			var verifyOk =
				verifyOr.IsError is false
				&& verifyOr.Value.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
				&& verifyOr.Value.Bits == targetBits
				&& verifyOr.Value.SampleRate == targetRate;
			if (verifyOk is false)
			{
				var got = verifyOr.IsError
					? verifyOr.FirstError.Description
					: $"bits={verifyOr.Value.Bits} rate={verifyOr.Value.SampleRate}";
				Telemetry.Warn(
					"Audio.Transcode.VerifyFailed file={File} attempt={Attempt}/{Max} wanted={WantBits}-bit/{WantRate}Hz got={Got}",
					fileName,
					attempt,
					maxAttempts,
					targetBits,
					targetRate,
					got
				);
				DeleteIfExists(tempDest);
				continue;
			}

			try
			{
				File.Move(tempDest, file, overwrite: true);
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"Audio.Transcode.ReplaceFailed file={File} attempt={Attempt}/{Max}: {Error}",
					fileName,
					attempt,
					maxAttempts,
					ex.Message
				);
				DeleteIfExists(tempDest);
				continue;
			}

			Telemetry.Info(
				"{File:l}: {FromBits}-bit/{FromRate}Hz → {ToBits}-bit/{ToRate}Hz",
				fileName,
				probe.Bits,
				probe.SampleRate,
				targetBits,
				targetRate
			);
			return TranscodeOutcome.Converted;
		}

		Telemetry.Error(
			"Audio.Transcode.GaveUp file={File} attempts={Max} — sox/verify/move kept failing on our own output, not the source file",
			fileName,
			maxAttempts
		);
		return TranscodeOutcome.Failed;
	}
```

- [ ] **Step 2: Delete the now-unused `ResolveTargetSampleRate` method and the `TranscodePipelineException` class**

Remove this method entirely:

```csharp
private static int ResolveTargetSampleRate(int sourceSampleRate)
{
	var target = sourceSampleRate;
	while (target > 48000)
		target /= 2;
	return target;
}
```

Remove this class entirely (at the bottom of the file):

```csharp
public sealed class TranscodePipelineException(string message) : Exception(message);
```

- [ ] **Step 3: Update the success-line format note**

The success line changed shape from `"{File}: {FromBits}-bit → 16-bit"` (Task from the earlier session, rate omitted because it never changed) to `"{File}: {FromBits}-bit/{FromRate}Hz → {ToBits}-bit/{ToRate}Hz"` (rate is back, because now it sometimes DOES change — a 16-bit/96kHz source transcoding to 16-bit/48kHz needs the rate shown, or the line would misleadingly look like a no-op). This is intentional, not a regression of the earlier "drop the redundant rate" fix — that fix assumed rate never changed for a transcoded file, which this redesign's rate-floor-for-16-bit-too rule invalidates.

- [ ] **Step 4: Scorched-earth build — expect a compile error here**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: FAILS, because `src/Services/Pristine/PristineOrchestrator.cs` still references `TranscodePipelineException` in a `catch` clause. This is expected — Task 3 fixes it. Confirm the error is exactly that reference and nothing else in this file, then proceed.

- [ ] **Step 5: Commit**

Do NOT commit yet — Task 3 must land in the same build-passing state. Continue directly to Task 3, then commit both together (see Task 3 Step 3).

---

## Task 3: `PristineOrchestrator` — remove the now-dead `TranscodePipelineException` catch

**Files:**

- Modify: `src/Services/Pristine/PristineOrchestrator.cs:236-249` (the `try`/`catch (TranscodePipelineException ...)` around the `TranscodeDirectoryAsync` call)

**Interfaces:**

- Consumes: `FlacTranscodeService.TranscodeDirectoryAsync` no longer throws (Task 2) — always returns `ErrorOr<FlacTranscodeResult>`.

- [ ] **Step 1: Simplify `TranscodeAlbumAsync`**

Replace:

```csharp
ErrorOr<FlacTranscodeResult> transcodeOr;
try
{
	transcodeOr = await flacTranscode.TranscodeDirectoryAsync(r.OutPath, ct);
}
catch (TranscodePipelineException ex)
{
	Telemetry.Error(
		"Pristine.Orchestrator.TranscodePipelineBroken code={Code}: {Error} — our own transcode output is broken, skipping this album's transcode, batch continues",
		r.Code,
		ex.Message
	);
	return r;
}
if (transcodeOr.IsError)
```

with:

```csharp
ErrorOr<FlacTranscodeResult> transcodeOr = await flacTranscode.TranscodeDirectoryAsync(
	r.OutPath,
	ct
);
if (transcodeOr.IsError)
```

- [ ] **Step 2: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Format and commit (covers Task 2 + Task 3 together)**

```bash
dprint fmt .
git add src/Services/Audio/FlacTranscodeService.cs src/Services/Pristine/PristineOrchestrator.cs
git commit -m "refactor(audio,pristine): retry-then-soft-fail transcode, drop throw path

FlacTranscodeService.TranscodeFileAsync now retries its own sox/verify/move
cycle up to 3 times before giving up on a single file (soft Failed outcome,
loop continues to the next file) instead of throwing TranscodePipelineException
and aborting the whole album's transcode on the first broken file. Also now
uses the shared AudioTranscodeTarget rule, so a 16-bit source at an
above-floor rate (e.g. 16-bit/96kHz) gets its rate folded down too, not just
24-bit sources."
```

---

## Task 4: `PristineAudioVerifier`/`PristineVerification` — structural-only corruption check

**Files:**

- Modify: `src/Services/Pristine/PristineAudioVerifier.cs`
- Modify: `src/Services/Pristine/PristineVerification.cs`

**Interfaces:**

- Produces: `PristineProbeResult.IsValid` (renamed from `IsAcceptableFlac`) — `true` iff codec is `flac` or `mp3` and ffprobe could read at least one stream. No bit-depth requirement at all (this redesign's whole premise is that ANY bit depth Pristine actually serves is legitimate — target-decision is a separate, later concern, not part of "is this download corrupt").
- Consumes: unchanged by `PristinePollService.cs` (browser path, out of scope) — that file calls `verifier.VerifyAsync(...).Value.Bits is 16 or 24` directly in its own resume-check and will need the same field-rename applied mechanically (see Step 3) even though its _logic_ is untouched.

- [ ] **Step 1: Rewrite the acceptance check in `PristineAudioVerifier.VerifyAsync`**

Replace:

```csharp
var streamCount = streamsEl.GetArrayLength();
var isFlac = codec.Equals("flac", StringComparison.OrdinalIgnoreCase);
var isAcceptableBitDepth = bits is 16 or 24;
Telemetry.Debug(
	"Pristine.Verify.Result code={Code} track={Track} streams={Streams} codec={Codec} bits={Bits} rate={Rate} channels={Channels} path={Path}",
	code,
	trackNum,
	streamCount,
	codec,
	bits,
	sampleRate,
	channels,
	Path.GetFileName(filePath)
);
if (isFlac is false)
	Telemetry.Debug(
		"Pristine.Verify.NotFlac code={Code} track={Track} codec={Codec}",
		code,
		trackNum,
		codec
	);
if (isAcceptableBitDepth is false && bits != 0)
	Telemetry.Debug(
		"Pristine.Verify.UnexpectedBitDepth code={Code} track={Track} bits={Bits}",
		code,
		trackNum,
		bits
	);
return new PristineProbeResult(
	isFlac && isAcceptableBitDepth,
	codec,
	bits,
	sampleRate,
	channels,
	$"streams:{streamCount}"
```

with:

```csharp
var streamCount = streamsEl.GetArrayLength();
var isKnownCodec =
	codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
	|| codec.Equals("mp3", StringComparison.OrdinalIgnoreCase);
Telemetry.Debug(
	"Pristine.Verify.Result code={Code} track={Track} streams={Streams} codec={Codec} bits={Bits} rate={Rate} channels={Channels} path={Path}",
	code,
	trackNum,
	streamCount,
	codec,
	bits,
	sampleRate,
	channels,
	Path.GetFileName(filePath)
);
if (isKnownCodec is false)
	Telemetry.Debug(
		"Pristine.Verify.UnknownCodec code={Code} track={Track} codec={Codec}",
		code,
		trackNum,
		codec
	);
return new PristineProbeResult(
	isKnownCodec && streamCount > 0,
	codec,
	bits,
	sampleRate,
	channels,
	$"streams:{streamCount}"
```

- [ ] **Step 2: Rename the record field**

Replace:

```csharp
public sealed record PristineProbeResult(
	bool IsAcceptableFlac,
	string Codec,
	int Bits,
	int SampleRate,
	int Channels,
	string Note
);
```

with:

```csharp
public sealed record PristineProbeResult(
	bool IsValid,
	string Codec,
	int Bits,
	int SampleRate,
	int Channels,
	string Note
);
```

- [ ] **Step 3: Update every consumer of the renamed field**

Run `grep -rn "IsAcceptableFlac" src/` — expect matches in `PristineVerification.cs` and possibly `PristinePollService.cs` (its own inline resume-check uses `.Bits is 16 or 24` directly, not this field, so it likely has zero matches — confirm). In `PristineVerification.cs`, replace every `probe.IsAcceptableFlac` with `probe.IsValid` (two occurrences: the Debug log and the `if` check). Also update that Debug log's field name from `is24` to `valid` to match:

```csharp
PristineProbeResult probe = probeOr.Value;
Telemetry.Debug(
	"Pristine.Verify.Checked code={Code} track={Track} valid={Valid} codec={Codec} bits={Bits} dest={Dest} note={Note}",
	code,
	track,
	probe.IsValid,
	probe.Codec,
	probe.Bits,
	Path.GetFileName(dest),
	probe.Note
);
if (probe.IsValid is false)
{
	DeleteRejectedFile(
		dest,
		code,
		track,
		$"codec={probe.Codec} bits={probe.Bits} ({probe.Note})"
	);
	return false;
}
```

- [ ] **Step 4: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` If `PristinePollService.cs` fails to compile, it means it DID reference the old field name somewhere — fix that one reference mechanically (rename only, no logic change) and rebuild.

- [ ] **Step 5: Format and commit**

```bash
dprint fmt .
git add src/Services/Pristine/PristineAudioVerifier.cs src/Services/Pristine/PristineVerification.cs
git commit -m "fix(pristine): corruption check no longer gates on bit depth

PristineAudioVerifier's post-download check now only verifies structural
validity (known codec, at least one stream) — not bit depth. Bit depth is
no longer this check's job: this redesign moves the target-format decision
to an upfront live probe + local classifier, so a freshly-downloaded file
being 16-bit, 24-bit, or anything else Pristine actually serves is by
definition not corruption. Renamed IsAcceptableFlac -> IsValid to match."
```

---

## Task 5: `PristineModels` — per-track failure tracking

**Files:**

- Modify: `src/Services/Pristine/PristineModels.cs`

**Interfaces:**

- Produces: `TrackFailureReason` enum, `TrackFailure` record, `PristineAlbumResult.FailedTracks` (`IReadOnlyList<TrackFailure>`), `PristineAlbumResult.AlbumLevelFailure` (`string?`). Consumed by Task 7 (populates them) and Task 8 (reads them for the batch summary).

- [ ] **Step 1: Add the new types and extend `PristineAlbumResult`**

Replace:

```csharp
namespace Services.Pristine;

public sealed record PristineAlbumResult
{
	public required string Code { get; init; }
	public required string Title { get; init; }
	public required string OutPath { get; init; }
	public int Expected { get; init; }
	public int Downloaded { get; init; }
	public int Resumed { get; init; }
}
```

with:

```csharp
namespace Services.Pristine;

public enum TrackFailureReason
{
	DownloadCorruptExhausted,
	TranscodeBrokenExhausted,
}

public sealed record TrackFailure(int Position, string Title, TrackFailureReason Reason);

public sealed record PristineAlbumResult
{
	public required string Code { get; init; }
	public required string Title { get; init; }
	public required string OutPath { get; init; }
	public int Expected { get; init; }
	public int Downloaded { get; init; }
	public int Resumed { get; init; }
	public IReadOnlyList<TrackFailure> FailedTracks { get; init; } = [];
	public string? AlbumLevelFailure { get; init; }
}
```

`AlbumLevelFailure` is set (non-null) only when the album never got past resolve/probe (the spec's "Failed" bucket) — e.g. `"code not found"` or `"live probe failed: <reason>"`. `FailedTracks` non-empty with `AlbumLevelFailure` null is the spec's "Partial" bucket. Both empty/null is "Success".

- [ ] **Step 2: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` (Adding fields with defaults to a `record` with `required` members elsewhere doesn't break existing `new PristineAlbumResult { ... }` call sites, since the new fields aren't `required`.)

- [ ] **Step 3: Format and commit**

```bash
dprint fmt .
git add src/Services/Pristine/PristineModels.cs
git commit -m "feat(pristine): add per-track failure tracking to PristineAlbumResult

TrackFailure + TrackFailureReason let the final batch summary (Task 8) name
which specific tracks permanently failed and why, instead of just a raw
Downloaded/Expected count. AlbumLevelFailure distinguishes an album that
never started (code didn't resolve, probe failed) from one that partially
succeeded."
```

---

## Task 6: `PristineAlbumClassifier` — pure local-state-first decision

**Files:**

- Create: `src/Services/Pristine/PristineAlbumClassifier.cs`

**Interfaces:**

- Consumes: `AudioTranscodeTarget.NeedsTranscode` (Task 1), `PristineText.SanitizePathComponent`/`NormalizeTrackTitle`/`ClampFileName` (existing), `PristineApiTrack` (existing).
- Produces: `PristineAlbumClassifier.Classify(IReadOnlyList<PristineApiTrack> expectedTracks, string albumOut, Func<string, (int Bits, int Rate)?> probeLocal) -> AlbumPlan`. `probeLocal` is injected specifically so this stays pure/testable without touching ffprobe — Task 7 passes a real ffprobe-backed delegate, the scratch verification in Step 2 passes a fake in-memory one.

- [ ] **Step 1: Write the file**

```csharp
namespace Services.Pristine;

public static class PristineAlbumClassifier
{
	public enum AlbumPlanKind
	{
		AlreadyComplete,
		NeedsWork,
	}

	public sealed record TrackPlan(
		PristineApiTrack Track,
		string Dest,
		bool ExistsLocally,
		bool NeedsDownload,
		bool NeedsTranscode
	);

	public sealed record AlbumPlan(AlbumPlanKind Kind, IReadOnlyList<TrackPlan> Tracks);

	public static AlbumPlan Classify(
		IReadOnlyList<PristineApiTrack> expectedTracks,
		string albumOut,
		Func<string, (int Bits, int Rate)?> probeLocal
	)
	{
		List<TrackPlan> plans = [];
		foreach (PristineApiTrack track in expectedTracks)
		{
			var safeTitle = PristineText.SanitizePathComponent(
				PristineText.NormalizeTrackTitle(track.Title)
			);
			var stem = PristineText.ClampFileName(
				albumOut,
				$"{track.Position:00}. {safeTitle}",
				".flac"
			);
			var dest = Path.Combine(albumOut, $"{stem}.flac");
			var existsLocally = File.Exists(dest) && new FileInfo(dest).Length > 0;
			var needsTranscode = false;
			if (existsLocally)
			{
				(int Bits, int Rate)? probe = probeLocal(dest);
				needsTranscode =
					probe is null
					|| AudioTranscodeTarget.NeedsTranscode(probe.Value.Bits, probe.Value.Rate);
			}

			plans.Add(
				new TrackPlan(
					Track: track,
					Dest: dest,
					ExistsLocally: existsLocally,
					NeedsDownload: existsLocally is false,
					NeedsTranscode: needsTranscode
				)
			);
		}

		var allDone = plans.All(p => p.ExistsLocally && p.NeedsTranscode is false);
		return new AlbumPlan(
			allDone ? AlbumPlanKind.AlreadyComplete : AlbumPlanKind.NeedsWork,
			plans
		);
	}
}
```

MP3-only tracks are not FLAC and so never match a `.flac` dest — Task 7's live-probe/download step handles the MP3 case separately (an MP3 track is never locally-resumable-as-FLAC, so it always shows up as `NeedsDownload: true` here, which is correct: an MP3 track that's already been downloaded as `.mp3` needs its own separate local check, added in Task 7 alongside the MP3 download path itself, not here — this classifier's `.flac`-only dest assumption matches today's `DownloadTrackAsync`, which is FLAC-only; Task 7 extends both together).

- [ ] **Step 2: Scratch-verify with a fake probe function (no real ffprobe needed)**

```bash
mkdir -p /tmp/classifier_verify && cd /tmp/classifier_verify
# copy AudioTranscodeTarget.cs and PristineAlbumClassifier.cs (and their
# direct dependencies: PristineText.cs, PristineModels.cs's PristineApiTrack)
# alongside a scratch Program.cs that builds a 3-track expected list, an
# empty temp albumOut dir, and a probeLocal that always returns null (no
# local files) -> assert Kind == NeedsWork and every TrackPlan.NeedsDownload
# is true. Then repeat with albumOut containing 3 zero-byte-avoiding dummy
# files at the exact computed Dest paths and probeLocal returning (16, 44100)
# for all of them -> assert Kind == AlreadyComplete.
```

Delete the scratch directory once both cases pass.

- [ ] **Step 3: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Format and commit**

```bash
dprint fmt .
git add src/Services/Pristine/PristineAlbumClassifier.cs
git commit -m "feat(pristine): add pure local-state album classifier

Given an album's expected tracks and a probe function, decides whether the
album is already fully complete locally (no network needed beyond the
initial track-list resolve) or needs downloading/transcoding work — without
ever comparing against the live source, per the design spec's
local-state-first reasoning. probeLocal is injected so this stays testable
without a real ffprobe call."
```

---

## Task 7: `PristineApiPollService` — rewire around the classifier

**Files:**

- Modify: `src/Services/Pristine/PristineApiPollService.cs` (full rewrite of `ProcessAlbumAsync` and `DownloadTrackAsync`)

**Interfaces:**

- Consumes: `PristineAlbumClassifier.Classify` (Task 6), `AudioTranscodeTarget.Resolve` (Task 1), `PristineVerification.VerifyAndKeepAsync` (Task 4's structural-only version), `PristineModels.TrackFailure`/`TrackFailureReason` (Task 5).
- Produces: `PristineAlbumResult` now populated with `FailedTracks` and (on early-exit paths) `AlbumLevelFailure`.

- [ ] **Step 1: Add a local-ffprobe delegate and wire the classifier in `ProcessAlbumAsync`**

Replace the whole body of `ProcessAlbumAsync` from the `available` list onward:

```csharp
		List<PristineApiTrack> available =
		[
			.. tracks.Where(t => t.Available).OrderBy(t => t.Position),
		];
		Telemetry.Debug(
			"Pristine.ApiPoll.Expected code={Code} count={Count}",
			code,
			available.Count
		);
		if (available.Count == 0)
		{
			Telemetry.Warn("Pristine.ApiPoll.NoTracksAvailable code={Code}", code);
			return new PristineAlbumResult
			{
				Code = code,
				Title = albumTitle,
				OutPath = albumOut,
				Expected = 0,
				Downloaded = 0,
				Resumed = 0,
			};
		}

		Telemetry.Info("Downloading: {Title:l}", album.Title);

		PristineAlbumClassifier.AlbumPlan plan = PristineAlbumClassifier.Classify(
			available,
			albumOut,
			dest => ProbeLocalSync(dest)
		);

		if (plan.Kind is PristineAlbumClassifier.AlbumPlanKind.AlreadyComplete)
		{
			Telemetry.Info(
				"Already complete — {Count}/{Count} tracks, skipping",
				available.Count,
				available.Count
			);
			return new PristineAlbumResult
			{
				Code = code,
				Title = albumTitle,
				OutPath = albumOut,
				Expected = available.Count,
				Downloaded = available.Count,
				Resumed = available.Count,
			};
		}

		List<PristineAlbumClassifier.TrackPlan> toFetch =
		[
			.. plan.Tracks.Where(t => t.NeedsDownload),
		];
		PristineAlbumClassifier.TrackPlan? firstToFetch = toFetch.Count > 0 ? toFetch[0] : null;
		(int Bits, int Rate)? sourceProbe = null;
		if (firstToFetch is not null)
		{
			ErrorOr<PristineApiListenSources> firstListenOr = await api.GetListenSourcesAsync(
				http,
				apiKey,
				firstToFetch.Track.Id,
				ct
			);
			if (firstListenOr.IsError || firstListenOr.Value.Flac is null)
			{
				Telemetry.Error(
					"Pristine.ApiPoll.LiveProbeFailed code={Code}: could not resolve a source URL for track {Track}",
					code,
					firstToFetch.Track.Position
				);
				return new PristineAlbumResult
				{
					Code = code,
					Title = albumTitle,
					OutPath = albumOut,
					Expected = available.Count,
					Downloaded = 0,
					Resumed = 0,
					AlbumLevelFailure = "live probe failed: no source URL for first track",
				};
			}

			var firstUrl = $"{BaseUrl}{firstListenOr.Value.Flac}";
			ErrorOr<(int Bits, int Rate)> probeOr = await ProbeRemoteAsync(firstUrl, ct);
			if (probeOr.IsError)
			{
				Telemetry.Error(
					"Pristine.ApiPoll.LiveProbeFailed code={Code}: {Error}",
					code,
					probeOr.FirstError.Description
				);
				return new PristineAlbumResult
				{
					Code = code,
					Title = albumTitle,
					OutPath = albumOut,
					Expected = available.Count,
					Downloaded = 0,
					Resumed = 0,
					AlbumLevelFailure = $"live probe failed: {probeOr.FirstError.Description}",
				};
			}

			sourceProbe = probeOr.Value;
			Telemetry.Info("Source: {Bits}-bit/{Rate}Hz", sourceProbe.Value.Bits, sourceProbe.Value.Rate);
		}

		List<string> results = [];
		List<string> resumed = [];
		List<TrackFailure> failedTracks = [];

		using SemaphoreSlim gate = new(MaxConcurrent);
		List<Task> pending = [];
		foreach (PristineApiTrack track in available)
		{
			PristineAlbumClassifier.TrackPlan trackPlan = plan.Tracks.First(t =>
				t.Track.Position == track.Position
			);
			if (trackPlan.ExistsLocally && trackPlan.NeedsTranscode is false)
			{
				Telemetry.Info(
					"  [{Num:00}] {Title:l} — already present, skipping",
					track.Position,
					track.Title
				);
				lock (results)
				{
					results.Add(trackPlan.Dest);
				}
				lock (resumed)
				{
					resumed.Add(trackPlan.Dest);
				}
				continue;
			}

			if (trackPlan.NeedsDownload is false)
				continue;

			await gate.WaitAsync(ct);
			PristineApiTrack capturedTrack = track;
			string capturedDest = trackPlan.Dest;
			Task task = Task.Run(
				async () =>
				{
					try
					{
						await DownloadTrackAsync(
							http,
							apiKey,
							code,
							capturedTrack,
							capturedDest,
							results,
							failedTracks,
							ct
						);
					}
					finally
					{
						gate.Release();
					}
				},
				CancellationToken.None
			);
			pending.Add(task);
		}

		try
		{
			await Task.WhenAll(pending);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.PendingDrainFailed code={Code}: {Error}",
				code,
				ex.Message
			);
		}

		Telemetry.Debug(
			"Pristine.ApiPoll.Done code={Code} downloaded={Downloaded} resumed={Resumed} failed={Failed} expected={Expected}",
			code,
			results.Count,
			resumed.Count,
			failedTracks.Count,
			available.Count
		);
		return new PristineAlbumResult
		{
			Code = code,
			Title = albumTitle,
			OutPath = albumOut,
			Expected = available.Count,
			Downloaded = results.Count,
			Resumed = resumed.Count,
			FailedTracks = failedTracks,
		};
	}
```

Note the download loop above no longer decides resume-vs-fresh per track by probing `File.Exists` inline — that decision already happened once, up front, in `PristineAlbumClassifier.Classify`. `DownloadTrackAsync` (Step 2 below) is now purely "fetch this specific missing track, with corrupt-retry" — it no longer contains any resume/stale-orphan logic at all, since the classifier already resolved that.

- [ ] **Step 2: Rewrite `DownloadTrackAsync` as a pure fetch-with-retry (no resume logic)**

Replace the entire method:

```csharp
	private async Task DownloadTrackAsync(
		HttpClient http,
		string apiKey,
		string code,
		PristineApiTrack track,
		string dest,
		List<string> results,
		List<TrackFailure> failedTracks,
		CancellationToken ct
	)
	{
		var safeTitle = PristineText.SanitizePathComponent(
			PristineText.NormalizeTrackTitle(track.Title)
		);

		ErrorOr<PristineApiListenSources> listenOr = await api.GetListenSourcesAsync(
			http,
			apiKey,
			track.Id,
			ct
		);
		if (listenOr.IsError)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.ListenFailed code={Code} track={Track} err={Err}",
				code,
				track.Position,
				listenOr.FirstError.Description
			);
			lock (failedTracks)
			{
				failedTracks.Add(
					new TrackFailure(track.Position, safeTitle, TrackFailureReason.DownloadCorruptExhausted)
				);
			}
			return;
		}

		var flacRel = listenOr.Value.Flac;
		if (flacRel is null)
		{
			Telemetry.Warn(
				"Pristine.ApiPoll.NoFlacSource code={Code} track={Track}",
				code,
				track.Position
			);
			lock (failedTracks)
			{
				failedTracks.Add(
					new TrackFailure(track.Position, safeTitle, TrackFailureReason.DownloadCorruptExhausted)
				);
			}
			return;
		}

		var flacUrl = $"{BaseUrl}{flacRel}";
		const int maxAttempts = 3;
		for (var attempt = 1; attempt <= maxAttempts; attempt++)
		{
			Telemetry.Info("  Downloading: {Title:l}", safeTitle);
			ErrorOr<Success> dlResult = await downloader.DownloadAsync(flacUrl, dest, http, ct);
			if (dlResult.IsError)
			{
				Telemetry.Warn(
					"Pristine.ApiPoll.DownloadFailed code={Code} track={Track} attempt={Attempt}/{Max} dest={Dest}",
					code,
					track.Position,
					attempt,
					maxAttempts,
					Path.GetFileName(dest)
				);
				continue;
			}

			if (await PristineVerification.VerifyAndKeepAsync(verifier, dest, code, track.Position, ct))
			{
				Telemetry.Info("  [{Num:00}] {Title:l} — kept", track.Position, safeTitle);
				lock (results)
				{
					results.Add(dest);
				}
				return;
			}

			Telemetry.Warn(
				"Pristine.ApiPoll.CorruptDownload code={Code} track={Track} attempt={Attempt}/{Max}",
				code,
				track.Position,
				attempt,
				maxAttempts
			);
		}

		Telemetry.Error(
			"Pristine.ApiPoll.DownloadGaveUp code={Code} track={Track} attempts={Max} — {Title:l}",
			code,
			track.Position,
			maxAttempts,
			safeTitle
		);
		lock (failedTracks)
		{
			failedTracks.Add(
				new TrackFailure(track.Position, safeTitle, TrackFailureReason.DownloadCorruptExhausted)
			);
		}
	}
```

Per-track lines are now indented two spaces including `Downloading:` (matches the design spec's disambiguation rule — album line unindented, track lines indented).

- [ ] **Step 3: Add the two new private helpers `ProbeLocalSync` and `ProbeRemoteAsync`**

Add near the bottom of the class, above the closing brace:

```csharp
	private static (int Bits, int Rate)? ProbeLocalSync(string path)
	{
		ErrorOr<PristineProbeResult> result = new PristineAudioVerifier()
			.VerifyAsync(path, "local-probe", 0, CancellationToken.None)
			.GetAwaiter()
			.GetResult();
		return result.IsError || result.Value.IsValid is false
			? null
			: (result.Value.Bits, result.Value.SampleRate);
	}

	private static async Task<ErrorOr<(int Bits, int Rate)>> ProbeRemoteAsync(
		string url,
		CancellationToken ct
	)
	{
		ErrorOr<ProcessResult> result = await new ProcessRunner().RunAsync(
			"ffprobe",
			["-v", "quiet", "-print_format", "json", "-show_streams", url],
			ct
		);
		if (result.IsError)
			return result.Errors;
		if (result.Value.ExitCode != 0)
			return Errors.Pristine.ApiRequestFailed(
				$"ffprobe on remote URL exited {result.Value.ExitCode}"
			);

		using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(
			result.Value.Stdout
		);
		if (
			doc.RootElement.TryGetProperty("streams", out System.Text.Json.JsonElement streamsEl)
				is false
			|| streamsEl.GetArrayLength() == 0
		)
			return Errors.Pristine.ApiRequestFailed("ffprobe returned no streams for remote URL");

		System.Text.Json.JsonElement first = streamsEl[0];
		var bits = 0;
		if (
			first.TryGetProperty("bits_per_raw_sample", out System.Text.Json.JsonElement bitsEl)
			&& bitsEl.GetString() is string bitsStr
			&& int.TryParse(bitsStr, out var parsedBits)
		)
			bits = parsedBits;
		else if (
			first.TryGetProperty("bits_per_sample", out System.Text.Json.JsonElement bpsEl)
			&& bpsEl.ValueKind is System.Text.Json.JsonValueKind.Number
		)
			bits = bpsEl.GetInt32();
		var rate = 0;
		if (
			first.TryGetProperty("sample_rate", out System.Text.Json.JsonElement srEl)
			&& srEl.GetString() is string srStr
			&& int.TryParse(srStr, out var parsedSr)
		)
			rate = parsedSr;

		return (bits, rate);
	}
```

Add `using Services.Audio;` and `using System.Text.Json;` to the top of the file (the latter can replace the fully-qualified `System.Text.Json.*` references above with plain `JsonDocument`/`JsonElement`/`JsonValueKind` for readability — do that cleanup now rather than leaving the fully-qualified names, since this file's existing style uses plain `using` imports throughout).

**Known risk to validate on first live run:** `ProbeRemoteAsync` assumes ffprobe can read enough of Pristine's S3-presigned URLs over HTTP range requests to report `bits_per_raw_sample`/`sample_rate` without downloading the whole file. This was flagged as unverified in the design spec. If it fails in practice (some CDNs reject range requests or ffprobe needs the full file for FLAC's metadata block), the fallback is to range-GET the first ~1MB into a temp file and probe that instead — but don't build that fallback preemptively; confirm the failure first.

- [ ] **Step 4: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` Fix any compile errors from the `using` additions or leftover references to the old per-track resume logic before proceeding — this is the largest single change in the plan, review the diff carefully against Step 1-3 above rather than skimming.

- [ ] **Step 5: Format and commit**

```bash
dprint fmt .
git add src/Services/Pristine/PristineApiPollService.cs
git commit -m "refactor(pristine): rewire API path around local-state-first classifier

ProcessAlbumAsync now classifies the whole album's state up front
(PristineAlbumClassifier) before touching the network for anything beyond
the initial track-list resolve. A fully-resumed album never live-probes or
downloads at all. Live-probing only fires for the first track actually
being fetched. DownloadTrackAsync no longer contains resume/stale-orphan
logic (the classifier already resolved that) — it's now a pure
fetch-with-corrupt-retry (3 attempts) that records a named TrackFailure
instead of just decrementing a counter."
```

---

## Task 8: `PristineOrchestrator` — three-bucket final batch summary

**Files:**

- Modify: `src/Services/Pristine/PristineOrchestrator.cs` (the pre-existing error-fallback constructions in `DownloadAlbumAsync`, the per-album summary line, and the final `"Done — {Total} album(s) processed"` line in `DownloadAsync`)

**Interfaces:**

- Consumes: `PristineAlbumResult.FailedTracks`, `.AlbumLevelFailure` (Task 5)

- [ ] **Step 1: Set `AlbumLevelFailure` on every pre-existing error-fallback path in `DownloadAlbumAsync`**

`DownloadAlbumAsync` has four places that construct a placeholder `PristineAlbumResult { Title = "error", ... }` when the browser-fallback path fails (browser launch exception, null context, poll-result error, outer catch) — these predate this redesign and are NOT touched by Task 7 (which only changes API-path-internal logic in `ProcessAlbumAsync`, reached only after the album already resolved). Without this step, an album that fails to resolve via BOTH the API path and the browser fallback would have `AlbumLevelFailure` still null by default, and Task 8's classification below would wrongly bucket it as a clean Success. Add `AlbumLevelFailure` to each:

```csharp
Telemetry.Error("Pristine.Orchestrator.BrowserFailed: {Error}", ex.Message);
return (
	new PristineAlbumResult
	{
		Code = code,
		Title = "error",
		OutPath = dest,
		Expected = 0,
		Downloaded = 0,
		AlbumLevelFailure = $"browser launch failed: {ex.Message}",
	},
	ctx
);
```

```csharp
if (ctx is null)
	return (
		new PristineAlbumResult
		{
			Code = code,
			Title = "error",
			OutPath = dest,
			Expected = 0,
			Downloaded = 0,
			AlbumLevelFailure = "browser context unavailable (not logged in?)",
		},
		null
	);
```

```csharp
PristineAlbumResult r = pollResult.Match(
	value => value,
	errors => new PristineAlbumResult
	{
		Code = code,
		Title = "error",
		OutPath = dest,
		Expected = 0,
		Downloaded = 0,
		AlbumLevelFailure = errors[0].Description,
	}
);
```

```csharp
Telemetry.Error(
	"Pristine.Orchestrator.AlbumFailed code={Code}: {Error}",
	code,
	ex.Message
);
return (
	new PristineAlbumResult
	{
		Code = code,
		Title = "error",
		OutPath = dest,
		Expected = 0,
		Downloaded = 0,
		AlbumLevelFailure = ex.Message,
	},
	ctx
);
```

- [ ] **Step 2: Update the per-album summary line to include track-level failure detail**

Replace:

```csharp
r = await TranscodeAlbumAsync(r, ct);
results.Add(r);
var freshDownloaded = Math.Max(0, r.Downloaded - r.Resumed);
var rejected = Math.Max(0, r.Expected - r.Downloaded);
Telemetry.Info(
	"[{Index}/{Total}] {Code:l} \"{Title:l}\" — {Fresh} fresh, {Resumed} resumed, {Rejected} rejected → {Downloaded}/{Expected} tracks → {Out:l}",
	results.Count,
	effective.Length,
	r.Code,
	r.Title,
	freshDownloaded,
	r.Resumed,
	rejected,
	r.Downloaded,
	r.Expected,
	r.OutPath
);
```

with:

```csharp
r = await TranscodeAlbumAsync(r, ct);
results.Add(r);
var freshDownloaded = Math.Max(0, r.Downloaded - r.Resumed);
Telemetry.Info(
	"[{Index}/{Total}] {Code:l} \"{Title:l}\" — {Fresh} fresh, {Resumed} resumed, {Failed} failed → {Downloaded}/{Expected} tracks → {Out:l}",
	results.Count,
	effective.Length,
	r.Code,
	r.Title,
	freshDownloaded,
	r.Resumed,
	r.FailedTracks.Count,
	r.Downloaded,
	r.Expected,
	r.OutPath
);
foreach (TrackFailure failure in r.FailedTracks)
	Telemetry.Warn(
		"  [{Num:00}] {Title:l} — {Reason:l}",
		failure.Position,
		failure.Title,
		failure.Reason == TrackFailureReason.DownloadCorruptExhausted
			? "download failed"
			: "transcode failed"
	);
```

- [ ] **Step 3: Replace the final one-line summary with the three-bucket tabulation**

Replace:

```csharp
Telemetry.Info("Done — {Total} album(s) processed", results.Count);
return results;
```

with:

```csharp
			List<PristineAlbumResult> succeeded =
			[
				.. results.Where(r => r.AlbumLevelFailure is null && r.FailedTracks.Count == 0),
			];
			List<PristineAlbumResult> partial =
			[
				.. results.Where(r => r.AlbumLevelFailure is null && r.FailedTracks.Count > 0),
			];
			List<PristineAlbumResult> failed =
			[
				.. results.Where(r => r.AlbumLevelFailure is not null),
			];

			Telemetry.Info(
				"Done — {Success} succeeded, {Partial} partial, {Failed} failed, out of {Total}",
				succeeded.Count,
				partial.Count,
				failed.Count,
				results.Count
			);
			if (succeeded.Count > 0)
				Telemetry.Info(
					"  Success: {Codes:l}",
					string.Join(", ", succeeded.Select(r => r.Code))
				);
			foreach (PristineAlbumResult r in partial)
				Telemetry.Warn(
					"  Partial: {Code:l} — {Failed}/{Expected} tracks failed ({Names})",
					r.Code,
					r.FailedTracks.Count,
					r.Expected,
					string.Join(", ", r.FailedTracks.Select(f => $"[{f.Position:00}] {f.Title}"))
				);
			foreach (PristineAlbumResult r in failed)
				Telemetry.Error(
					"  Failed: {Code:l} — {Reason:l}",
					r.Code,
					r.AlbumLevelFailure!
				);

			return results;
```

- [ ] **Step 4: Scorched-earth build**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Format and commit**

```bash
dprint fmt .
git add src/Services/Pristine/PristineOrchestrator.cs
git commit -m "feat(pristine): three-bucket final batch summary

Success/Partial/Failed, with track-level detail on anything not fully
clean — Partial names which specific tracks failed and why, Failed names
the album-level reason (code didn't resolve, or live probe failed before
any download started). Per the design spec's batch-summary requirement."
```

---

## Task 9: Full verification pass

**Files:** none (verification only)

- [ ] **Step 1: Scorched-earth build one more time, from a fully clean state**

```bash
rm -rf artifacts/bin artifacts/obj && dotnet clean && dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Static review checklist against the spec**

Walk `docs/superpowers/specs/2026-08-21-pristine-pipeline-redesign-design.md` section by section and confirm each is implemented:

- Per-album pipeline steps 1-4 (resolve → print → local-state-first → live-probe-only-when-needed → download-then-transcode strictly sequential) — Tasks 6, 7.
- Target matrix (12 rows) — Task 1, scratch-verified in Task 1 Step 2.
- Concurrency (downloads capped at 5, transcode sequential) — unchanged `MaxConcurrent`/sequential `foreach` in `FlacTranscodeService`, confirm neither Task 2 nor Task 7 accidentally introduced parallel transcoding.
- Logging ownership (no duplicate transcode lines, indentation convention) — Task 2 Step 3, Task 7 Step 2.
- Error/retry/throw matrix (all five rows) — Tasks 2, 3, 7.
- Final batch summary (three buckets, track-level detail) — Task 8.

- [ ] **Step 3: Flag what this plan does NOT verify**

This plan's build-verify steps prove the code compiles and the pure-logic pieces (Task 1, Task 6) match the spec's matrix by scratch-script inspection. It does **not** prove `ProbeRemoteAsync` (Task 7 Step 3) actually works against Pristine's real S3-presigned URLs — that's a live-network integration point that needs a real run against the real service, which this plan does not execute automatically (running live downloads against the user's paid account is not something to do without them watching). Report this gap explicitly rather than claiming end-to-end success: the next real Pristine batch run is the actual test of the live-probe path, the corrupt-retry paths, and the final summary's real-world shape.

- [ ] **Step 4: No commit for this task** — it's verification-only, nothing to add.
