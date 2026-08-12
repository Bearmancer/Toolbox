> **CORRECTION (2026-08-11):** The UTF-8/ACP root cause originally claimed for the Saracon death loop was REJECTED by probe run #4 (all-PASS with ACP=65001). Verified root cause: ID3 chunks in DFF + Saracon retry self-restart loop, compounded by non-interactive session GUI failure. Evidence: docs/superpowers/audits/sacd-probe-journal.md. Do not restate the UTF-8 hypothesis as settled. Note (2026-08-12): after the Windows reinstallation the machine ACP is 1252 — the UTF-8 beta condition is absent entirely.

# SACD Saracon Death-Loop — Consolidated Plan (single source of truth)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Consolidation (2026-08-12):** This plan MERGES and SUPERSEDES the two earlier SACD plans:

- `2026-08-08-sacd-death-loop-repro.md` (repro harness plan — executed; its UTF-8 root-cause premise was rejected)
- `2026-08-09-sacd-saracon-death-loop-fix.md` (evidence-gated fix plan — executed through its code-fix tasks)

Both files were deleted when this plan landed; everything still binding from them is restated here. A byte-identical duplicate copy of the SACD docs that had accumulated under `.omo/docs/` was also deleted — `docs/` (git-tracked) is the sole canonical location. Supporting docs that remain: `docs/superpowers/audits/sacd-probe-journal.md` (append-only evidence journal) and `docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md` (governing design spec).

**Goal:** Close out the Saracon death-loop work: run the REAL Disc 10 pipeline gate against the real ISO — no dummy, no synthetic files — then close the journal and hand off.

**Architecture (unchanged, RED toolchain):** `sacd_extract -> DFF -> Saracon -> SoX -> tag`. Probe harness `tools/SacdProbe` stays in the tree as the regression gate.

**Tech Stack:** .NET 11.0, C#, `ErrorOr`, `ProcessRunner`, `saracon`, `sox`, `sacd_extract`, ATL.NET; no new NuGet packages and no test framework.

## Global Constraints

- Preserve RED guide toolchain: `sacd_extract -> DFF -> Saracon -> SoX -> tag`.
- Do not call SoX a DSD-to-PCM replacement; SoX cannot perform this conversion.
- Do not claim UTF-8/ACP, filename, metadata, or race hypotheses as root cause without a controlled journal result.
- Keep confidence tags (`HIGH`, `MEDIUM`, `LOW`) on every journal finding; never upgrade source confidence.
- No new NuGet packages, test frameworks, PowerShell scripts, or production fixture factories.
- Resolve `saracon`, `sox`, and `sacd_extract` from `PATH`; do not bundle binaries. `sox` lives at `C:\Program Files (x86)\sox-14-4-2\sox.exe` — add that directory to the session PATH before running the pipeline (it is not in the system PATH).
- Build after every source edit with `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`.
- NEVER delete the user's source ISO: every `audio sacd-convert` invocation MUST pass `--keep-iso` (the pipeline deletes source ISOs by default).
- One class per file; no explanatory inline comments, warning suppressions, or compatibility shims.

## Verified root cause (evidence-closed)

From the probe journal (real Disc 10 DFF, 2,302,380,392-byte PCM outputs):

1. **ID3 chunks** appended by `sacd_extract` trigger Saracon "Unknown chunk (ID3 )" noise and interact with the old retry logic. Fix landed: ID3 detection + strip before conversion (`DffMetadataStripper`, `Seek(16)` bounded chunk walk).
2. **Retry self-restart loop**: retrying a failed/timed-out Saracon launch respawned a new process against the same output filename while the previous instance's partial write had not released — the file-lock death loop. Fix landed: no retry inside `SaraconService`; restart only at disc level.
3. **Non-interactive session failure mode** (registry/OLE: `Can't open registry key 'HKCU\Software\Weiss Engineering\Saracon'`, `Cannot initialize OLE`, `wxIdleWakeUpModule`) blocks Saracon in some agent sessions. Mitigation landed: audio-only startup skips Google OAuth; headless runs later PASSED 3/3 in the probe journal, so headless execution is possible on this machine — but a registry/OLE failure remains the designated HALT condition.


## Status ledger (what is already DONE — do not redo)

