> **CORRECTION (2026-08-11):** The UTF-8/ACP root cause claimed here was REJECTED by probe run #4 (all-PASS with ACP=65001). Verified root cause: ID3 chunks in DFF + Saracon retry self-restart loop, compounded by non-interactive session GUI failure. Evidence: docs/superpowers/audits/sacd-probe-journal.md. Do not restate the UTF-8 hypothesis as settled.


# SACD Saracon Death-Loop Verification and Fix Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace unverified Saracon failure claims with an evidence-gated probe, preserve the RED toolchain, land only proven defensive fixes, and validate the real Disc 10 pipeline.

**Architecture:** Keep `sacd_extract -> Saracon -> SoX -> tag`. Add one standalone `tools/SacdProbe` console project using the real DFF and real service code. The probe first checks registry/OLE preconditions, then classifies failures by signature instead of treating every error as an expected pass. Production changes remain limited to Saracon validation, DFF metadata-walk hardening, removal of the invalid SoX DSD replacement, and the smallest startup change needed to run audio without unrelated Google OAuth.

**Tech Stack:** .NET 11.0, C#, `ErrorOr`, `ProcessRunner`, `saracon`, `sox`, `sacd_extract`, ATL.NET; no new NuGet packages and no test framework.

## Global Constraints

- Preserve RED guide toolchain: `sacd_extract -> DFF -> Saracon -> SoX -> tag`.
- Do not call SoX a DSD-to-PCM replacement; SoX cannot perform this conversion.
- Do not claim UTF-8/ACP, filename, metadata, or race hypotheses as root cause without a controlled journal result.
- Clear registry/OLE execution precondition before interpreting Saracon output.
- Keep confidence tags (`HIGH`, `MEDIUM`, `LOW`) on every journal finding; never upgrade source confidence.
- Keep phase-1 probe work outside `src/Services/Audio`.
- No new NuGet packages, test frameworks, PowerShell scripts, or production fixture factories.
- Keep probe fixtures and copied media under `C:\Temp\saracon-probe\`, never in the repository.
- Resolve `saracon`, `sox`, and `sacd_extract` from `PATH`; do not bundle binaries or add path environment variables.
- Build after every source edit with `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`.
- Stage only named SACD files; never use `git add -A` in dirty worktree.
- Preserve unrelated existing changes in `C:\Users\Lance\Dev\Toolbox`.
- One class per file; no `Helpers.cs`, `Constants.cs`, explanatory inline comments, warning suppressions, or compatibility shims.
- Use standalone `Main()` checks through `tools/SacdProbe`; do not add a test project.

---

## Evidence Baseline

Evidence read before writing this plan:

| Evidence | Status | Planning consequence |
|---|---|---|
| Original user report | File-lock error: `Disc 10-d2p.wav` was in use | Do not relabel it as proven charset failure. |
| Synthetic probe, ACP=1252 | 12/12 pass, 265136 bytes each | Brackets, COMT bytes, and ID3 variants did not fail there. |
| Synthetic probe, ACP=65001 | Baseline, COMT, ID3, and bracket cases passed, much slower | UTF-8 hypothesis remains unconfirmed locally. |
| Agent-context Saracon runs | Registry/OLE initialization failed under both ACP values | Add canary and compare agent versus interactive session before A/B. |
| Worktree commit `ad8abf4` | `HasId3Chunk` changed `Seek(12)` to `Seek(16)` | Port exact parser fix into main; main file is currently untracked. |
| Worktree commit `e14e92e` | DFF walk failures are logged and rethrown with EOF bounds | Port exact hardening into main. |
| Worktree commit `51193e3` | Saracon output-size guard exists | Merge guard into main retry implementation, not blind cherry-pick. |
| Main tree | Dirty; `DsdConvertService` injects unregistered `SoxDsdService` | Rewire facade to existing `SaraconService`; delete invalid replacement. |
| Main startup | `Program` always calls `AddGoogleServicesAsync()` | Make audio command startup independent of Google OAuth before final gate. |

No conclusion in this plan treats the UTF-8 theory as confirmed. The latest evidence specifically supersedes the stale “root cause confirmed” wording in earlier proposal documents.

## File Map

### Create

- `tools/SacdProbe/SacdProbe.csproj`: standalone executable referencing `Audio` and `Core`.
- `tools/SacdProbe/Program.cs`: prints identity, session, ACP, then starts probe.
- `tools/SacdProbe/ProbeRunner.cs`: precondition canary, real-media matrix, failure signatures, journal rows, exit codes.
- `tools/SacdProbe/RealDffFixture.cs`: locates copied Disc 10 DFF and computes expected PCM bytes from its `DSD ` chunk.
- `.superpowers/audit/sacd-probe-journal.md`: append-only run and finding record.

### Modify

- `Toolbox.slnx`: include `tools/SacdProbe/SacdProbe.csproj`.
- `src/Services/Audio/DffMetadataStripper.cs`: seek past the complete DSDIFF header, bound chunk walks, surface exceptions.
- `src/Services/Audio/SaraconService.cs`: retain existing retry flow; add output-size validation and conditional staging only when A/B proves path dependence.
- `src/Services/Audio/DsdConvertService.cs`: inject `SaraconService`, not `SoxDsdService`; route all DSD conversion calls through Saracon.
- `src/App/Program.cs`: skip Google OAuth registration for `audio` command only.
- `src/Services/Audio/SoxService.cs`: modify only if a measured RED-guide mismatch affects current pipeline; convert-once-then-split may make click trim unnecessary.

### Delete

- `src/Services/Audio/SoxDsdService.cs`: invalid DSD conversion replacement.
- Prior-session noise files listed in Task 11, only when present and only after evidence is recorded.

### Do not touch

- YouTube state, Azure changes, dashboard files, `.omo` goals, existing historical `SACD errors.md`, and unrelated dirty files.

## Dependency Table

| Task | Description | Predecessors | Validation |
|---|---|---|---|
| 1 | Freeze scope in dirty worktree | None | Intended file list recorded |
| 2 | Add corrected real-media probe | 1 | Solution build; probe project resolves |
| 3 | Clear registry/OLE precondition | 2 | Canary exits past `RegistryOleInit` |
| 4 | Run controlled ACP/session A/B | 3 | Journal has classified runs |
| 5 | Harden DFF metadata walker | 1, 2 | Build; raw/stripped probe remains valid |
| 6 | Add Saracon output-size guard | 1, 2 | Build; expected-size guard present |
| 7 | Remove invalid SoX DSD service | 1 | Build; zero references remain |
| 8 | Verify SoX behavior against RED | 1 | Recorded match or separate measured delta |
| 9 | Add filename staging only if proven | 4, 6 | Conditional; skipped when unsupported |
| 10 | Remove Google OAuth audio blocker | 1 | Audio command starts without Google auth |
| 11 | Run real Disc 10 pipeline | 3, 4, 5, 6, 7, 8, 9, 10 | Full output, track count, tags |
| 12 | Prune noise and close journal | 11 | Named noise absent; confidence tags intact |
| 13 | Final targeted verification and handoff | 12 | Build, status, diff, commits reviewed |

Critical path: `2 -> 3 -> 4 -> 11 -> 12 -> 13`; Tasks 5, 6, 7, 8, and 10 are parallel prerequisites for Task 11, while Task 9 joins only when its evidence gate passes.

---

### Task 1: Freeze Dirty Worktree Scope

**Files:**
- Read only: `C:\Users\Lance\Dev\Toolbox` Git state and SACD source files.

**Interfaces:**
- Produces: exact SACD file allowlist for later staging.
- Does not modify or revert existing work.

- [ ] **Step 1: Record branch and dirty paths**

Run:

```powershell
git status --short --branch
git diff --name-only -- src/App/Program.cs src/Services/Audio
git ls-files --others --exclude-standard -- src/Services/Audio tools
```

Expected: dirty paths are visible; unrelated YouTube, Azure, dashboard, `.omo`, and state changes remain excluded from this work.

- [ ] **Step 2: Verify source differences before selecting merge targets**

Run:

```powershell
git diff -- src/Services/Audio/DsdConvertService.cs src/Services/Audio/SaraconService.cs src/Services/Audio/SoxService.cs
rg -n "SoxDsdService|SaraconService|DffMetadataStripper|RunConversionWithRetryAsync" src/Services/Audio src/App
```

Expected: current `DsdConvertService` references `SoxDsdService`; `AudioSetup` registers `SaraconService`; main `DffMetadataStripper.cs` is untracked; main `SaraconService.cs` has retry changes.

- [ ] **Step 3: Record worktree fix provenance**

Run from `C:\Users\Lance\Dev\Toolbox-sacd-repro`:

```powershell
git show --stat --oneline ad8abf4
git show --stat --oneline e14e92e
git show --stat --oneline 51193e3
```

Expected: only the three named audio fixes are selected as source material. Do not merge the entire worktree branch.

- [ ] **Step 4: Mark scope checkpoint**

Acceptance: later commits may stage only these paths:

```text
Toolbox.slnx
tools/SacdProbe/SacdProbe.csproj
tools/SacdProbe/Program.cs
tools/SacdProbe/ProbeRunner.cs
tools/SacdProbe/RealDffFixture.cs
.superpowers/audit/sacd-probe-journal.md
src/Services/Audio/DffMetadataStripper.cs
src/Services/Audio/SaraconService.cs
src/Services/Audio/DsdConvertService.cs
src/Services/Audio/SoxDsdService.cs
src/App/Program.cs
src/Services/Audio/SoxService.cs
```

No commit is made for this read-only task.

---

### Task 2: Add Corrected Real-Media Probe

**Files:**
- Create: `tools/SacdProbe/SacdProbe.csproj`
- Create: `tools/SacdProbe/Program.cs`
- Create: `tools/SacdProbe/ProbeRunner.cs`
- Create: `tools/SacdProbe/RealDffFixture.cs`
- Create: `.superpowers/audit/sacd-probe-journal.md`
- Modify: `Toolbox.slnx`

**Interfaces:**
- Consumes: `SaraconService(ProcessRunner, string)`, `DffMetadataStripper.StripId3TagsAsync`, real copied DFF at `C:\Temp\t.dff`.
- Produces: `SacdProbe` executable; exit `0` for classified expected/pass results, `1` for hypothesis failure, `2` for registry/OLE precondition failure, `3` for missing real DFF.

- [ ] **Step 1: Create the project and add it to the solution**

Create `tools/SacdProbe/SacdProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<RootNamespace>SacdProbe</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\src\Services\Audio\Audio.csproj" />
		<ProjectReference Include="..\..\src\Core\Core.csproj" />
	</ItemGroup>
