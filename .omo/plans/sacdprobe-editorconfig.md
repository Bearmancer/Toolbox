# Plan: SacdProbe Merge + Editorconfig Enforcement

**Status:** approved  
**Intent:** CLEAR  
**Date:** 2026-08-13

---

## Context

Two issues requiring resolution:

1. **SacdProbe location**: Currently in `tools/SacdProbe/` as a standalone diagnostic harness probing saracon wxWidgets charset bug. User wants it absorbed into `src/Services/Audio/` as a diagnostic capability.

2. **Editorconfig enforcement**: `.editorconfig` rules not fully enforced during `dotnet build`. Investigation found 4 concrete defects preventing build-time enforcement.

---

## Research Findings

### SacdProbe Analysis

**Current state** (from exploration):
- Location: `tools/SacdProbe/` (5 files)
- Purpose: Diagnostic harness probing saracon's wxWidgets charset bug with specific test data
- Has hardcoded paths (`C:\Temp\t.dff`, `C:\Temp\saracon-probe\`)
- Uses test fixtures (RealDffFixture, DffFixtureFactory)
- References Audio services (SaraconService, ProcessRunner, DffMetadataStripper)
- Not a production tool — for debugging/reproduction only

**Best-practice guidance** (from librarian research):
- Diagnostic tools should STAY separate when they: need test data/fixtures, have heavy dependencies, modify state, or are for CI/CD validation
- Diagnostic tools should MERGE into services when they: test service's own state, need service internals, or are always-on production diagnostics
- Standard patterns: `IHealthCheck`, `DiagnosticSource`, `EventSource`

**Decision**: User explicitly requested merge ("audio should have sacd probe"). This goes against best-practice recommendation (SacdProbe has test fixtures, hardcoded paths, is for debugging). Oracle will explain final structure and justify this decision.

### Editorconfig Analysis

**Current state** (from exploration):
- Single `.editorconfig` at root, `root = true`, 173 lines
- `EnforceCodeStyleInBuild=true` set in `Directory.Build.props`
- `LangVersion=preview`, `TargetFramework=net11.0`
- Collection expression rules (IDE0300-IDE0306) configured as `error`

**Defects found**:
1. **Malformed boolean entries** (lines 53, 55): Missing `true:` prefix
   - `csharp_style_implicit_object_creation_when_type_is_apparent = error` → should be `true:error`
   - `dotnet_style_prefer_collection_expression = error` → should be `true:error`
   - Roslyn parser drops invalid entries silently

2. **IDE0130 self-conflict**: Set to `error` at line 136, then `none` at lines 166-167 and 172-173 (duplicate suppression). Last entry wins → effectively `none`.

3. **Rules without severity suffix**: Lines 49, 104, 106 have no `:error` suffix → silent at build time.

4. **Naming rules are IDE-only**: `dotnet_naming_rule.*` never enforced at build (platform limitation).

**Best-practice guidance** (from librarian research):
- For .NET 9+: inline `option:severity` syntax works at build
- For .NET ≤8: must use `dotnet_diagnostic.IDExxxx.severity` syntax
- Project is net11.0 → inline syntax should work
- All IDE* rules need explicit severity configuration

---

## Approach

### Wave 1: Fix Editorconfig (4 tasks)

1. Fix malformed boolean entries (lines 53, 55)
2. Resolve IDE0130 conflict (delete duplicate suppression, pick one intent)
3. Add severity suffixes to silent rules (lines 49, 104, 106)
4. Verify `dotnet build` enforces all rules

### Wave 2: Merge SacdProbe into Audio (5 tasks)

1. Create `SacdProbeService` in `src/Services/Audio/` (wraps ProbeRunner logic)
2. Create `SacdProbeRunner` in `src/Services/Audio/` (probe matrix)
3. Move `RealDffFixture` into Audio (keeps diagnostic capability)
4. Delete `DffFixtureFactory` (dead code from v1)
5. Delete `tools/SacdProbe/` directory
6. Update DI registration in `AudioSetup.cs`

### Wave 3: Oracle Review (1 task)

Deploy oracle to:
- Explain final repo structure
- Justify why SacdProbe belongs in Audio (or should remain separate)
- Document the decision rationale
- Confirm no regressions

### Wave 4: Verification (3 tasks)

1. Build verification (0 errors, 0 warnings)
2. Run `dotnet build` with editorconfig enforcement
3. Confirm SacdProbe capability accessible via Audio services

---

## Must-Not-Have

- No test NuGet packages (xUnit, NUnit, MSTest)
- No new dependencies
- No breaking changes to Audio public API
- No changing `EnforceCodeStyleInBuild` or `LangVersion`
- No modifying `Directory.Build.props` or `Directory.Packages.props`

---

## Acceptance Criteria

### Editorconfig
- [x] All boolean style options have `true:`/`false:` prefix
- [x] No duplicate/conflicting severity entries
- [x] All rules have explicit severity suffix
- [x] `dotnet build` fails on style violations

### SacdProbe Merge
- [x] SacdProbe functionality accessible via `src/Services/Audio/`
- [x] `tools/SacdProbe/` directory deleted
- [x] DI registration updated
- [x] No regressions in Audio services

### Oracle Review
- [x] Final repo structure documented
- [x] Decision rationale explained
- [x] No unresolved concerns

### Verification
- [x] `dotnet build` succeeds with 0 errors, 0 warnings
- [x] Editorconfig enforcement confirmed
- [x] SacdProbe capability testable via Audio services

---

## Tasks

### Wave 1: Editorconfig Fixes

- [x] 1. Fix malformed boolean entries in `.editorconfig` (lines 53, 55)
  - Change `csharp_style_implicit_object_creation_when_type_is_apparent = error` to `true:error`
  - Change `dotnet_style_prefer_collection_expression = error` to `true:error`
  - Verify: `dotnet build` enforces these rules

- [x] 2. Resolve IDE0130 conflict in `.editorconfig`
  - Delete duplicate suppression block (lines 172-173)
  - Decide intent: keep as `error` (line 136) or `none` (line 166-167)
  - Default: keep as `error` (namespace should match folder structure)
  - Verify: no duplicate entries, single clear intent

- [x] 3. Add severity suffixes to silent rules in `.editorconfig`
  - Line 49: `csharp_style_var_elsewhere = false` → `false:error`
  - Line 104: `dotnet_sort_system_directives_first = true` → `true:error`
  - Line 106: `dotnet_separate_import_directive_groups = false` → `false:error`
  - Verify: `dotnet build` enforces these rules

- [x] 4. Verify editorconfig enforcement
  - Run `dotnet build` and confirm all rules enforced
  - Introduce intentional violations and confirm build fails
  - Verify: 0 errors, 0 warnings on clean code

### Wave 2: SacdProbe Merge

- [x] 5. Create `SacdProbeService` in `src/Services/Audio/`
  - Wrap ProbeRunner logic in a service class
  - Public method: `RunProbeAsync(CancellationToken ct)`
  - Return diagnostic results (pass/fail, journal path, variants tested)
  - Verify: compiles, no regressions

- [x] 6. Create `SacdProbeRunner` in `src/Services/Audio/`
  - Move probe matrix logic from `tools/SacdProbe/ProbeRunner.cs`
  - Keep 4-variant probe matrix: {raw, stripped} × {headless, visible}
  - Keep journal writing to `C:\Users\Lance\Dev\Toolbox-sacd-repro\.superpowers\audit\sacd-probe-journal.md`
  - Verify: logic preserved, compiles

- [x] 7. Move `RealDffFixture` into Audio
  - Move `tools/SacdProbe/RealDffFixture.cs` to `src/Services/Audio/`
  - Keep hardcoded path `C:\Temp\t.dff` (diagnostic tool, not production)
  - Verify: compiles, no regressions

- [x] 8. Delete `DffFixtureFactory` (dead code)
  - Delete `tools/SacdProbe/DffFixtureFactory.cs`
  - Verify: no references in codebase

- [x] 9. Delete `tools/SacdProbe/` directory
  - Delete entire directory after moving files
  - Remove from `Toolbox.slnx` solution file
  - Verify: solution builds, no broken references

- [x] 10. Update DI registration in `AudioSetup.cs`
  - Register `SacdProbeService` as singleton
  - Verify: DI container resolves service

### Wave 3: Oracle Review

- [x] 11. Deploy oracle to explain final structure
  - Explain why SacdProbe belongs in Audio (or should remain separate)
  - Document decision rationale
  - Confirm no regressions
  - Output: markdown report in `.omo/oracle-reports/sacdprobe-merge.md`

### Wave 4: Verification

- [x] 12. Build verification
  - Run `dotnet build`
  - Verify: 0 errors, 0 warnings
  - Verify: no regressions in existing tests

- [x] 13. Editorconfig enforcement verification
  - Introduce intentional style violations
  - Run `dotnet build`
  - Verify: build fails with appropriate errors
  - Revert violations

- [x] 14. SacdProbe capability verification
  - Call `SacdProbeService.RunProbeAsync()` via DI
  - Verify: probe runs, journal written
  - Verify: results accessible

---

## Dependencies

- Wave 1 (editorconfig) is independent
- Wave 2 (merge) is independent
- Wave 3 (oracle) depends on Wave 2 completion
- Wave 4 (verification) depends on Waves 1-3 completion

---

## Risks

1. **SacdProbe merge goes against best practice**: Research recommends keeping diagnostic tools with test fixtures separate. User override accepted. Oracle will document rationale.

2. **Hardcoded paths in SacdProbe**: `C:\Temp\t.dff`, `C:\Temp\saracon-probe\`, journal path. These are acceptable for diagnostic tools but would be unacceptable in production services.

3. **Editorconfig changes may break existing code**: If code violates newly-enforced rules, build will fail. Mitigation: fix violations in same commit.

---

## Success Criteria

- Editorconfig fully enforced at build time
- SacdProbe accessible as Audio service
- Oracle explains and justifies final structure
- Zero regressions
- Clean build (0 errors, 0 warnings)