| Item | Evidence |
|---|---|
| Probe harness `tools/SacdProbe` (signature-classified verdicts, journal) | commits up to `0395a1e`; journal runs table |
| UTF-8 hypothesis tested + REJECTED (probe run #4, ACP=65001, all-PASS) | journal finding 2026-08-11 |
| DFF metadata chunk walk hardened (`Seek(16)`, EOF bounds, exceptions surfaced) | `src/Services/Audio/DffMetadataStripper.cs`, commit `0395a1e` |
| Saracon output-size guard (`EstimateExpectedPcmBytes`, <50% expected => fail) | `src/Services/Audio/SaraconService.cs`, commit `0395a1e` |
| No-retry Saracon conversion (death-loop mechanism removed) | `src/Services/Audio/SaraconService.cs`, commit `0395a1e` |
| `DsdConvertService` rewired to `SaraconService`; invalid `SoxDsdService` deleted | commit `4a91384` + `0395a1e` |
| Audio-only startup skips Google/Azure/LastFm OAuth registration | `src/App/Program.cs`, commit `0395a1e` |
| UTF-8 root-cause docs corrected with banner; journal relocated to `docs/superpowers/audits/` | commit `62119f6` |
| Prior-session noise (debug ps1/csx scripts, scratch md files, `.superpowers`) pruned | commit `9592977`; root is clean |
| Filename staging (Task 9 of old fix plan) | SKIPPED: filename trigger never demonstrated (real-path runs passed without staging) |

## Remaining tasks

### Task 1: Real Disc 10 pipeline gate (REAL ISO — no dummy, no synthetic files)

**Input (user-mandated):** `C:\Users\Lance\Desktop\Music\Temp\Disc 10.iso`

**Interfaces:**
- Consumes: everything in the status ledger; `saracon` + `sacd_extract` on PATH; `sox` added to session PATH.
- Produces: one FLAC per CUE track (DSD64 default → 44.1 kHz / 16-bit), tagged from the CUE sheet; source ISO retained; intermediate DFF/CUE/WAV cleaned.

- [ ] **Step 1: Verify prerequisites**

```powershell
where.exe saracon
where.exe sacd_extract
$env:PATH = "C:\Program Files (x86)\sox-14-4-2;$env:PATH"
where.exe sox
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage" /v ACP
dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx
```

Expected: all three binaries resolve; ACP recorded (1252 post-reinstall); build exit 0.

- [ ] **Step 2: Launch the real pipeline (detached, poll for completion)**

Run from the repo root (so `logs/audio.jsonl` lands in the repo's gitignored `logs/`):

```powershell
dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- --verbose audio sacd-convert "C:\Users\Lance\Desktop\Music\Temp\Disc 10.iso" --keep-iso
```

Expected sequence (from `logs/audio.jsonl`):

```text
SacdExtract.ProbeComplete           (stereo/multichannel detection)
SacdExtract.Complete                (real DFF + CUE extracted from the ISO)
DsdConvert.GainCalcStart/Complete   (first full Saracon conversion, gain probe)
Saracon.Id3Detected (if present) + DffMetadataStripper completion
Saracon.ConvertComplete             (master conversion, size guard passed)
SoX split per CUE track + ATL tagging
Cleanup (DFF/CUE/XML removed, master WAV removed, ISO KEPT)
```

Hard requirements: Saracon exit 0; no charset error; no registry/OLE error; no "file is being used by another process" error; ZERO retry entries; master WAV passes the size guard (>= 50% of the PCM estimate computed from the DFF `DSD ` chunk).

Duration budget: two full Saracon conversions (gain probe + master) of a full disc — expect ~50–70 minutes total. Run detached and poll; do not treat elapsed time alone as failure before the 1h Saracon timeout.


- [ ] **Step 3: Validate outputs from the filesystem, not log claims**

```powershell
Get-ChildItem "C:\Users\Lance\Desktop\Music" -Recurse -Filter *.flac | Select-Object FullName, Length
Get-ChildItem "C:\Users\Lance\Desktop\Music" -Recurse -Include *.wav,*.dff | Measure-Object
Get-Item "C:\Users\Lance\Desktop\Music\Temp\Disc 10.iso" | Select-Object Length
```

Expected: FLAC count == CUE track count; every FLAC non-trivial in size; no leftover WAV/DFF; ISO still present with original size (1,086,652,416 bytes).

- [ ] **Step 4: Journal the gate**

Append to `docs/superpowers/audits/sacd-probe-journal.md` `## Findings` exactly one of:

```text
Final gate PASS. Confidence: HIGH (measured real Disc 10 run from ISO). Saracon output=<bytes>; cue tracks=<n>; FLAC outputs=<n>; file-lock errors=0; retries=0; ACP=<value>.
```

or:

```text
Final gate BLOCKED/FAIL. Confidence: HIGH (measured). Signature=<signature>; command=<command>; next action=<specific blocker resolution>.
```

Do not write "death loop resolved" unless every success check passes.

**HALT rule:** on a `RegistryOleInit` signature (`Can't open registry key` / `Cannot initialize OLE` / `wxIdleWakeUpModule`) the agent session is blocked by design — STOP, journal the signature, and hand the user this exact command for their INTERACTIVE terminal:

```powershell
$env:PATH = "C:\Program Files (x86)\sox-14-4-2;$env:PATH"
dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- --verbose audio sacd-convert "C:\Users\Lance\Desktop\Music\Temp\Disc 10.iso" --keep-iso
```

Then resume at Step 3 with the user's confirmation.

### Task 2: Close journal and commit evidence

- [ ] **Step 1:** Ensure Task 1's finding line is in the journal with its confidence tag.
- [ ] **Step 2:** `git add docs/superpowers/` then commit `docs(audio): record real Disc 10 pipeline gate result`. No unrelated files in the commit.

### Task 3: Final targeted verification and handoff

- [ ] **Step 1:** `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx` — exit 0.
- [ ] **Step 2:** Structural checks: `git status --porcelain`; `git log --oneline -8`; no `SoxDsdService|soxDsd` matches in `src`/`tools`.
- [ ] **Step 3:** Journal discipline spot-check: every `## Findings` entry carries a confidence tag; no UTF-8 claim restated as settled.
- [ ] **Step 4:** Mark final state with one exact outcome:

```text
READY: real Disc 10 gate passed from the real ISO; FLACs verified on disk; journal closed; single consolidated plan in place.
```

or:

```text
BLOCKED: gate=<result>; blocker=<specific measured reason>; next action=<specific step>.
```

## Commit sequence (remaining)

1. `docs(audio): consolidate SACD plans into a single plan` (this file; deletes the two superseded plans)
2. `docs(audio): record real Disc 10 pipeline gate result`

## Explicit exclusions

- No SoX-based DSD-to-PCM replacement.
- No new Saracon replacement service.
- No synthetic/dummy fixtures in the final gate — the real ISO only.
- No retries added to Saracon conversion.
- No deletion of the source ISO (always `--keep-iso`).
- No cleanup of unrelated YouTube/Azure/dashboard/state work.
