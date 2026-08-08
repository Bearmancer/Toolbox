# SACD Saracon Death-Loop: Repro Harness, Journal, Fix Plan — Design

**Date:** 2026-08-08
**Status:** Approved by user
**Session source:** Kiro `sess_5e94dcd5-45b3-4cb3-992a-75d59671dd2b` ("SACD Saracon failure - proper root cause analysis") — 662 messages, reviewed in 25-line chunks, 103 findings.

---

## 1. Problem

SACD → FLAC pipeline (sacd_extract → saracon → sox) fails persistently. A 13-hour debugging session produced 4 off-target fixes, none verified, issue persisted. Root causes of the debugging failure itself, not just the pipeline failure:

- Failure never reproduced by the agent (user reproduced it)
- 9 debug ps1 scripts + 3 speculative docs littered the repo
- `execute_pwsh` quoting/exit-code lies cost ~30 failed calls
- `SACD errors.md` (9081 lines of prior investigation) read only 280 lines
- Root-cause claims ("COMPLETE FAILURE MAP", "ROOT CAUSE IDENTIFIED") repeatedly outran evidence
- Three distinct Saracon failure outcomes observed (charset hang, silent truncation 6.5/11.67MB, zero bytes) — run-to-run variance never examined as variance
- `list_directory`/multi-file `read_files` results not persisted in Kiro transcript (`{}`) — evidence trail gaps

Verified code-level gaps (from source):

