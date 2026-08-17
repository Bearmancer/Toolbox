# SACD Probe Journal

Append-only debug journal for the Saracon death-loop reproduction and fix work. Spec: docs/superpowers/specs/2026-08-08-sacd-death-loop-repro-design.md

## Runs

| timestamp | case | variant | exit | elapsed | out-bytes | verdict | snippet |
|---|---|---|---|---|---|---|---|
| 2026-08-10 12:23:03 | raw/headless-canary | -1 | 2795524ms | 0 | FAIL-unexpected(Other) | Conversion failed for C:\Temp\t.dff: saracon exit code -1:  |
| 2026-08-10 12:23:24 | raw/headless | -1 | 20303ms | 0 | FAIL-unexpected(Other) | Conversion failed for C:\Temp\t.dff: saracon exit code -1:  |
| 2026-08-10 12:23:46 | stripped/headless | -1 | 22648ms | 0 | FAIL-unexpected(Other) | Conversion failed for C:\Temp\t.dff: saracon exit code -1:  |
| 2026-08-10 12:24:09 | raw/visible | 1 | 23104ms | 0 | FAIL-unexpected(Other) | {12:23:46.902}    Saracon 01.61-27 (Mar  4 2010, 11:29:38)   Copyright (c) 2004 - 2010 Weiss Engineering, Switzerland    {12:23:46.904} License: Saracon DSD.  {12:23:46.905} Saracon has been set into tolerant mode.  {12:23:46} [warning] LSCO chunk not supported.  {12:23:46} [warning] DIIN chunk not supported.  {12:23:46} [warning] Unknown chunk (ID3 ).  {12:23:46} [warning] Unknown chunk (ID3 ).  {12:23:46} [warning] Unknown chunk (ID3 ).  {12:23:46} [warning] Unknown chunk (ID3 ).  {12:23:46} [ |
| 2026-08-10 12:27:14 | stripped/visible | -1 | 184723ms | 0 | FAIL-unexpected(Other) | {12:24:10.006}    Saracon 01.61-27 (Mar  4 2010, 11:29:38)   Copyright (c) 2004 - 2010 Weiss Engineering, Switzerland    {12:24:10.011} License: Saracon DSD.  {12:24:10.011} Saracon has been set into tolerant mode.  {12:24:10} [warning] LSCO chunk not supported.  {12:24:10} [warning] DIIN chunk not supported.  {12:24:10} [warning] Unknown chunk (ID3 ).  {12:24:10} [warning] Unknown chunk (ID3 ).  {12:24:10} [warning] Unknown chunk (ID3 ).  {12:24:10} [warning] Unknown chunk (ID3 ).  {12:24:10} [ |
| 2026-08-10 13:20:46 | raw/headless-canary | 0 | 1575840ms | 2302380392 | PASS | t-d2p.wav |
| 2026-08-10 13:47:25 | raw/headless | 0 | 1598834ms | 2302380392 | PASS | t-d2p.wav |
| 2026-08-10 17:04:11 | raw/headless-canary | 0 | 1385655ms | 2302380392 | PASS | t-d2p.wav |
| 2026-08-10 17:15:02 | raw/headless | -1 | 651062ms | 0 | FAIL-unexpected(Other) | Conversion failed for C:\Temp\t.dff: saracon exit code -1:  |
| 2026-08-10 17:38:42 | stripped/headless | 0 | 1420499ms | 2302380392 | PASS | t-d2p.wav |
| 2026-08-10 18:02:24 | raw/visible | 0 | 1421756ms | 0 | FAIL-unexpected(Other) | {17:38:42.924}    Saracon 01.61-27 (Mar  4 2010, 11:29:38)   Copyright (c) 2004 - 2010 Weiss Engineering, Switzerland    {17:38:42.926} License: Saracon DSD.  {17:38:42.926} Saracon has been set into tolerant mode.  {17:38:42} [warning] LSCO chunk not supported.  {17:38:42} [warning] DIIN chunk not supported.  {17:38:42} [warning] Unknown chunk (ID3 ).  {17:38:42} [warning] Unknown chunk (ID3 ).  {17:38:42} [warning] Unknown chunk (ID3 ).  {17:38:42} [warning] Unknown chunk (ID3 ).  {17:38:42} [ |
| 2026-08-10 18:25:09 | stripped/visible | 0 | 1365130ms | 0 | FAIL-unexpected(Other) | {18:02:24.677}    Saracon 01.61-27 (Mar  4 2010, 11:29:38)   Copyright (c) 2004 - 2010 Weiss Engineering, Switzerland    {18:02:24.678} License: Saracon DSD.  {18:02:24.678} Saracon has been set into tolerant mode.  {18:02:24} [warning] LSCO chunk not supported.  {18:02:24} [warning] DIIN chunk not supported.  {18:02:24} [warning] Unknown chunk (ID3 ).  {18:02:24} [warning] Unknown chunk (ID3 ).  {18:02:24} [warning] Unknown chunk (ID3 ).  {18:02:24} [warning] Unknown chunk (ID3 ).  {18:02:24} [ |

## Findings

### 2026-08-09 22:08:47 +05:30 — Agent-context real-DFF canary blocked

Saracon launched from `SacdProbe` in Session 1 but did not return after more than 10 minutes. Output reached only 592172 bytes, far below the real DFF PCM estimate, and the process had to be terminated. No registry/OLE or charset signature was captured. Confidence: HIGH (measured process timeout and output size). Repeat from the user's interactive terminal before interpreting ACP or filename behavior.

### 2026-08-08 20:37:26 +05:30 — Librarian research + local verification (ROOT CAUSE CANDIDATE CONFIRMED)

**Charset error "Unknown encoding (-1)" = wxWidgets cannot map Windows UTF-8 codepage 65001.**
- Saracon 01.61-27 (2010) bundles wxWidgets 2.8.12.
- wxLocale::GetSystemEncoding() calls ::GetACP(); codepage 65001 is unhandled in wx 2.8.x → encoding ID -1 → "Cannot convert from the charset 'Unknown encoding (-1)'!" (wx-users thread 2019-09-27; wx PR #1570 adds the mapping in 3.1.2+).
- **Trigger = Windows system locale setting, NOT file paths or DFF metadata.** "Beta: Use Unicode UTF-8 for worldwide language support" enabled → GetACP() = 65001.
- Nondeterministic truncation explained: race between Saracon's audio thread and wx locale init — locale error fires early → truncated output + exit 0 ("Good bye" is the destructor path, always fires); fires late/never → full conversion.
- **LOCAL VERIFICATION: ACP = 65001, OEMCP = 65001 — the UTF-8 beta setting IS enabled on this machine. HIGH confidence root cause.**

**ID3 findings (secondary, defense-in-depth):**
- sacd_extract ID3 sync-safe size bug = real (sacd-ripper #94, PR #99) but FIXED in euflo 0.3.9.3-173 (our build). Trailing "ID3 " chunk is normal sacd_extract behavior; DSDIFF spec says readers MUST skip unknown chunks.
- Saracon "Unknown chunk (ID3 )" warnings = non-fatal, unrelated to charset error (spec-compliant).
- CMPR pad-byte off-by-one ambiguity exists in the wild; chunk walkers should bound by file-remaining-bytes.
- HasId3Chunk exception-masking (silent false) remains a real latent bug worth fixing regardless.

**Consequences for fix plan:**
1. Fix #0 (new): disable UTF-8 beta locale OR confirm via A/B (probe run on a non-65001 codepage). Root cause.
2. Fix #1 filename staging: downgraded from primary to workaround (only matters if user keeps UTF-8 beta).
3. Fix #2 output-size sanity check: still required (defense-in-depth; catches ALL truncation).
4. Fix #3 HasId3Chunk exception logging: still required (latent bug).
5. Probe harness still required: proves fix, becomes regression gate, verifies real Disc 10 run.

## 2026-08-17 Reconciliation

Historical `stripped/` and `raw/` rows were byte-identical, so conclusions derived from comparing those rows are void. Confidence: HIGH, basis: historical journal rows and later streamed stripper evidence.

The earlier UTF-8 / ACP 65001 explanation is rejected as settled root cause. Confidence: HIGH, basis: current real Disc 3/4/5 headless Saracon runs completed through the same machine configuration; no `RegistryOleInit`, charset, or `Unknown encoding (-1)` signature occurred. Preserve historical rows, but do not restate ACP 65001 as confirmed.

Current observed cause classification: Saracon CLI runs are valid with `-c d2p -r 44100 -f wav -n 16bit -d tpdf -g <gain> -T -V all -t <output> <input>`. Output-size validation remains required defense-in-depth. The prior journal's ACP root-cause claims are superseded, not deleted.

## 2026-08-17 Plan residue register

Confidence tags below describe evidence status, not source-code quality.

| Item | Status | Signature / evidence | Owner / rationale |
|---|---|---|---|
| P0.1 second-volume byte copy | BLOCKED | `Get-Volume` exposed only `C:`; no different physical volume | System hardware; cannot fabricate cross-volume equality |
| P0.1 13 canaries | FAIL/BLOCKED | Historical FLAC baseline was deleted; seven requested discs had no preserved canary | User-owned media state; do not invent hashes |
| Historical T11 artifact | BLOCKED | Historical report path absent; current `task-11-report.md` is P1.6 | Audit artifact unavailable; collision note retained |
| P3.3.8 orchestrator guard termination | BLOCKED | Durable harness records no production `PipelineOrchestrator` guard invocation | Checks harness owner; synthetic matrix remains valid but not end-to-end |
| P4.2.5 completion-marker/truncated-output branches | BLOCKED | Real Disc 13/4/5 Saracon normal exits observed; no forced marker-hang or truncated-output fixture run | Runtime contract test owner |
| P4.3.4 artifact ownership fault injection | BLOCKED | Forced probe failure and cleanup-exception paths not exercised | Runtime fault-injection owner |
| P5.3 Gate C | BLOCKED/SKIPPED | User instruction: `Skip gate C`; Disc 5 completed, Disc 6 extraction partial, Discs 7-9 untouched | User-directed skip; no media deletion |
| P5.4 Gate D | BLOCKED | Requires all discs complete; P5.3 skipped and Disc 6 partial | Phase dependency |
| P5.5 Gate E | BLOCKED | Requires active Saracon cancellation run after Phase 5 sequencing; phase halted | Phase dependency and safe media state |

P3.4.7 is no longer blocked: streamed Disc 3 evidence observed `3332711216 -> 3332709410` bytes, exactly 1806 bytes removed, with real Saracon completion. P4.3 T8/T3/T7 and P4.2 real ISO/FLAC/trim evidence are likewise superseded from BLOCKED to observed runtime evidence in the plan addenda.
