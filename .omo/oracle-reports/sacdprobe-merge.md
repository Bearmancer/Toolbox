# Oracle Report: SacdProbe Merge into Services.Audio

**Date:** 2026-08-14 | **Scope:** read-only analysis | **State basis:** working tree (merge uncommitted; deletions unstaged, new files untracked)

---

## 1. Final Repo Structure

SacdProbe moved from a standalone console project (`tools/SacdProbe/`, 5 files, its own `.csproj`) into the Audio service library as three files plus one DI registration:

```
Toolbox/
├── Toolbox.slnx                    # 7 projects, all under src/ — no tools/ entry
├── src/
│   ├── Services/Audio/
│   │   ├── SacdProbeService.cs     # NEW — public facade: RunProbeAsync() → ProbeResult
│   │   ├── SacdProbeRunner.cs      # NEW — internal; 4-variant probe matrix + journal writer
│   │   ├── RealDffFixture.cs       # MOVED — internal; DSD chunk parser for C:\Temp\t.dff
│   │   ├── AudioSetup.cs           # MODIFIED — AddSingleton<SacdProbeService>() (line 34)
│   │   ├── SaraconService.cs       # unchanged — probe consumes ConvertDsdToPcmAsync
│   │   ├── DffMetadataStripper.cs  # unchanged — probe consumes StripId3TagsAsync
│   │   └── ... (pipeline files untouched)
│   └── CLI/Audio/                  # unchanged — sacd-convert, dsd-convert only
└── tools/                          # EMPTY — SacdProbe was its only occupant
```

**What was deleted, and why:**

| Deleted file            | Disposition                                                                 |
| ----------------------- | --------------------------------------------------------------------------- |
| `ProbeRunner.cs`        | Renamed/moved → `SacdProbeRunner.cs`, logic preserved (matrix, canary, classifier, journal append) |
| `RealDffFixture.cs`     | Moved verbatim into Audio                                                    |
| `Program.cs`            | Deleted — standalone entry point replaced by DI-resolvable service facade     |
| `SacdProbe.csproj`      | Deleted — code now compiles inside `Audio.csproj`                             |
| `DffFixtureFactory.cs`  | Deleted — v1 synthetic-fixture generator, dead since v2 switched to the real DFF fixture |

**Probe semantics preserved:** 4-variant matrix `{raw, stripped} × {headless, visible}`; canary run (`raw/headless`) gates on registry/OLE-init failures before the full matrix; visible variants declare `CharsetEncoding` as the expected failure signature (that is the wxWidgets bug being tracked); results append to a markdown journal table with dedup.

---

## 2. Decision Rationale

The merge was explicitly requested by the user, overriding librarian research that recommended keeping fixture-bound diagnostics separate. The override is defensible on technical grounds, not just authority:

