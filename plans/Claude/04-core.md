---
concern: Core (cross-cutting — Telemetry, Errors, dead code)
status: active
ref: github.com/Bearmancer/Toolbox @ fe6e322d (master)
source_docs: [dead-code-catalog.md, error-taxonomy.md, telemetry-spec.md, god-audit-spec.md]
---

# Core — Plan

## 1. Scope

`src/Core`: `Telemetry.cs`, `Errors.cs`, `Text.cs`, `ServiceName.cs`, `PathResolver.cs`. Deliberately excludes anything service-specific — Audio's and LastFm's ErrorOr work lives in their own files even though the pattern is the same, because the fix locations don't overlap.

## 2. Current state — several claims already resolved, verified directly

Re-checked every dead-code claim in the corpus against real source rather than trusting the prior "Caveman Ultra" passes:

| Claim | Verified |
|---|---|
| `IsSeqReachableAsync` TCP probe should be cut | **Already gone.** Zero hits anywhere, not even a definition. |
| `LogPaths` custom formatter should become a Serilog enricher | **Already done.** File doesn't exist. |
| `Serilog.Sinks.Console` orphan package version | **Already gone** from `Directory.Packages.props`. |
| `PathValidator.ValidateOutputDirectory` unused | **Already deleted** — zero hits. |
| `Text.Has` / `Text.StartsWith` unused | **Confirmed still true.** One hit each — the definition only. |
| SSH.NET declared in 5+ projects, used in 1 | **Partially resolved** — now only in `Google.csproj`, not centralized to CLI as one earlier plan proposed, but no longer sprawled either. |

So roughly half of what the dead-code catalogs describe has already landed, likely via the M1–M4 mega-plan commit (`0451bf4`). What remains is smaller than the corpus suggests.

## 3. Findings

### F-1 — `Text.Has` / `Text.StartsWith` genuinely dead `[LOW] [HIGH]`

Confirmed by direct grep, not inherited. One reference each, both the definitions.

### F-2 — `Errors.cs` has 34 factories; unaudited for zero-producer/zero-consumer pairs `[MEDIUM] [MEDIUM]`

Prior passes (`error-taxonomy.md`) named specific dead codes (`YT.PlaylistNotFound`, `Azure.ServiceUnavailable`) — but those are YouTube-scoped and out of this consolidation. The remaining ~25 non-YouTube factories haven't been individually re-audited against this source snapshot. This is real work, not a known-answer task.

### F-3 — `LastFmApiException` will need `Errors.LastFm.*` additions `[LOW] [HIGH]`

Direct dependency from `02-lastfm.md` L1 — noting here so Core and LastFm don't land the same factory twice on parallel branches.

## 4. CPM network

**Project duration: 3.0 h.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| C3 | Audit 34 `Errors.cs` factories for zero-producer/zero-consumer pairs | 1.0 | — | 0.0 | 1.0 | 0.0 | 1.0 | **0** |
| C1 | Repo-wide grep confirm: `Text.Has`/`StartsWith`/`IsEqualTo` call sites | 0.5 | — | 0.0 | 0.5 | 1.0 | 1.5 | 1.0 |
| C5 | Confirm `LogPaths`→enricher migration introduced no regressions | 1.0 | — | 0.0 | 1.0 | 1.5 | 2.5 | 1.5 |
| C4 | Delete confirmed-dead factories; fix confirmed logical-error mappers | 1.5 | C3 | 1.0 | 2.5 | 1.0 | 2.5 | **0** |
| C2 | Delete unreferenced `Text` extensions per C1's result | 1.0 | C1 | 0.5 | 1.5 | 1.5 | 2.5 | 1.0 |
| C6 | Build gate | 0.5 | C2,C4,C5 | 2.5 | 3.0 | 2.5 | 3.0 | **0** |

Critical path: `C3 → C4 → C6`.

## 5. Out of scope

Any Telemetry sink-count reduction — the "10 sinks for 8 empty files" framing in the prior corpus already resolved itself: empty files mean missing `ForService` calls at the caller, not architecture to cut, and this consolidation isn't re-litigating that call.