</Project>
```

Add one project entry inside `Toolbox.slnx`:

```xml
<Project Path="tools\SacdProbe\SacdProbe.csproj" />
```

- [ ] **Step 2: Create real-media fixture source**

`RealDffFixture.cs` must expose these exact members:

```csharp
internal static class RealDffFixture
{
	public const string Path = @"C:\Temp\t.dff";
	public static bool Exists();
	public static long ExpectedPcmBytes();
}
```

`ExpectedPcmBytes()` must seek to byte `16`, walk top-level DSDIFF chunks as `4-byte ID + 8-byte big-endian size + even padding`, locate `DSD `, and calculate 88.2 kHz, 24-bit, stereo PCM bytes from the DSD payload. Return `-1` when the file or DSD chunk is invalid. Never hardcode a 2.3 GB result.

Required formula for DSD64 stereo:

```csharp
var durationSeconds = dsdBytes / (2822400.0 / 8.0 * 2);
return (long)(durationSeconds * 88200 * 2 * 3);
```

- [ ] **Step 3: Create probe entry point**

`Program.cs` must print identity, session ID, and ACP before running the matrix:

```csharp
public static int Main()
{
	Console.WriteLine($"Identity: {WindowsIdentity.GetCurrent().Name}");
	Console.WriteLine($"Session: {Environment.ProcessId} / {Process.GetCurrentProcess().SessionId}");
	Console.WriteLine($"ACP: {ReadAcp()}");
	return ProbeRunner.RunAll();
}
```

On non-Windows hosts, print `ACP: unavailable` and return `3` before attempting Saracon. On Windows, read `HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage` value `ACP` with `Registry.GetValue`.

- [ ] **Step 4: Implement signature-matched probe runner**

Define signatures exactly:

```csharp
private enum FailureSignature
{
	None,
	RegistryOleInit,
	CharsetEncoding,
	Truncation,
	ZeroBytes,
	Other,
}
```

Classify registry/OLE text before charset text:

```csharp
private static FailureSignature Classify(string text) =>
	text.Contains("Can't open registry key", StringComparison.OrdinalIgnoreCase)
	|| text.Contains("Cannot initialize OLE", StringComparison.OrdinalIgnoreCase)
	|| text.Contains("wxIdleWakeUpModule", StringComparison.OrdinalIgnoreCase)
		? FailureSignature.RegistryOleInit
	: text.Contains("Unknown encoding", StringComparison.OrdinalIgnoreCase)
		|| text.Contains("Cannot convert from the charset", StringComparison.OrdinalIgnoreCase)
		? FailureSignature.CharsetEncoding
		: FailureSignature.Other;
