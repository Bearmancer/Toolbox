# Delete SACD Probe Harness and Repair Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove obsolete one-off SACD regression harness code, preserve production DSD probing, and make service-file telemetry obey the configured `--debug`/`--verbose` level with reliable service routing.

**Architecture:** Delete the isolated `SacdProbeRunner` matrix, its wrapper, fixture, and DI registration. Keep `DsdConvertService.ProbeDsdAsync`, which is production conversion validation and remains called by pipeline and CLI paths. Change only the shared Serilog service sink level gate and add the missing Audio scope at the SACD pipeline command boundary; existing YouTube scope remains at its command boundary.

**Tech Stack:** .NET 11 preview, C#, Serilog, Spectre.Console.Cli, ErrorOr, standalone build verification only.

## Global Constraints

- No new NuGet dependencies.
- No test NuGet packages; verification uses `dotnet build` and runnable CLI checks.
- Preserve `DsdConvertService.ProbeDsdAsync` and all production callers.
- Delete the one-off matrix harness; preserve journal/spec documentation as historical evidence.
- Service files must use the same configured `LoggingLevelSwitch` as console: default Information, `--debug` Debug, `--verbose` Verbose.
- Every service-file event must carry its existing `ServiceName` scope; do not add a new logging abstraction unless existing `ForService` cannot cover the entry point.
- Follow one-class-per-file, no inline comments, ErrorOr, and editorconfig-as-error rules.
- Do not commit changes unless explicitly requested.

---

## Task 1: Write and verify a failing telemetry routing check

**Files:**
- Create: `.omo/evidence/2026-08-17-delete-sacd-probe-harness-telemetry-red.md`

**Interfaces:**
- Consumes: current `src/Core/Telemetry.cs`, `src/CLI/Audio/SacdConvertCommand.cs`, and `src/App/Program.cs` behavior.
- Produces: a recorded RED check proving the current service sink does not share `LevelSwitch` and the SACD pipeline command lacks `ForService(Audio)`.

- [ ] **Step 1: Record the failing assertions without changing production code**

Run from `C:\Users\Lance\Dev\Toolbox`:

```powershell
rg -n "restrictedToMinimumLevel|ControlledBy\(LevelSwitch\)|ForService\(ServiceName\.Audio\)" src/Core/Telemetry.cs src/CLI/Audio/SacdConvertCommand.cs
```

Expected RED evidence: `Telemetry.cs` contains `ControlledBy(LevelSwitch)` only on the Spectre sink, the file sink uses `restrictedToMinimumLevel: LogEventLevel.Debug`, and `SacdConvertCommand.cs` has no `ForService(ServiceName.Audio)`.

- [ ] **Step 2: Save the exact RED output**

Write the command output and the two expected failures to `.omo/evidence/2026-08-17-delete-sacd-probe-harness-telemetry-red.md`. This is a documentation-only test artifact; it is not production code or a test package.

- [ ] **Step 3: Verify RED**

Run the same `rg` command and confirm both conditions remain true before implementation. Do not alter the assertions to match existing behavior.

---

## Task 2: Delete obsolete harness and registration

**Files:**
- Delete: `src/Services/Audio/SacdProbeRunner.cs`
- Delete: `src/Services/Audio/SacdProbeService.cs`
- Delete: `src/Services/Audio/RealDffFixture.cs`
- Modify: `src/Services/Audio/AudioSetup.cs:15-20`
- Verify: all project/source references with `rg`

**Interfaces:**
- Consumes: no callers outside the deleted wrapper/harness; `DsdConvertService.ProbeDsdAsync` remains untouched.
- Produces: Audio DI registration without `SacdProbeService`; no source reference to deleted harness symbols.

- [ ] **Step 1: Delete the three isolated harness files**

Remove only the three files listed above. Do not remove `DsdConvertService.cs`, `DffMetadataStripper.cs`, `DsdConvertCommand.cs`, `tools/ProbeVerify`, or historical journal/spec files.

- [ ] **Step 2: Remove the dead DI registration**

Delete only `services.AddSingleton<SacdProbeService>();` from `AudioSetup.AddAudioServices()`. Keep PATH validation and all production service registrations.

- [ ] **Step 3: Verify no production probe capability was deleted**

Run:

```powershell
rg -n "SacdProbeRunner|SacdProbeService|RealDffFixture|RunProbeAsync|ProbeDsdAsync" src tools Toolbox.slnx
```

Expected: no `SacdProbeRunner`, `SacdProbeService`, `RealDffFixture`, or `RunProbeAsync` matches; `ProbeDsdAsync` still matches `DsdConvertService`, `PipelineOrchestrator`, `DiscOutputInspector`, `DsdConvertCommand`, and `tools/ProbeVerify`.

- [ ] **Step 4: Build deletion result**

Run `dotnet build`. Expected: exit code 0, no errors, no new warnings caused by this task.

---

## Task 3: Wire service-file level gate and Audio scope

**Files:**
- Modify: `src/Core/Telemetry.cs:53-68`
- Modify: `src/CLI/Audio/SacdConvertCommand.cs:31-35`

