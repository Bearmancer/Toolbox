# Overengineering vs SRP vs SoC — Master Verdict

**Source:** `toolbox-consolidated-spec.md` §7
**Constraint:** solo dev, not enterprise. Single interface + single impl with no test seam = overhead. Clean code + architectural clarity paramount.
**Verified:** 2026-08-18 | Live: `src/Services/Google/YouTube` (12 files), `src/Services/Audio` (18 files), `src/Core` (6 files), `src/CLI` (~22 files)

| Bucket | Verdict | Examples | Rationale |
|--------|---------|----------|-----------|
| True overengineering — CUT | delete/shrink | dead harness, 13 dead factories, 5 dead result fields, 84 LOC repeated `Result.Match`, 10 sinks for 8 empty files | dead, duped, or speculative. No second consumer/caller/behavior |
| Legit SRP — KEEP | keep split | `PipelineOrch` vs `DsdConvert` (merging→900 LOC god); `Saracon/Sox/SacdExtract` (distinct binaries); LIS sort (quota); `MergePolicy` (destructive rules) | merging creates gods or collapses distinct external contracts |
| Correct SoC, wrong layer — MOVE | relocate | Dashboard 508 LOC in CLI → Services; `OciConfig` in Core → CLI/Dashboard env | separation correct, location violates layering |
| Adapter necessity — KEEP | keep | 3 `EventListener`s (incompatible SDKs); `Google→Azure` direct dep | generic façade hides, not reduces |
| Gray — lean keep | keep until 2nd consumer | `YouTubeChangeDetector` (62 LOC 1 caller), 4 command modules, `FlacChecker` statics | YAGNI says inline; SRP says testable. Inline only if <60 LOC AND untested |

## When Is It Overengineering? (Solo-Dev Heuristic)

1. Single interface, single impl, no test seam → delete interface.
2. Wrapper adding 0 behavior (`Text.IsEqualTo` over `string.Equals`) → inline.
3. File-per-trivial-thing (`DiscState` 10 LOC enum, `PathValidator` 25 LOC 0 callers) → merge/delete.
4. Speculative generality (`ProcessRunner` `TerminationReason` branches for 1 path) → cut to used.
5. Copy-paste abstraction (5× identical `TextAnalytics` guard) → *overengineering by duplication*; extract shared runner.
6. **NOT** overengineering: distinct binary wrappers (`Saracon ≠ Sox ≠ SacdExtract`), quota-critical algos (LIS sort), destructive policy separation (`MergePolicy`), SDK adapter necessity (3 `EventListener`s).

## Telemetry Verdict

| Pattern | Verdict | Rationale |
|---------|---------|-----------|
| 10 per-service JSONL loggers | **KEEP** (fix, not cut) | routing correct; empty files = missing callers not bad architecture |
| `Seq` TCP probe | **CUT** | native Serilog retry sufficient |
| `LogPaths` custom formatter | **CUT** | Serilog Enricher does it |
| 5 one-line `Telemetry` wrappers | **SHRINK** | one `Telemetry.Log(ServiceName, level, template)` |

## God Files (Reconciled to Live LOC)

| File | LOC | Verdict |
|------|-----|---------|
| `PipelineOrchestrator.cs` | ~461 | keep — 1 job, borderline not true god |
| `YouTubeDuplicateMerger.cs` | ~386 | keep — destructive workflow density |
| `DsdConvertService.cs` | ~410 | shrink — extract `DffHeaderReader` → ~340 |
| `YouTubeSortService.cs` | ~369 | keep — LIS algorithmic density |
| `YouTubePlaylistOrchestrator.cs` | ~363 | split — 4 entry points → 2 |
| `YouTubeSyncProcessor.cs` | ~332 | split — completes layer god with orchestrator |
| `SaraconService.cs` | ~329 | shrink — drop retry branches |
| `ProcessRunner.cs` | ~336 | shrink — drop grace-kill branches → ~240 |
| `DashboardHtmlGenerator.cs` | ~364 | move — keep hand-roll, relocate to Services |
| `TextAnalyticsService.cs` | ~255 | shrink — 5× cloned guard/catch → central runner → ~160 |

God modules: `Services/Audio` file-count god (18 files); `Services/Google` layer god (dedup to 1 layer); `CLI` layer violation — move 508 LOC to Services; `Core` `Telemetry` god (10 sinks, shrink).

## Ceilings

Mark deliberate ceiling shortcuts with `ponytail:` comment (`global lock, per-account locks if throughput matters`).
