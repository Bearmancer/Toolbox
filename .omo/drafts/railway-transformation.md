---
slug: railway-transformation
status: awaiting-approval
intent: clear
pending-action: write .omo/plans/railway-transformation.md
approach: Bottom-up transformation. Prereq types -> Azure leaf services -> YouTube/LastFm internals -> Orchestrators.
---

# Draft: railway-transformation

## Components (topology ledger)
- `Azure Services` | `ErrorOr<string>` pipelines | active | `src/Services/Azure/`
- `LastFmService` | `Option` -> `ErrorOr` + Railway internals | active | `src/Services/LastFm/LastFmService.cs`
- `YouTubeProcessor` | granular `Then` chains | active | `src/Services/Google/YouTube/YouTubePlaylistProcessor.cs`
- `YouTubeTranslation` | `TranslateBatchAsync` -> `ErrorOr` + Railway loop | active | `src/Services/Google/YouTube/YouTubeTranslationService.cs`
- `YouTubeSortService` | Refined Railway inner pass | active | `src/Services/Google/YouTube/YouTubeSortService.cs`
- `YouTubeOrchestrator` | Granular Railway `ExecuteCoreAsync` | active | `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs`

## Open assumptions (announced defaults)
- Loop Preservation | Methods with complex break/continue/pagination/retry logic remain imperative wrappers around Railway inner units | rationale: pure Railway doesn't express break/continue/pagination state naturally | reversible: yes

## Findings (cited - path:lines)
- `YouTubePlaylistOrchestrator.cs:24-33`: `ExecuteCoreAsync` is already Railway but coarse.
- `YouTubePlaylistOrchestrator.cs:191-227`: `ProcessPlaylistsAsync` uses a `for` loop with `ShouldBreak` (RateLimits). Keep imperative.
- `YouTubePlaylistProcessor.cs:110-143`: `BuildVideoListAsync` uses imperative loop for duration lookup. Candidate for Railway.
- `YouTubePlaylistProcessor.cs:145-194`: `MergeCacheAsync` uses imperative loop. Candidate for Railway.
- `YouTubeTranslationService.cs:119-152`: `ExecuteTranslationBatchesAsync` is an imperative `foreach`. Needs `TranslateService.TranslateBatchAsync` to return `ErrorOr` first.
- `LastFmService.cs:25-30`: `Option<T>` is a custom type duplicating `ErrorOr`. Candidate for replacement.
- `Azure/*.cs`: Services use `Task<string>` and throw exceptions. All candidates for `ErrorOr<string>` pipelines.

## Decisions (with rationale)
1. **Prerequisite Type Update**: Update `TranslateService.TranslateBatchAsync` return type to `ErrorOr` to enable Railway in `YouTubeTranslationService`.
2. **Option Retirement**: Retire `Option<T>` in `LastFmService` in favor of `ErrorOr<T>` for codebase consistency.
3. **Loop Strategy**: Maintain imperative wrappers for pagination (`LastFm`), retries (`LastFm`), and rate-limit breaks (`YouTubeOrchestrator`) to avoid over-engineering.
4. **Azure Standardization**: All Azure service methods will return `ErrorOr<string>`, converting internal exceptions to typed `Errors`.

## Scope IN
- Conversion of identified methods to `Then` / `ThenAsync` chains.
- Return type updates to `ErrorOr<T>`.
- Cleanup of `Option<T>`.
- Integration of `Telemetry` as side-effects within Railway blocks.

## Scope OUT (Must NOT have)
- Implementation of a full-blown Monad library (stick to existing `ErrorOr` extension methods).
- Changing the public API of the CLI commands.
- Adding new NuGet packages.

## Open questions
None. All forks resolved via best-practice defaults.

## Approval gate
status: awaiting-approval