**Interfaces:**
- Consumes: `Telemetry.Configure(LogEventLevel level)`, `Telemetry.ForService(ServiceName service)`, and `Program.Main` level selection (`Information` default, `Debug` for `--debug`, `Verbose` for `--verbose`).
- Produces: file sinks governed by `LevelSwitch`; SACD pipeline logs stamped `Service=Audio` from command entry through awaited orchestration.

- [ ] **Step 1: Implement the minimal file-sink fix**

In `Telemetry.AddServiceLogger`, replace the fixed file minimum-level restriction with the existing switch-controlled minimum-level configuration:

```csharp
lc.MinimumLevel.ControlledBy(LevelSwitch)
    .Filter.ByIncludingOnly(...)
    .WriteTo.File(...)
```

Preserve the existing service filter, formatter, path, rolling policy, retention, and file-size settings. Do not create a second switch or new logging wrapper.

- [ ] **Step 2: Add the missing SACD pipeline scope**

At the start of `SacdConvertCommand.ExecuteAsync`, before calling `PipelineOrchestrator.RunAsync`, add:

```csharp
using IDisposable _ = Telemetry.ForService(ServiceName.Audio);
```

Keep `DsdConvertCommand` unchanged because it already owns this scope. Keep YouTube unchanged because `SyncYoutubeCommand.ExecuteAsync` already owns `ForService(ServiceName.YouTube)`.

- [ ] **Step 3: Verify GREEN structurally**

Run:

```powershell
rg -n "ControlledBy\(LevelSwitch\)|restrictedToMinimumLevel|ForService\(ServiceName\.Audio\)|ProbeDsdAsync" src/Core/Telemetry.cs src/CLI/Audio/SacdConvertCommand.cs src/Services/Audio src/Services/Google/YouTube tools/ProbeVerify
```

Expected: the file sink uses `ControlledBy(LevelSwitch)`, no fixed `restrictedToMinimumLevel` remains in `Telemetry.cs`, SACD command has `ForService(ServiceName.Audio)`, and production `ProbeDsdAsync` remains.

- [ ] **Step 4: Build GREEN**

Run `dotnet build`. Expected: exit code 0, no errors, no style violations.

---

## Task 4: Patch integrated toolbox spec

**Files:**
- Modify: `toolbox-spec.md`

**Interfaces:**
- Consumes: integrated spec sections 0, 5, 7, 8, 12, 13, and 14.
- Produces: spec that states harness deletion, universal production probing, telemetry behavior, and corrected counts consistently.

- [ ] **Step 1: Replace harness relocation with deletion**

Update every occurrence of `tools/sacd-probe`, “move harness”, “harness preserved in tools”, and “move not split” so the spec says:

> `SacdProbeRunner + SacdProbeService + RealDffFixture` are obsolete one-off regression harness code with zero pipeline callers. Delete them. Preserve the historical journal/spec. `DsdConvertService.ProbeDsdAsync` remains production code and remains universal.

- [ ] **Step 2: Make telemetry permanence explicit**

Add to the Audio/Telemetry sections:

> `ProbeDsdAsync` is universal production validation, not harness tooling. Its `Telemetry.Debug` events remain part of every conversion path. Service files follow `LevelSwitch`: Information by default, Debug with `--debug`, Verbose with `--verbose`; command-boundary `ForService` scopes route events to the correct JSONL file.

- [ ] **Step 3: Correct metrics and bucket wording**

Keep the overall target delta `-820 to -960` only if the deleted harness is counted consistently. Change audio bucket wording from “move (-375 to tools)” to “delete (-422 harness, including 15-line wrapper + 357-line runner + 50-line fixture)” and remove the separate “-3 registration” relocation claim. Recalculate the displayed Audio net and global file delta so they do not claim both relocation and deletion. Do not change unrelated YT/Azure findings.

- [ ] **Step 4: Correct integrated diff appendix**

Change the prior semantic-conflict resolution from “move” to “delete”; explain that move is unnecessary because no production caller needs the matrix. Retain the distinction between harness diagnostics and `ProbeDsdAsync` logging.

- [ ] **Step 5: Verify spec consistency**

Run:

```powershell
rg -n "tools/sacd-probe|move.*harness|harness preserved|ProbeDsdAsync|LevelSwitch|ForService|SacdProbeRunner|RealDffFixture" toolbox-spec.md
```

Expected: no stale relocation claim; all remaining harness mentions explicitly say delete; production probe and telemetry contract appear in sections 5, 6, 8, 12-14.

---

## Task 5: Final verification and diff review

**Files:**
- Verify: all files changed by Tasks 2-4

- [ ] **Step 1: Run diagnostics**

Run `lsp_diagnostics` on every changed C# file: `src/Core/Telemetry.cs` and `src/CLI/Audio/SacdConvertCommand.cs`. Deleted files require no diagnostics.

- [ ] **Step 2: Run build**

Run `dotnet build` from `C:\Users\Lance\Dev\Toolbox`. Expected: exit code 0, 0 errors, 0 new warnings.

- [ ] **Step 3: Run CLI surface checks**

Run:

```powershell
dotnet run --project src\App -- --help
dotnet run --project src\App -- audio sacd-convert --help
```

Expected: both exit 0; audio command startup does not resolve or invoke deleted `SacdProbeService`.

- [ ] **Step 4: Review diff scope**

Run:

```powershell
```

Confirm only requested harness deletion, telemetry scope/sink changes, and spec changes are present. Do not commit.
