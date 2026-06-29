## TL;DR (For humans)
Refactor exception-based service methods into Railway pipelines using `ErrorOr`. Propagation stops at the CLI boundary via `.Match()` — the CLI remains a thin translation layer with zero business logic. Imperative loops (pagination, retries, rate-limit breaks) are preserved. Explicit test files are foregone by design — verification is `dotnet build` plus CLI-based manual invocation.

## Design Principles
- **SRP**: CLI is a pure terminal — `.Match(success, error)` only. No error-code inspection, no conditional logic, no domain knowledge.
- **Propagation termination**: Railway flows `Service → Orchestrator → CLI`. CLI is the sink; it converts `ErrorOr<T>` to `int` (exit code).
- **Loop preservation**: Pagination, retry, and rate-limit break loops remain imperative. Railway is for business pipelines, not control flow.
- **No dogma**: Pure computations (LIS, sort plans), cross-cutting retries, and inherently stateful loops stay as-is. Only transform chains that benefit from pipeline composition.

## Todos

### Batch 1: Prerequisites (The Foundation)
1. [x] Update `TranslateService.TranslateBatchAsync` to return `ErrorOr<IReadOnlyList<TranslationResult>>`
2. [x] Update `YouTubeVideoService.GetVideoDurationsAsync` to return `ErrorOr<Dictionary<string, TimeSpan>>`
3. [x] Retire `Option<T>` in `LastFmService` and replace with `ErrorOr<T>`
4. [x] Refactor `SyncCounters` from mutable class to immutable `record` with `WithResult(ProcessResult)` returning new instance
5. [x] Define error factories in `Errors.cs` for `DocIntel`, `Speech`, `Vision`, `OpenAI`, and `Translate`

### Batch 2: Azure Service Transformation
6. [ ] Railwayize `VisionService.AnalyzeAsync`
7. [ ] Railwayize `TextAnalyticsService.SentimentAsync`
8. [ ] Railwayize `TextAnalyticsService.PiiAsync`
9. [ ] Railwayize `TextAnalyticsService.EntitiesAsync`
10. [ ] Railwayize `TextAnalyticsService.KeyPhrasesAsync`
11. [ ] Railwayize `TextAnalyticsService.DetectLanguageAsync`
12. [ ] Railwayize `OpenAiService.ChatAsync`
13. [ ] Railwayize `SpeechSttService.TranscribeAsync`
14. [ ] Railwayize `DocIntelService.AnalyzeAsync`
15. [ ] Railwayize `SpeechTtsService.SynthesizeAsync`

### Batch 2.5: Thin CLI `.Match()` Unwrapping
16. [ ] Update `VisionCommand` — add `.Match()` terminal
17. [ ] Update `TranslateCommand` — add `.Match()` terminal
18. [ ] Update `SpeechSttCommand` — add `.Match()` terminal
19. [ ] Update `NerCommand` — add `.Match()` terminal
20. [ ] Update `PhrasesCommand` — add `.Match()` terminal
21. [ ] Update `DocIntelCommand` — add `.Match()` terminal

### Batch 3: YouTube/LastFm Internals
22. [ ] Railwayize `YouTubePlaylistProcessor.BuildVideoListAsync`
23. [ ] Railwayize `YouTubePlaylistProcessor.MergeCacheAsync` — **fix unsafe `.Value` access in callers**
24. [ ] Railwayize `YouTubeTranslationService.ExecuteTranslationBatchesAsync` (error accumulation strategy)
25. [ ] Railwayize `LastFmService.TryExtractTrack`

### Batch 4: Orchestration Layer
26. [ ] Refine `YouTubeSortService.SortPlaylistAsync` inner pass
27. [ ] Railwayize `YouTubePlaylistOrchestrator.ProcessSinglePlaylistAsync`
28. [ ] Granularize `YouTubePlaylistOrchestrator.ExecuteCoreAsync`

## Task Details

