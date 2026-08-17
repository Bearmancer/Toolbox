# Task 15 Report — P2.3 Probe harness disposition

Branch: `sacd-completion-v2` (HEAD `1fb4064` before change)
Worktree: `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`

## Decision

`SacdProbeService` is DI-registered but `RunProbeAsync` has no caller; `RealDffFixture` hardcodes `C:\Temp\t.dff` in shipped assembly. Remove probe harness: delete `SacdProbeService.cs`, `SacdProbeRunner.cs`, `RealDffFixture.cs`, and the registration together. `DffMetadataStripper` is retained — it is used by `DsdConvertService` (lines 23, 33), not orphaned.

## Subtask 1 — Confirm orphaned / no public caller

Command: `rg -n "SacdProbeService|SacdProbeRunner|RealDffFixture|ProbeResult" --glob "*.cs"`

Raw output (pre-deletion):
```
src\Services\Audio\AudioSetup.cs:18: services.AddSingleton<SacdProbeService>();
src\Services\Audio\SacdProbeService.cs:3: public sealed class SacdProbeService(SaraconService saracon)
src\Services\Audio\SacdProbeService.cs:5: private readonly SacdProbeRunner Runner = new(saracon);
src\Services\Audio\SacdProbeService.cs:7: public Task<ProbeResult> RunProbeAsync(CancellationToken ct = default)
src\Services\Audio\SacdProbeRunner.cs:7: internal sealed class SacdProbeRunner(SaraconService saracon)
src\Services\Audio\RealDffFixture.cs:5: internal static class RealDffFixture
```

`SacdProbeService` referenced only by its own file and the DI registration. `SacdProbeRunner`/`RealDffFixture`/`ProbeResult` referenced only within the three files. No public caller of `RunProbeAsync` exists.

Result: **PASS**

## Subtask 2 — Delete files + registration

Command: `git rm src/Services/Audio/SacdProbeService.cs src/Services/Audio/SacdProbeRunner.cs src/Services/Audio/RealDffFixture.cs`

Raw output:
```
rm 'src/Services/Audio/RealDffFixture.cs'
rm 'src/Services/Audio/SacdProbeRunner.cs'
rm 'src/Services/Audio/SacdProbeService.cs'
```

Edit `AudioSetup.cs`: removed `services.AddSingleton<SacdProbeService>();` (line 18).

Diff:
```diff
 			services.AddSingleton<DiskSpaceChecker>();
-			services.AddSingleton<SacdProbeService>();
 			services.AddSingleton(sp => new SacdExtractService(
```

Result: **PASS**

## Subtask 3 — Reference search after deletion

Command: `rg -n "SacdProbeService|SacdProbeRunner|RealDffFixture|ProbeResult" --glob "*.cs"`

Raw output (post-deletion): only unrelated `DsdProbeResult` / `SacdProbeResult` matches remain (used by `DsdConvertService`, `SacdExtractService`, `DiscOutputInspector`, `PipelineOrchestrator`, `DsdConvertCommand`). No match for the deleted types.

Result: **PASS**

## Subtask 4 — Clean build

Command: `dotnet build Toolbox.slnx --no-restore --no-incremental`

Raw output (tail):
```
  Audio -> ...\Audio\debug\Audio.dll
  CLI -> ...\CLI\debug\CLI.dll
  App -> ...\App\debug\App.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:10.97
```

Result: **PASS**

## Acceptance

- Three files and registration gone: **PASS**
- Clean build (0 warnings, 0 errors): **PASS**
- No unreferenced public member remains: **PASS** (`SacdProbeService`/`ProbeResult` removed; `DffMetadataStripper` retained and referenced by `DsdConvertService`)

No runtime blocker required — static removal and clean build observed.