```

Run a raw/headless baseline canary first. Abort with exit `2` on `RegistryOleInit`; do not classify that result as a successful hypothesis reproduction. Then run exactly four variants against the same copied real DFF: raw/headless, stripped/headless, raw/visible, stripped/visible. Measure exit code, elapsed time, output bytes, signature, and a bounded stderr/stdout note.

Use verdict rules:

```csharp
var verdict = actual == FailureSignature.None
	? "PASS"
	: declared != FailureSignature.None && actual == declared
		? $"FAIL-expected({actual})"
		: $"FAIL-unexpected({actual})";
```

`FAIL-unexpected` sets exit `1`. Do not use blanket `ErrorOr.IsError => FAIL-expected` logic.

- [ ] **Step 5: Add append-only journal**

Create `.superpowers/audit/sacd-probe-journal.md`:

```markdown
# SACD Probe Journal

## Runs

| timestamp | variant | exit | elapsed | out-bytes | verdict | note |
|---|---|---:|---:|---:|---|---|

## Findings
```

Insert each new run immediately after the `## Runs` table separator. Escape `|`, CR, and LF in notes. Findings must state source and confidence, for example: `Confidence: HIGH (measured; ACP=65001 run passed baseline and bracket cases).`

- [ ] **Step 6: Copy real input outside repository**

Run:

```powershell
New-Item -ItemType Directory -Force -Path C:\Temp\saracon-probe | Out-Null
Copy-Item "C:\Users\Lance\Disc 10\Disc 10.dff" C:\Temp\t.dff
```

Expected: `C:\Temp\t.dff` exists; no DFF is committed.

- [ ] **Step 7: Build before first run**