### Batch 1: Prerequisites
1. [ ] **Update TranslateService.TranslateBatchAsync**
  - **Goal**: Change return type to `ErrorOr<IReadOnlyList<TranslationResult>>`. Wrap exceptions in `Errors.Translate.ApiError`.
  - **Files**: `src/Services/Azure/TranslateService.cs`
  - **Acceptance**: Method returns `ErrorOr`. Downstream callers in `YouTubeTranslationService` updated to use `.Then()`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): return ErrorOr from TranslateBatchAsync`

2. [ ] **Update YouTubeVideoService.GetVideoDurationsAsync**
  - **Goal**: Change return type to `ErrorOr<Dictionary<string, TimeSpan>>`. Wrap `FormatException` in `Errors.YouTube.ApiError`.
  - **Files**: `src/Services/Google/YouTube/YouTubeVideoService.cs`
  - **Acceptance**: Method returns `ErrorOr`. Callers in `YouTubePlaylistProcessor` updated.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): return ErrorOr from GetVideoDurationsAsync`

3. [ ] **Retire Option<T> in LastFmService**
  - **Goal**: Remove custom `Option<T>` type. Replace with `ErrorOr<T>` for consistency.
  - **Files**: `src/Services/LastFm/LastFmService.cs`
  - **Acceptance**: `Option<T>` definition deleted. `TryExtractTrack` returns `ErrorOr<LastFmScrobble>`. Callers updated.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(lastfm): replace Option with ErrorOr`

4. [ ] **Refactor SyncCounters to immutable record**
  - **Goal**: Convert `class SyncCounters` (lines 408-439) to `record SyncCountersRecord` with a `WithResult(ProcessResult)` method returning a new instance. Replace all `UpdateFrom()` calls with `counters = counters.WithResult(result)`.
  - **Files**: `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs`
  - **Acceptance**: No mutable property setters remain. `ProcessPlaylistsAsync` uses immutable update pattern.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): make SyncCounters an immutable record`

5. [ ] **Define error factories in Errors.cs**
  - **Goal**: Add `Errors.DocIntel`, `Errors.Speech`, `Errors.Vision`, `Errors.OpenAI`, `Errors.Translate` with `ApiError(string message)` factories following existing `Errors.YouTube` / `Errors.Azure` / `Errors.LastFm` conventions.
  - **Files**: `src/Core/Errors.cs`
  - **Acceptance**: All five error classes available with `.ApiError()`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(core): add error factories for remaining Azure services`

### Batch 2: Azure Service Transformation
*Standard pattern for all Batch 2 methods:* `ValidateInput` → `CallApi` → `FormatResult`. Exceptions map to `Errors.[Service].ApiError`. Telemetry wraps the entire Railway chain in `using var _ = Telemetry.ForService(...);`.

6. [ ] **Railwayize VisionService.AnalyzeAsync**
  - **Files**: `src/Services/Azure/VisionService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. No `try-catch` in main method.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize VisionService.AnalyzeAsync`

7. [ ] **Railwayize TextAnalyticsService.SentimentAsync**
  - **Files**: `src/Services/Azure/TextAnalyticsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. `ArgumentOutOfRangeException` → `Errors.Validation.InvalidInput`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize TextAnalyticsService.SentimentAsync`

8. [ ] **Railwayize TextAnalyticsService.PiiAsync**
  - **Files**: `src/Services/Azure/TextAnalyticsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. Invalid domain → `Errors.Validation.InvalidInput`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize TextAnalyticsService.PiiAsync`

9. [ ] **Railwayize TextAnalyticsService.EntitiesAsync**
  - **Files**: `src/Services/Azure/TextAnalyticsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize TextAnalyticsService.EntitiesAsync`

10. [ ] **Railwayize TextAnalyticsService.KeyPhrasesAsync**
  - **Files**: `src/Services/Azure/TextAnalyticsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize TextAnalyticsService.KeyPhrasesAsync`

11. [ ] **Railwayize TextAnalyticsService.DetectLanguageAsync**
  - **Files**: `src/Services/Azure/TextAnalyticsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize TextAnalyticsService.DetectLanguageAsync`

12. [ ] **Railwayize OpenAiService.ChatAsync**
  - **Files**: `src/Services/Azure/OpenAiService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. `InvalidOperationException` (no deployment) → `Errors.OpenAI.ApiError`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize OpenAiService.ChatAsync`

13. [ ] **Railwayize SpeechSttService.TranscribeAsync**
  - **Files**: `src/Services/Azure/SpeechSttService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. Temp file cleanup preserved in `finally` block.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize SpeechSttService.TranscribeAsync`

14. [ ] **Railwayize DocIntelService.AnalyzeAsync**
  - **Files**: `src/Services/Azure/DocIntelService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`. `InvalidOperationException` → `Errors.DocIntel.ApiError`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize DocIntelService.AnalyzeAsync`