1. **Dependency direction was already inverted.** The probe's entire value is exercising Audio internals: `SaraconService.ConvertDsdToPcmAsync` (SaraconService.cs:12), `DffMetadataStripper.StripId3TagsAsync` (DffMetadataStripper.cs:75), `ProcessRunner.IsOnPath` (ProcessRunner.cs:211). As `tools/SacdProbe/` it was a separate project referencing the production service library — a diagnostic tail wagging the service dog. Merging removes the cross-project edge; the probe now lives where its dependencies live.
2. **It tests the service's own integration, not general behavior.** Research's own merge criteria include "tests service's own state." The probe exists because saracon (Audio's own external binary) has a wxWidgets 2.8.12 charset bug (codepage 65001 → "Unknown encoding (-1)", per journal finding 2026-08-08). This is an Audio-domain regression gate, not a general-purpose tool.
3. **It was already outside the build perimeter.** `Toolbox.slnx` listed only `src/` projects; `tools/SacdProbe` never built with the solution, so editorconfig enforcement and build verification skipped it. Merging brings it under the same style/build gates as everything else.
4. **Blast radius on production code is zero.** The probe is additive: one new DI singleton, no changes to `PipelineOrchestrator`, `DsdConvertService`, or any pipeline path. Nothing in the conversion pipeline references the probe. `SacdProbeRunner` and `RealDffFixture` are `internal`; only `SacdProbeService` + `ProbeResult` are public.

**Cost accepted:** hardcoded machine-specific paths (`C:\Temp\t.dff`, `C:\Temp\saracon-probe\out`) now live in a production service assembly. Mitigations in place: `internal` visibility, and a graceful `PRECONDITION FAILED` exit when the fixture is absent, so the probe degrades safely on any machine without the fixture.

---

## 3. Trade-offs

**Gained:**
- One build, one project, one convention surface — no orphan project escaping `.editorconfig`/solution builds.
- Dependency edge deleted (tools→Audio reference gone); probe consumes services in-process via DI.
- Dead code purged (`DffFixtureFactory`, v1 synthetic fixtures).
- Discoverability: the diagnostic lives beside the code it diagnoses.

**Lost:**
- **Standalone executability.** `Program.cs` is gone and no CLI command invokes `SacdProbeService` (verified: `AudioCommandModule` registers only `sacd-convert` and `dsd-convert`; grep finds zero consumers outside Audio). The probe is currently reachable only by writing code against the DI container.
- **Isolation.** A compile error or style violation in probe code now blocks the Audio build. Acceptable given the probe is ~390 lines, but real.
- **Purity of the service assembly.** Test-fixture and hardcoded-path code ships inside `Services.Audio.dll`. Harmless in practice (nothing calls it unintentionally) but a future reader must know it is diagnostic-only.

---

## 4. Recommendations

Ranked by severity:

1. **[Bug — fix before relying on the probe] Hardcoded journal path is dead.** `SacdProbeRunner` writes its journal to `C:\Users\Lance\Dev\Toolbox-sacd-repro\.superpowers\audit\sacd-probe-journal.md` (SacdProbeRunner.cs:8-14). That directory **does not exist** (verified via `Test-Path` → False). First `AppendJournal` call after the canary will throw `DirectoryNotFoundException` and crash the run. Meanwhile the journal was relocated into this repo (commit `62119f6`, now at `docs/superpowers/audits/sacd-probe-journal.md`). Fix: build the path from `PathResolver.RepoRoot` (Core already exposes it) to the relocated journal. Note the relocated journal's existing table is the v1 schema (`## Runs`, has a `case` column); the writer looks for `## Runs (v2` and will append a new section rather than merge — acceptable, but worth knowing.
2. **[Gap] No invocation path.** Either add a thin `audio probe` command in `src/CLI/Audio/` (matches the established thin-command pattern, ~20 lines) or document that the probe is intentionally library-only. As-is, plan acceptance criterion "SacdProbe capability testable" requires hand-written DI code.
3. **[Docs] `src/Services/Audio/AGENTS.md` is stale.** Its STRUCTURE section lists neither the three new probe files nor `DffMetadataStripper.cs`. Add entries marking the probe files as diagnostic-only so future agents don't wire them into the pipeline.
4. **[Naming] Collision risk.** `SacdExtractService.ProbeAsync` returns a `SacdProbeResult` record (ISO stereo/multichannel probe) — unrelated to the charset diagnostic now called `SacdProbeService`. Two different "SacdProbe" concepts in one namespace. Rename the diagnostic to `SaraconCharsetProbeService` (or rename the extract record) when convenient. Not blocking.
5. **[Hygiene] Commit the merge.** The merge currently sits uncommitted in a working tree that also carries the unrelated flatline refactor (~200 modified files). Commit the probe files + deletions atomically per repo rules so the merge stays revertable.

**Do not** refactor `RunVisibleAsync` to route through `ProcessRunner` — it deliberately spawns saracon with `CreateNoWindow = false` because the charset bug only reproduces in a visible session. The duplicated argument list is the price of that isolation and is correct as-is.

---

## Appendix: Verification Evidence

- Directory listings: `src/Services/Audio/` (18 files incl. 3 new), `tools/` (empty).
- `git status`: `D tools/SacdProbe/*` (5 files), `?? src/Services/Audio/{SacdProbeService,SacdProbeRunner,RealDffFixture}.cs`.
- `Toolbox.slnx`: 7 projects, none under `tools/`.
- Grep across `src/`: only consumer of `SacdProbeService` is `AudioSetup.cs:34` (DI registration); no CLI command.
- `Test-Path C:\Users\Lance\Dev\Toolbox-sacd-repro` → **False** (journal target dead).
- `docs/superpowers/audits/sacd-probe-journal.md` exists; root-cause finding (wxWidgets 2.8.12 vs codepage 65001) recorded 2026-08-08.
- Merge plan: `.omo/plans/sacdprobe-editorconfig.md` (approved 2026-08-13; documents user override of best-practice guidance).
- Build verification is owned by plan Wave 4 (not re-run here; analysis is structural).