Run:

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
```

Expected: exit `0`, including `SacdProbe`; no new package restore or warnings-as-errors failure.

- [ ] **Step 8: Commit probe only**

```powershell
git add Toolbox.slnx tools/SacdProbe .superpowers/audit/sacd-probe-journal.md
git commit -m "feat(audio): add evidence-gated SACD probe"
```

Expected: commit contains only probe files and solution entry.

---

### Task 3: Clear Registry/OLE Preconditions

**Files:**
- Modify: `.superpowers/audit/sacd-probe-journal.md` only for measured findings.

**Interfaces:**
- Consumes: `SacdProbe` canary, Windows identity, interactive terminal.
- Produces: a decision between ACL remediation and interactive-session execution.

- [ ] **Step 1: Capture agent-context identity and session**

Run from agent context:

```powershell
[Security.Principal.WindowsIdentity]::GetCurrent().Name
query session
(Get-Process -Id $PID).SessionId
reg query "HKCU\Software\Weiss Engineering\Saracon"
```

Record exact output in journal `## Findings` with `Confidence: HIGH (measured)`.

- [ ] **Step 2: Capture same data in user interactive terminal**

Run the same commands in an active user terminal. Compare session ID, session state, and window station context. Do not infer from matching usernames alone.

- [ ] **Step 3: Apply only measured remediation**

If ACL access is the measured blocker, user performs the permission change from elevated interactive session; do not script destructive ACL changes in this plan. If session/Desktop mismatch is the blocker, route all Saracon probe and final pipeline commands to the user interactive terminal. If both contexts clear the canary, make no environment change.

- [ ] **Step 4: Run canary and verify exit code**

Run:

```powershell
dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe -c Release
```

Expected: probe proceeds past `RegistryOleInit`. Exit `2` remains a hard stop; do not continue to ACP A/B while it is present.

No commit is made for this execution-only task unless journal findings are added; if findings are added, stage only the journal.

---

### Task 4: Run Controlled ACP and Session A/B

**Files:**
- Modify: `.superpowers/audit/sacd-probe-journal.md`.

**Interfaces:**
- Consumes: cleared canary, same DFF, same Saracon binary, same execution context.
- Produces: evidence for or against ACP/UTF-8, filename, and metadata hypotheses.

- [ ] **Step 1: Run baseline arm with current ACP**

Run the probe in the same context that passed Task 3. Record ACP and all four variant rows. Do not label any result “root cause” yet.

- [ ] **Step 2: Disable UTF-8 beta only when the user chooses that A/B**

User action:

```text
Settings -> Time & Language -> Language -> Administrative language settings
-> Change system locale -> uncheck “Beta: Use Unicode UTF-8 for worldwide language support” -> reboot
```

Verify after reboot:

```powershell
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage" /v ACP
```

The value must differ from `65001` for the OFF arm. Record the value; do not assume `1252`.

- [ ] **Step 3: Repeat with one variable changed**

Run the same command, from the same session type, using the same `C:\Temp\t.dff` and same output settings. Record four additional rows.

- [ ] **Step 4: Apply decision table**

| Result | Action |
|---|---|
| Either arm exits `2` | Stop; environment still blocks inference. |
| Charset signature flips to pass with only ACP changed | Record `Confidence: HIGH (controlled A/B)`; locale is supported as trigger. |
| Both ACP arms pass | Record UTF-8 hypothesis `NOT CONFIRMED`; do not add locale workaround. |
| Bracketed real DFF fails while sanitized copy passes | Filename staging becomes eligible in Task 9. |
| Both real-path and sanitized-path runs fail | Skip staging; investigate content or environment, not filenames. |
| Output is below 50% expected with exit `0` | Record truncation; Task 6 guard is required defense. |

- [ ] **Step 5: Commit journal evidence**

```powershell
git add .superpowers/audit/sacd-probe-journal.md
git commit -m "docs(audio): record controlled Saracon environment A/B"
```

Expected: journal contains measured rows and confidence-tagged analysis; no unsupported root-cause sentence.

---

### Task 5: Harden DFF Metadata Walker

**Files:**
- Modify: `src/Services/Audio/DffMetadataStripper.cs`.

**Interfaces:**
- Consumes: DSDIFF files from `SaraconService`.
- Produces: `HasId3Chunk(string)` that begins at first top-level chunk; malformed walks surface failure; `StripId3TagsAsync` remains `ErrorOr<string>`.

- [ ] **Step 1: Write the intended parser behavior as probe assertions**

The real-media probe must exercise both raw and stripped paths. Required invariants:

```text
FRM8 header = 4-byte ID + 8-byte size + 4-byte form type = 16 bytes
first chunk begins at offset 16
chunk size is big-endian UInt64
odd chunk payloads consume one pad byte
skip never crosses EOF
walk failure is not converted to “no ID3”
```

- [ ] **Step 2: Apply the minimal parser fix**

Change the first-chunk seek from:

```csharp
stream.Seek(12, SeekOrigin.Begin);
```

to:

```csharp
stream.Seek(16, SeekOrigin.Begin);
```