15. [ ] **Railwayize SpeechTtsService.SynthesizeAsync**
  - **Files**: `src/Services/Azure/SpeechTtsService.cs`
  - **Acceptance**: Returns `ErrorOr<string>`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(azure): railwayize SpeechTtsService.SynthesizeAsync`

### Batch 2.5: Thin CLI `.Match()` Unwrapping
*Standard pattern for all CLI commands:* Call the service's `ErrorOr<T>` method, terminate via `.Match()`. Zero business logic. Zero error-code inspection.

```csharp
return await service.MethodAsync(args, ct)
    .Match(
        success => { Console.WriteLine(success); return 0; },
        error   => { Console.Error.WriteLine(error.Description); return 1; }
    );
```

16. [ ] **Update VisionCommand**
  - **Files**: `src/CLI/Azure/VisionCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in VisionCommand`

17. [ ] **Update TranslateCommand**
  - **Files**: `src/CLI/Azure/TranslateCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in TranslateCommand`

18. [ ] **Update SpeechSttCommand**
  - **Files**: `src/CLI/Azure/SpeechSttCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in SpeechSttCommand`

19. [ ] **Update NerCommand**
  - **Files**: `src/CLI/Azure/NerCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in NerCommand`

20. [ ] **Update PhrasesCommand**
  - **Files**: `src/CLI/Azure/PhrasesCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in PhrasesCommand`

21. [ ] **Update DocIntelCommand**
  - **Files**: `src/CLI/Azure/DocIntelCommand.cs`
  - **QA**: `dotnet build`
  - **Commit**: `refactor(cli): unwrap ErrorOr in DocIntelCommand`

### Batch 3: YouTube/LastFm Internals
22. [ ] **Railwayize YouTubePlaylistProcessor.BuildVideoListAsync**
  - **Goal**: Convert to `ExtractVideoIds` → `GetVideoDurationsAsync` → `BuildVideoListFromDurations`.
  - **Files**: `src/Services/Google/YouTube/YouTubePlaylistProcessor.cs`
  - **Acceptance**: Returns `ErrorOr<(List<YouTubeVideo>, int)>`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): railwayize BuildVideoListAsync`

23. [ ] **Railwayize YouTubePlaylistProcessor.MergeCacheAsync**
  - **Goal**: Convert to `LoadExistingVideosAsync` → `MergeWithIncoming`. **CRITICAL: Fix unsafe `.Value` access at lines 38 and 81** — replace `(await MergeCacheAsync(...)).Value` with `.Then(videos => new MergeResult(videos, ...))` to propagate errors safely.
  - **Files**: `src/Services/Google/YouTube/YouTubePlaylistProcessor.cs`
  - **Acceptance**: Returns `ErrorOr<List<YouTubeVideo>>`. No `.Value` access without prior `.IsError` check anywhere in the file.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): railwayize MergeCacheAsync and fix unsafe Value access`

24. [ ] **Railwayize YouTubeTranslationService.ExecuteTranslationBatchesAsync**
  - **Goal**: After Task 1 makes `TranslateBatchAsync` return `ErrorOr`, convert the batch loop to a Railway process. Use error accumulation: capture individual batch failures, continue processing remaining batches, return accumulated results.
  - **Files**: `src/Services/Google/YouTube/YouTubeTranslationService.cs`
  - **Acceptance**: Returns `ErrorOr<List<List<BatchApiResult>>>`. Failed batches don't halt the loop.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): railwayize ExecuteTranslationBatchesAsync`