- **A1** `SaraconService.FindSaraconOutput` accepts ANY matching output — no size sanity check. Truncated 11MB output = success.
- **A2** `DffMetadataStripper.HasId3Chunk` catches all exceptions → `return false` → strip silently skipped (sacd-ripper #94: ID3 chunk-size miscoding in sacd_extract DFFs).
- **A3** `IsCharsetError`/`IsTransientError` match loose substrings ("encoding") → false-positive retries.
- **A4** `CleanupLockedFiles` deletes before retry while the hung process may still hold the handle.
- **A5** Filename hypothesis (user): `[SACD]` brackets + spaces + `-d2p` suffix names may trigger 2010 wxWidgets charset path in Saracon. Untested.

Verified DFF facts: FRM8 → FVER, PROP{CHNL, SLFT, SRGT, CMPR, LSCO}, DSD (3GB), ID3 trailing (Saracon warns 7× "Unknown chunk (ID3)"). Saracon 01.61-27, `-T` always used, resolves binaries from PATH.

## 2. Goal

Replace the death loop with a seconds-fast, evidence-producing harness that:

1. Reproduces the failure modes through the REAL service code paths (not reimplementations)
2. Appends every run to a native debug journal
3. Gates each fix on journal evidence (probe before → probe after → case flips FAIL→PASS)
4. Stays as permanent regression tool; all session noise pruned afterward

## 3. Approach (approved: A)

In-repo service-layer probe harness. NOT a CLI command (user: service-layer logic, user flow ISO→FLAC stays abstracted). Standalone `.cs` with `Main()` per project rule 4 ("No test NuGet packages. Standalone .cs files with Main() for manual verification").

## 4. Architecture

```
tools/SacdProbe/                          # NEW tiny console project, references Services.Audio + Core only
├── SacdProbe.csproj
├── Program.cs                            # Main: run matrix, print verdict table, exit code
└── ProbeRunner.cs                        # per-case: generate → convert → measure → journal append
    DffFixtureFactory.cs                  # chunk-level synthetic DFF builder
.superpowers/audit/sacd-probe-journal.md  # NEW journal (native .superpowers/audit pattern)
```

### ProbeRunner flow (per case)

1. `DffFixtureFactory` writes tiny DFF (~1s audio, KBs) to `C:\Temp\saracon-probe\`
2. Run through REAL `SaraconService.ConvertDsdToPcmAsync` (raw variant)
3. Run again through `DffMetadataStripper.StripId3TagsAsync` first (stripped variant) — 12 runs total (6 cases × 2)
4. Measure: exit code, elapsed, output bytes, captured stdout/stderr, WAV header probe (channels/rate/duration)
5. Append journal row; accumulate verdict
6. Exit 0 only if ALL runs match expected verdicts; nonzero on any FAIL-unexpected

Harness wires services manually (DI-less, ~5 lines). No production file edited in this phase.

## 5. Fixture matrix (6 cases, trimmed from 7 — LSCO-only dropped as predictably benign)

| Case | Variation | Targets |
|---|---|---|
| 1 | Baseline clean DFF, plain ASCII name | Control — must convert fully |
| 2 | + trailing ID3 chunk (valid size) | Stripper path, ID3 warning |
| 3 | + ID3 chunk with miscoded size (off-by-1/null-pad) | A2: HasId3Chunk exception masking |
| 4 | + COMT chunk with non-ASCII bytes (UTF-8/UTF-16) | charset hypothesis |
| 5 | Filename with brackets `[SACD]` + spaces + `(N)` | A5: filename hypothesis |
| 6 | Case 3 + case 5 combined | interaction |

Expected: case 1 PASS; at least one of 2–6 reproduces hang/silent-truncation/no-output (the death loop, in seconds).

## 6. Journal

File: `.superpowers/audit/sacd-probe-journal.md` (native pattern; tables with verdicts).

One row per run, appended by harness (append-only):

```
| timestamp | case | variant(raw/stripped) | exit | elapsed | out-bytes | verdict | snippet |
```

Sections:
- `## Runs` — table above, append-only
- `## Findings` — maintained by agent: cross-case analysis, librarian findings, fix evidence (before/after rows)

Verdicts: `PASS` (expected outcome observed) / `FAIL-expected` (reproduced known failure) / `FAIL-unexpected` (new behavior — causes nonzero probe exit).

## 7. Fix plan (evidence-gated, in order)

No fix ships without its journal evidence (probe before → probe after → case flips to PASS).

1. **Filename staging** — if case 5/6 reproduces and sanitized-name run doesn't: stage DFF to sanitized temp name in `SaraconService.RunConversionWithRetryAsync` before invoking saracon.
2. **Output-size sanity check** — in `SaraconService` after output found: expected bytes from DFF duration/channels/bitdepth; fail if <50% expected. Kills A1.
3. **`HasId3Chunk` exception logging** — log the exception, never silent `return false`. Kills A2.
4. **A/B without `-T`** via probe — if tolerant mode triggers truncation, gate `-T` behind setting.
5. Optional: tighten `IsCharsetError`/`IsTransientError` matching from substring to evidence-based.

## 8. Librarian deployment (after spec approved)

Two background librarian agents:
1. Saracon 01.61-27 charset/truncation specifics (manual PDF, wxWidgets, forums)
2. DSDIFF ID3/chunk-size corruption in sacd_extract + known-good conversion tooling

Findings appended to journal `## Findings`, reconciled against probe verdicts before fixes start.

## 9. Cleanup phase (post-fixes, user-mandated)

Delete all session noise:
- Repo root: `dff-inspect.csx`, `inspect-dff.ps1`, `debug-sacd-disc10.ps1`, `test-saracon-simple.ps1`, `extract-and-test.ps1`, `test-saracon-now.ps1`, `diagnose-saracon-complete.ps1`, `strip-dff-metadata.ps1`, `test-saracon-gui-popup.ps1`
- Docs: `SACD-Saracon-Analysis-Summary.md`, `docs/SACD-SARACON-ISSUE.md`
- Temp: `C:\Temp\saracon-probe\`, `C:\Temp\saracon_test`, `C:\Temp\saracon_diagnostics`, `C:\Temp\saracon_popup_test`, `C:\Temp\check-file-size.ps1`, `C:\Temp\run-strip.ps1`
- Keep: probe harness, journal, this spec, `SACD errors.md` (history)

## 10. Success criteria (Done =)

- [ ] Probe exits 0 (all cases expected) — repeatedly after each fix
- [ ] Real Disc 10 final gate: full `audio sacd-convert` run completes with correct output size
- [ ] Journal complete: every run + findings + fix evidence
- [ ] Noise pruned per Section 9
- [ ] `dotnet build` clean
- [ ] Fixes committed atomically (1–3 files per commit)

## 11. Rules (project + session lessons)

- Phase 1 changes NOTHING in `src/Services/Audio` — new files only
- No new NuGet packages, no test frameworks
- No ps1 scripts, no shell for FS ops (use tools); fixtures never in repo
- Build-verify after every edit (project rule 1)
- No root-cause claim without journal evidence (session lesson #1)
- Execute via native tools/`dotnet run`, never `execute_pwsh` with spaces/quotes (session lesson #2)