After reading `chunkSize`, keep the walk bounded:

```csharp
var skip = checked((long)chunkSize);
if (skip <= 0 || stream.Position + skip > stream.Length)
	break;
if (skip % 2 != 0)
	skip++;
stream.Seek(skip, SeekOrigin.Current);
```

If the padding byte itself would cross EOF, stop before seeking beyond the stream. Preserve existing `ErrorOr` return shape.

- [ ] **Step 3: Surface walk exceptions**

Replace silent false return:

```csharp
catch (Exception ex)
{
	Telemetry.Warn("DffMetadataStripper.HasId3Chunk failed for {File}: {Error}", dffPath, ex.Message);
	return false;
}
```

with:

```csharp
catch (Exception ex)
{
	Telemetry.Error("DffMetadataStripper.HasId3Chunk failed for {File}: {Error}", dffPath, ex.Message);
	throw;
}
```

Use the existing telemetry API. If `Telemetry.Error` is unavailable in current source, use its existing error-level equivalent; do not suppress or swallow the exception.

- [ ] **Step 4: Build and run probe regression**

Run:

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe -c Release
```

Expected: build exit `0`; stripped arm either succeeds or reports a classified input error; no malformed walk is silently reported as “no ID3”.

- [ ] **Step 5: Commit one file**

```powershell
git add src/Services/Audio/DffMetadataStripper.cs
git commit -m "fix(audio): harden DSDIFF metadata chunk walk"
```

---

### Task 6: Add Saracon Output-Size Guard

**Files:**
- Modify: `src/Services/Audio/SaraconService.cs`.

**Interfaces:**
- Consumes: `FindSaraconOutput`, input DFF, target sample rate, bit depth.
- Produces: `ConversionFailed` when Saracon reports success but output is less than 50% of expected PCM bytes.

- [ ] **Step 1: Preserve dirty retry behavior**

Do not replace `RunConversionWithRetryAsync`. Keep existing timeout, charset retry, cleanup, and `FindSaraconOutput` behavior. Add guard after output discovery and before success return.

- [ ] **Step 2: Implement expected-size scan**

Add a private method with this contract:

```csharp
private static long EstimateExpectedPcmBytes(string dffPath, int sampleRate, int bitDepth)
```

It must:

1. Verify `FRM8`.
2. Seek to offset `16`.
3. Walk bounded even-padded top-level chunks.
4. Read channel count from `CHNL` when available; use `2` only as the existing DFF contract fallback.
5. Read `DSD ` payload bytes.
6. Convert DSD duration to PCM bytes using `sampleRate`, `channels`, and `bitDepth`.
7. Return `0` when metadata cannot produce a trustworthy estimate, so malformed metadata does not reject a valid output solely through a zero estimate.

Core calculation:

```csharp
var durationSeconds = dsdBytes / (2822400.0 / 8.0 * channels);
return (long)(durationSeconds * sampleRate * channels * (bitDepth / 8.0));
```

- [ ] **Step 3: Add guard after output discovery**

Insert after the existing null-output error:

```csharp
var outputBytes = new FileInfo(expectedOutput).Length;
var expectedBytes = EstimateExpectedPcmBytes(inputDff, sampleRate, bitDepth);
if (expectedBytes > 0 && outputBytes < expectedBytes / 2)
{
	Telemetry.Warn(
		"Saracon.OutputTooSmall output={Output} bytes={Bytes} expected={Expected}",
		Path.GetFileName(expectedOutput), outputBytes, expectedBytes
	);
	return Errors.Audio.ConversionFailed(
		inputDff,
		$"saracon output {Path.GetFileName(expectedOutput)} is {outputBytes} bytes; expected at least {expectedBytes / 2}"
	);
}
```

Do not accept file existence alone as success.

- [ ] **Step 4: Build and run regression**

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe -c Release
```

Expected: healthy real-media output passes; known truncated output cannot return success. If the probe is blocked by exit `2`, record that environment blocker rather than claiming a code regression.

- [ ] **Step 5: Commit one file**

```powershell
git add src/Services/Audio/SaraconService.cs
git commit -m "fix(audio): reject truncated Saracon output"
```

---

### Task 7: Remove Invalid SoX DSD Replacement

**Files:**
- Modify: `src/Services/Audio/DsdConvertService.cs`.
- Delete: `src/Services/Audio/SoxDsdService.cs`.

**Interfaces:**
- Consumes: registered `SaraconService`, existing `SoxService`, metadata service.
- Produces: `DsdConvertService` constructor `DsdConvertService(SaraconService saracon, SoxService sox, AudioMetadataService metadata)`; all DSD-to-PCM/FLAC calls route through Saracon.

- [ ] **Step 1: Rewire facade constructor**

Replace:

```csharp
public sealed class DsdConvertService(
	SoxDsdService soxDsd,
	SoxService sox,
	AudioMetadataService metadata
)
```

with:

```csharp
public sealed class DsdConvertService(
	SaraconService saracon,
	SoxService sox,
	AudioMetadataService metadata
)
```