25. [ ] **Railwayize LastFmService.TryExtractTrack**
  - **Goal**: Replace `Option<T>` with `ErrorOr<T>`.
  - **Files**: `src/Services/LastFm/LastFmService.cs`
  - **Acceptance**: Returns `ErrorOr<LastFmScrobble>`. Callers in `ExtractTracks` updated.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(lastfm): railwayize TryExtractTrack`

### Batch 4: Orchestration Layer
26. [ ] **Refine YouTubeSortService.SortPlaylistAsync inner pass**
  - **Goal**: Ensure the inner `FetchPlaylistItemsAsync` → `ComputeSortPlan` → `ExecuteSortPlanAsync` chain propagates `ErrorOr` cleanly. The outer multi-pass loop remains imperative.
  - **Files**: `src/Services/Google/YouTube/YouTubeSortService.cs`
  - **Acceptance**: Inner Railway chain is polished; outer `for (pass = 0; pass < 3; pass++)` loop unchanged.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): polish SortPlaylistAsync inner Railway chain`

27. [ ] **Railwayize YouTubePlaylistOrchestrator.ProcessSinglePlaylistAsync**
  - **Goal**: Convert to `ProcessPlaylistAsync` → `MapRateLimitErrors` → `SaveIncrementalState`. Use `OnErrorFirst` for error code classification. The imperative `for` loop in `ProcessPlaylistsAsync` (the caller) is preserved.
  - **Files**: `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs`
  - **Acceptance**: Returns `ErrorOr<ProcessResult>`. Error classification uses `.OnErrorFirst()`.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): railwayize ProcessSinglePlaylistAsync`

28. [ ] **Granularize YouTubePlaylistOrchestrator.ExecuteCoreAsync**
  - **Goal**: Expand the current high-level Railway chain into discrete, named steps: `LoadStoredState` → `FetchCurrentSummaries` → `DetectChanges` → `ArchiveDeleted` → `BuildToProcessList` → `ProcessPlaylists` → `BuildFinalState` → `SaveState` → `LogSummary`.
  - **Files**: `src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs`
  - **Acceptance**: Top-level `ExecuteCoreAsync` is a single clean `ErrorOr<SyncOutcome>` chain with each step as a private helper.
  - **QA**: `dotnet build`
  - **Commit**: `refactor(youtube): granularize ExecuteCoreAsync Railway chain`

## Verification Strategy
- **Primary**: `dotnet build` after every task. Change one file → build → verify clean. No scattershot multi-file edits before building.
- **Manual**: Exercise CLI commands (`dotnet run -- vision ...`, `dotnet run -- translate ...`) to confirm output format and error handling.
- **No standalone test files**: Foregone by design per AGENTS.md rule 4.

## Scoped Out (Must NOT Have)
- No Railway wrapping of `LongestIncreasingSubsequence` (pure algorithm)
- No Railway wrapping of `ComputeSortPlan` (pure transform)
- No Railway wrapping of `BuildTranslationBatches` (pure batching)
- No Railway wrapping of `ApplyTranslationResults` (pure mutation of local list)
- No Railway-rewrite of pagination loops (`LastFmService.FetchRecentTracksAsync`)
- No Railway-rewrite of retry loops (`LastFmService.FetchPageAsync`, `FetchPageAsync` retry)
- No Railway-rewrite of rate-limit break loops (`YouTubePlaylistOrchestrator.ProcessPlaylistsAsync`)
- No test NuGet packages
- No new CLI commands or CLI-level business logic
- No database, no ORM, no external config format changes
