---
concern: Pristine (PASC downloader)
status: BLOCKED — source unavailable, plan-only
ref: NONE — see §2
source_docs: [0-foundation.md, 1-types.md, 2-browser.md, 3-services.md, 4-cli-di.md, pristine.md, "pristine-hardening (2).md", Pristine-Refactor-TODO.md, failures_tally.md, 2026-08-19-pristine-overhaul-design.md]
---

# Pristine — Plan

## 1. A blunt warning before anything else

**Every task below is written against a description of code I cannot see.** `git ls-remote` against the real GitHub repo shows only `master`, and `master`'s HEAD is titled "Pre-Pristine adding commit" — the actual `Services/Pristine/*.cs` files were never pushed. Neither Toolbox zip you've given me across this whole session contains a `Pristine` directory. Everything here is reconstructed from plan documents and bug reports that quote file:line locations I can't verify still match.

Do not treat the durations or line references below as measured. Treat them as a starting shape to correct once P1 (below) actually gets you real source.

## 2. Scope

A Playwright-driven downloader for PristineClassical PASC album releases: login (cookie persistence), album resolution by code, playback-triggered track capture, download, artwork/PDF fetch. Six services (`PristineBrowser`, `PristineDownloader`, `PristineLoginService`, `PristineAlbumService`, `PristinePollService`, `PristineOrchestrator`), two CLI commands (`pristine login`, `pristine download`).

## 3. Current state, as described by the corpus (unverified)

The original 22-task build plan (`0-foundation.md` → `4-cli-di.md`, same content as the monolithic `pristine.md`) is reported complete at commit `f84ebec` — a commit that also doesn't exist on GitHub. What shipped was reportedly broken: `failures_tally.md` documents `ErrorOr`-pattern compile errors (assigning `ErrorOr<T>` directly to `T`, returning `null` for a non-nullable `ErrorOr<int?>`), and `Pristine-Refactor-TODO.md` documents a distinct runtime problem — `.WaitAsync(ct)` missing on Playwright calls with no native cancellation support, and several `catch (Exception ex)` blocks that swallow `OperationCanceledException` instead of rethrowing it, so a cancelled run can silently continue rather than stop.

`2026-08-19-pristine-overhaul-design.md` names the on-the-ground symptom: empty `catch{}` blocks swallowing every Playwright step, `auth.json` holding only Shopify cookies with no `pristinestreaming.com` session, a `singleTrack` code path that exists but has no CLI flag to reach it, and a leaked `HttpClient` from `new()` instead of a pooled instance.

The 12-todo hardening plan (`pristine-hardening (2).md`) is the response to all three reports above, structured in four waves plus a final F1–F4 verification wave. Its ledger (`progress.md`) shows the task log empty — either genuinely not started, or started on the branch that's now missing.

## 4. Decision register

**D-1:** This file assumes recovery is possible (F-1 below) and plans the hardening work as if source will reappear. If recovery fails, this becomes a rebuild-from-plan-docs exercise, which is a different and larger scope than what's costed here.

## 5. CPM network

**Project duration: 14.0 h — carries LOW confidence on every duration past P2, since P3 onward is timed against a 12-todo plan I can't cross-check line-by-line.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| P1 | Recover source: push `pristine-port` (or whatever branch/worktree holds it) to GitHub, or export `Services/Pristine` directly | 1.0 | — | 0.0 | 1.0 | 0.0 | 1.0 | **0** |
| P2 | Re-verify all 22 build-plan tasks against recovered source — treat as unconfirmed, not resumed | 0.5 | P1 | 1.0 | 1.5 | 1.0 | 1.5 | **0** |
| P3 | Todo 1–3: harden error paths, config/secret hygiene, cookie normalization | 2.0 | P2 | 1.5 | 3.5 | 1.5 | 3.5 | **0** |
| P4 | Todo 4–6: diagnostics contract, retry isolation (fresh-page on transient failure), state-driven resolver | 3.0 | P3 | 3.5 | 6.5 | 3.5 | 6.5 | **0** |
| P5 | Todo 7–9: 16-bit media gate before download, verifier authority (ffprobe, not extension), truthful CLI exit codes | 3.0 | P4 | 6.5 | 9.5 | 6.5 | 9.5 | **0** |
| P6 | Todo 10: Azure/runtime preflight, redacted | 1.5 | P5 | 9.5 | 11.0 | 9.5 | 11.0 | **0** |
| P7 | Todo 11: single-FLAC + ffprobe live proof | 1.0 | P6 | 11.0 | 12.0 | 11.0 | 12.0 | **0** |
| P8 | Todo 12: full-album ≤5-concurrent + sequential multi-PASC proof | 1.0 | P7 | 12.0 | 13.0 | 12.0 | 13.0 | **0** |
| P9 | F1–F4 final verification wave | 1.0 | P7,P8 | 13.0 | 14.0 | 13.0 | 14.0 | **0** |

Fully sequential, zero float on every task — this is a strict pipeline because each wave in the source plan explicitly blocks the next (diagnostics before retry isolation, stable resolver before media gates, all todos before live QA).

## 6. Task detail — carried from the source plan, not independently derived

**P3 (Todo 1–3):** normalize `__Host-` cookies (`Secure=true`, `Path=/`, no `Domain`, never logged); add explicit `try/catch` with `Telemetry` around every Playwright call currently missing one — `PristinePollService.cs:24` and `:524`, `PristineOrchestrator.cs:100`, `PristineLoginService.cs:18` and `:39`, `PristineBrowser.cs:126` and `:182` per `Pristine-Refactor-TODO.md`. **Line numbers unverified — confirm against recovered source before editing.**

**P5 (Todo 7–9):** select only candidates with explicit 16-bit evidence before downloading; ffprobe every file immediately after; missing ffprobe is a hard failure, never inferred from file extension; CLI exit code is 0 only when every requested album fully succeeds and every file verifies — mixed results exit 1.

**P9 acceptance:** per the source plan's own success criteria — no `!` operator anywhere in `Services/Pristine`, no `catch{}` without `Telemetry`, single PASC produces exactly one verified 16-bit FLAC, full album never exceeds 5 concurrent downloads, two PASC codes run strictly sequentially.

## 7. Out of scope

API reverse-engineering, stream transcoding, album-level parallelism, any new test framework, editing or printing `.env` secrets, deleting `state/auth` or output data. All explicitly excluded in the source hardening plan; carried forward unchanged.

Firefox DevTools MCP live-checks — present in the original build plan as verification steps only, never committed as code. Dropped entirely per this consolidation's instruction; not reinstated even for the recovered-source re-verification in P2.