- [ ] **Step 2: Route every DSD conversion call through Saracon**

Replace each `soxDsd` call in `CalculateGainAsync`, `ConvertAndSplitAsync`, and `ConvertFullDffAsync` with `saracon` calls. Required calls:

```csharp
await saracon.ConvertDsdToPcmAsync(...)
await saracon.ConvertDsdToFlacAsync(...)
```

Do not alter SoX split, stats, duration, or derive calls.

- [ ] **Step 3: Delete replacement service and verify references**

Delete `src/Services/Audio/SoxDsdService.cs`. Run:

```powershell
rg -n "SoxDsdService|soxDsd" src tools
```

Expected: no matches.

- [ ] **Step 4: Build and commit**

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
git add src/Services/Audio/DsdConvertService.cs src/Services/Audio/SoxDsdService.cs
git commit -m "fix(audio): route DSD conversion through Saracon"
```

Expected: build exit `0`; `AudioSetup` registration and facade constructor agree.

---

### Task 8: Verify SoX Operation Against RED Guide

**Files:**
- Read: `src/Services/Audio/SoxService.cs`.
- Read: `SACD.red.md` or retained RED guide copy from the approved worktree.
- Modify: `src/Services/Audio/SoxService.cs` only if measured current behavior violates the pipeline contract.

**Interfaces:**
- Consumes: current convert-once-then-split pipeline.
- Produces: recorded decision: current `trim start [duration]` is correct for PCM-domain cue splitting, or one isolated SoX command change with direct validation.

- [ ] **Step 1: Compare actual split arguments with current pipeline design**

Current `SplitTrackAsync` emits:

```text
sox <master-pcm> <track-flac> trim <start-seconds> [<duration-seconds>]
```

RED’s click-removal command is:

```text
sox <in.flac> <out.flac> trim 0.0065 reverse silence 1 0 0% trim 0.0065 reverse pad 0.0065 0.2
```

Because this pipeline converts one Edit Master to PCM and splits from the PCM cue, do not add click-removal operations merely to match a command that applies to separately converted tracks.

- [ ] **Step 2: Record result in journal**

Add a confidence-tagged finding stating whether the current architecture makes RED click trim unnecessary. If a real output shows clicks, create a separate measured fix task; do not hide it inside unrelated Saracon changes.

- [ ] **Step 3: Commit only a needed code change**

If no mismatch is measured, make no source edit and do not create a commit. If a mismatch is measured, build and run a single-track check before committing only `SoxService.cs`.

---

### Task 9: Conditional Saracon Filename Staging

**Files:**
- Modify: `src/Services/Audio/SaraconService.cs` only when Task 4 proves filename dependence.

**Interfaces:**
- Consumes: Task 4 path A/B result.
- Produces: same Saracon conversion API; optional sanitized input copy removed in `finally`.

- [ ] **Step 1: Apply gate before editing**

Add staging only when a real-media comparison shows:

```text
original path with brackets/spaces/non-ASCII = failure or truncation
sanitized path C:\Temp\t.dff = full-size success
same ACP and same Saracon execution context
```

If synthetic bracket cases pass or both real paths fail identically, mark this task `SKIPPED: filename trigger not demonstrated` in the journal. No staging code is added.

- [ ] **Step 2: Implement smallest scoped staging path when gate passes**

Use one temporary copy per conversion:

```csharp
var effectiveInput = inputDff;
var stagedPath = (string?)null;
try
{
	if (NeedsSanitizedPath(inputDff))
	{
		var stagingDir = Path.Combine(Path.GetTempPath(), "saracon_staging");
		Directory.CreateDirectory(stagingDir);
		stagedPath = Path.Combine(stagingDir, $"{Guid.NewGuid():N}.dff");
		File.Copy(inputDff, stagedPath);
		effectiveInput = stagedPath;
	}

	// Existing retry loop runs with effectiveInput.
}
finally
{
	if (stagedPath is not null && File.Exists(stagedPath))
		File.Delete(stagedPath);
}
```

`NeedsSanitizedPath` checks only non-ASCII characters, `[` and `]`; spaces alone are not treated as a failure without evidence. Keep output naming based on original user input where existing callers depend on it.

- [ ] **Step 3: Build and run same A/B regression**

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe -c Release
```

Expected: staged conversion is full-size; no staged file remains after success or failure.

- [ ] **Step 4: Commit only conditional change**

```powershell
git add src/Services/Audio/SaraconService.cs
git commit -m "fix(audio): stage Saracon input with proven unsafe path"
```

Do not create this commit when the evidence gate is false.

---

### Task 10: Remove Google OAuth Blocker From Audio Startup

**Files:**
- Modify: `src/App/Program.cs`.

**Interfaces:**
- Consumes: command arguments.
- Produces: audio commands start without calling `AddGoogleServicesAsync`; non-audio commands retain current Google registration.

- [ ] **Step 1: Add command gate before service registration**

Use the first command token, not substring matching over file paths:

