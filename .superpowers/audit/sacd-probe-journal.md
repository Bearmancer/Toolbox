# SACD Probe Journal

Append-only debug journal for the Saracon death-loop reproduction and fix work. Spec: docs/superpowers/specs/2026-08-08-sacd-death-loop-repro-design.md

## Runs

| timestamp | case | variant | exit | elapsed | out-bytes | verdict | snippet |
|---|---|---|---|---|---|---|---|

## Findings

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