```csharp
var isAudioCommand = args.Length > 0
	&& args[0].Equals("audio", StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Guard only Google registration**

Replace unconditional registration:

```csharp
await services.AddGoogleServicesAsync();
```

with:

```csharp
if (!isAudioCommand)
	await services.AddGoogleServicesAsync();
```

Keep Azure, Last.fm, and Audio registrations unchanged unless build evidence identifies a separate startup dependency. Do not remove Google command registration; only skip its eager OAuth setup for audio execution.

- [ ] **Step 3: Build and smoke-check command startup**

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- audio --help
```

Expected: audio help starts without Google OAuth listener or `HttpListenerException`. A non-audio command must still use existing Google registration; do not run a network sync as part of this plan.

- [ ] **Step 4: Commit one file**

```powershell
git add src/App/Program.cs
git commit -m "fix(audio): skip unrelated Google OAuth startup"
```

---

### Task 11: Run Real Disc 10 Pipeline Gate

**Files:**
- Modify: `.superpowers/audit/sacd-probe-journal.md` with measured final result.
- Generated outside repo: extraction and conversion output directories.

**Interfaces:**
- Consumes: cleared Saracon canary, fixed audio facade, optional proven staging, audio-only startup.
- Produces: complete SACD extraction/conversion evidence: Saracon output, SoX track split, metadata/tag results, and no file-lock loop.

- [ ] **Step 1: Verify prerequisites**

Run:

```powershell
where.exe saracon
where.exe sox
where.exe sacd_extract
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage" /v ACP
git status --short -- src/App/Program.cs src/Services/Audio tools .superpowers/audit/sacd-probe-journal.md
```

Expected: all binaries resolve; ACP value is recorded; no uncommitted unrelated file is staged.

- [ ] **Step 2: Run actual CLI against SACD input**

Use user’s real input directory:

```powershell
dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- audio sacd-convert "C:\Users\Lance\Downloads\Herbert von Karajan - Live in Berlin 1970-1979 - Berliner Philharmoniker, Herbert von Karajan (2026) [SACD]" --debug
```

Expected:

```text
Saracon exits 0
no charset or registry/OLE error
no “file is being used by another process” error
PCM output is full-size and passes guard
SoX creates one FLAC per CUE track
tagging completes or reports a measured per-file warning
intermediate master WAV is cleaned after splitting
```

- [ ] **Step 3: Validate outputs from filesystem, not log claims**

Count CUE tracks and FLACs with commands that do not mutate files:

```powershell
Get-ChildItem -Recurse -Filter *.cue <output-root> | Measure-Object
Get-ChildItem -Recurse -Filter *.flac <output-root> | Measure-Object
Get-ChildItem -Recurse -Filter *.wav <output-root> | Select-Object FullName,Length
```

Expected: FLAC count matches selected CUE track count; WAV size is nonzero and consistent with probe estimate; no stale locked output is used as success.

- [ ] **Step 4: Journal final gate**

Append a finding with exact measured output size, track counts, ACP/session, and errors. Use one of:

```text
Final gate PASS. Confidence: HIGH (measured real Disc 10 run). Saracon output=<bytes>; cue tracks=<n>; FLAC outputs=<n>; file-lock errors=0.
```

or:

```text
Final gate BLOCKED/FAIL. Confidence: HIGH (measured). Signature=<signature>; command=<command>; next action=<specific blocker resolution>.
```

Do not write “death loop resolved” unless all success checks pass.

---

### Task 12: Prune Prior-Session Noise and Close Journal

**Files:**
- Delete when present: `dff-inspect.csx`, `inspect-dff.ps1`, `debug-sacd-disc10.ps1`, `test-saracon-simple.ps1`, `extract-and-test.ps1`, `test-saracon-now.ps1`, `diagnose-saracon-complete.ps1`, `strip-dff-metadata.ps1`, `test-saracon-gui-popup.ps1`.
- Delete when present: `SACD-Saracon-Analysis-Summary.md`, `docs/SACD-SARACON-ISSUE.md`.
- Modify: `.superpowers/audit/sacd-probe-journal.md`.
- Keep: `tools/SacdProbe`, journal, approved specs/plans, historical `SACD errors.md`.

**Interfaces:**
- Consumes: successful or explicitly blocked Task 11 gate.
- Produces: focused repository and complete evidence journal.

- [ ] **Step 1: Confirm every deletion target before deleting**

Run:

```powershell
rg --files -g "dff-inspect.csx" -g "inspect-dff.ps1" -g "debug-sacd-disc10.ps1" -g "test-saracon-simple.ps1" -g "extract-and-test.ps1" -g "test-saracon-now.ps1" -g "diagnose-saracon-complete.ps1" -g "strip-dff-metadata.ps1" -g "test-saracon-gui-popup.ps1" -g "SACD-Saracon-Analysis-Summary.md" -g "SACD-SARACON-ISSUE.md"
```

Expected: only listed prior-session noise appears. Do not delete `SACD errors.md`.

- [ ] **Step 2: Delete only listed repository noise**

Use the file-edit/delete mechanism on each existing listed file. Do not use wildcard deletion and do not touch unrelated dirty paths.

- [ ] **Step 3: Remove temporary probe directories after journal close**

Delete only these known temporary paths after all output measurements are recorded:

```text
C:\Temp\saracon-probe
C:\Temp\saracon_test
C:\Temp\saracon_diagnostics
C:\Temp\saracon_popup_test
C:\Temp\check-file-size.ps1
C:\Temp\run-strip.ps1
```

- [ ] **Step 4: Append cleanup finding**

Use:

```text
Cleanup complete. Confidence: HIGH (filesystem check). Listed noise removed; probe source and journal retained; unrelated dirty paths untouched.
```

- [ ] **Step 5: Commit named cleanup paths**

```powershell
git add -u -- dff-inspect.csx inspect-dff.ps1 debug-sacd-disc10.ps1 test-saracon-simple.ps1 extract-and-test.ps1 test-saracon-now.ps1 diagnose-saracon-complete.ps1 strip-dff-metadata.ps1 test-saracon-gui-popup.ps1 SACD-Saracon-Analysis-Summary.md docs/SACD-SARACON-ISSUE.md
git commit -m "chore(audio): remove SACD debugging noise"
```

Expected: no unrelated file enters commit.

---

### Task 13: Final Targeted Verification and Handoff

**Files:**
- Read: all files changed by Tasks 2, 5, 6, 7, 9, 10, and 12.
- No new source edits unless a verification command identifies a task-caused defect.

**Interfaces:**
- Consumes: all task commits and journal evidence.
- Produces: verified handoff with known blockers explicitly named.

- [ ] **Step 1: Run final build**

```powershell
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
```

Expected: exit `0` with no warnings-as-errors failures.

- [ ] **Step 2: Run structural checks**

```powershell
rg -n "SoxDsdService|soxDsd" src tools
rg -n "stream\.Seek\(16|EstimateExpectedPcmBytes|OutputTooSmall|AddGoogleServicesAsync" src tools
git diff --check
```

Expected: first command returns no matches; second shows intended fixes; `git diff --check` returns no whitespace errors.

- [ ] **Step 3: Verify journal discipline**

```powershell
rg -n "## Findings|Confidence: (HIGH|MEDIUM|LOW)|FAIL-unexpected|RegistryOleInit" .superpowers/audit/sacd-probe-journal.md
```

Expected: every finding has confidence; any `RegistryOleInit` result is described as an environment blocker, never as a hypothesis pass.

- [ ] **Step 4: Inspect final SACD diff**

```powershell
git log --oneline --decorate -12
```

Expected: only intended SACD changes are in task commits; unrelated dirty files remain unmodified and uncommitted.

- [ ] **Step 5: Mark final state**

Use one exact outcome:

```text
READY: build passes; probe precondition cleared; controlled A/B classified; real Disc 10 gate passes; cleanup verified.
```

or:

```text
BLOCKED: build=<result>; probe=<result>; real gate=<result>; blocker=<specific measured reason>; no unsupported root-cause claim made.
```

No commit is required for this read-only handoff unless a journal correction from this task is needed.

---

## Commit Sequence

1. `feat(audio): add evidence-gated SACD probe`
2. `docs(audio): record controlled Saracon environment A/B`
3. `fix(audio): harden DSDIFF metadata chunk walk`
4. `fix(audio): reject truncated Saracon output`
5. `fix(audio): route DSD conversion through Saracon`
6. Optional: `fix(audio): stage Saracon input with proven unsafe path`
7. `fix(audio): skip unrelated Google OAuth startup`
8. `chore(audio): remove SACD debugging noise`

No commit contains unrelated dirty files. No commit is created for skipped conditional staging.

## Explicit Exclusions

- No SoX-based DSD-to-PCM replacement.
- No new Saracon replacement service.
- No production synthetic DFF fixture factory.
- No root-cause declaration based only on librarian research, ACP value, elapsed time, or a registry query.
- No automatic ACL modification or reboot script.
- No retries added beyond existing retry behavior.
- No broad Google startup refactor; audio command gets one narrow registration gate.
- No forced RED click-trim command when current PCM-domain split architecture already removes that per-track conversion artifact.
- No cleanup of unrelated YouTube, Azure, dashboard, state, `.omo`, or pre-existing source changes.

## Self-Review

- Spec coverage: precondition, ACP/session A/B, filename gate, DFF walker, Saracon size guard, invalid service deletion, SoX verification, OAuth blocker, real-media gate, cleanup, and final verification each have a task.
- Placeholder scan: no `TBD`, `TODO`, or vague “add appropriate handling” steps; conditional work has explicit evidence and skip criteria.
- Type consistency: `DsdConvertService` consumes `SaraconService`; `SaraconService` keeps `ConvertDsdToPcmAsync` and `ConvertDsdToFlacAsync`; probe consumes `SaraconService(ProcessRunner, string)` and `DffMetadataStripper.StripId3TagsAsync`.
- Evidence correction: stale “UTF-8 root cause confirmed” language is rejected because ACP=65001 synthetic runs passed and registry/OLE failures blocked earlier runs.
