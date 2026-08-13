# Tuple Usage Report — Toolbox `src/`

**Generated:** 2026-08-14 | **Scope:** all `.cs` files under `src/` | **Branch:** main

## 1. Tuple Declarations

| File | Line | Variable(s) | Named/Unnamed | Element Types |
|---|---|---|---|---|
| `src/Services/Google/YouTube/YouTubePlaylistService.cs` | 27 | `items`, `nextPageToken` (deconstruction of `fetch` result) | Named (from delegate type) | `IList<T>`, `string?` |
| `src/Services/Google/YouTube/YouTubeSyncProcessor.cs` | 179 | `sorted`, `consumed`, `distinctMoved` (`var` deconstruction) | Named (from return type) | `bool`, `int`, `int` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 56 | `videoIndex`, `video` (foreach deconstruction) | Unnamed | `int`, `YouTubeVideo` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 159 | `target`, `transliterated` (foreach deconstruction) | Unnamed | `TranslationTarget`, `string` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 206 | `target`, `result` (foreach deconstruction) | Unnamed | `TranslationTarget`, `TranslationResult` |
| `src/Services/Google/YouTube/YouTubeSortService.cs` | 185 | `(item.Id, idx)` (LINQ projection) | Unnamed (inferred names `Id`, `idx`) | `string`, `int` |
| `src/Services/Audio/PipelineOrchestrator.cs` | 248 | `primary`, `derived` (typed deconstruction) | Named (from return type) | `DsdConversionSettings`, `DsdConversionSettings?` |
| `src/CLI/Audio/DsdConvertCommand.cs` | 90 | `primary`, `derived` (`var` deconstruction) | Named (from return type) | `DsdConversionSettings`, `DsdConversionSettings?` |

## 2. Tuple Return Types

| Method | File | Return Type | Element Names |
|---|---|---|---|
| `DsdConversionSettings.ForDsdRate(int, AudioOutputFormat, double)` | `src/Services/Audio/AudioModels.cs:27` | `(DsdConversionSettings Primary, DsdConversionSettings? Derived)` | `Primary`, `Derived` |
| `YouTubeSyncProcessor.SortSinglePlaylistAsync(...)` | `src/Services/Google/YouTube/YouTubeSyncProcessor.cs:215` | `Task<(bool Sorted, int WritesConsumed, int DistinctItemsMoved)>` | `Sorted`, `WritesConsumed`, `DistinctItemsMoved` |
| `YouTubePlaylistService.PaginateAsync<T>` fetch delegate parameter | `src/Services/Google/YouTube/YouTubePlaylistService.cs:13` | `Func<string?, Task<(IList<T> Items, string? NextPageToken)>>` | `Items`, `NextPageToken` |

## 3. Tuple Deconstruction Sites

| File | Line | Code | Source |
|---|---|---|---|
| `src/Services/Google/YouTube/YouTubePlaylistService.cs` | 27 | `(IList<T> items, string? nextPageToken) = await fetch(pageToken);` | `PaginateAsync` fetch delegate |
| `src/Services/Google/YouTube/YouTubeSyncProcessor.cs` | 179 | `var (sorted, consumed, distinctMoved) = await SortSinglePlaylistAsync(...)` | `SortSinglePlaylistAsync` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 56 | `(var videoIndex, YouTubeVideo video) in videos.Select((video, index) => (index, video))` | LINQ `Select` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 159 | `var (target, transliterated) in transliterateTargets.Zip(transliterateResult.Value)` | LINQ `Zip` |
| `src/Services/Google/YouTube/YouTubeTranslationService.cs` | 206 | `foreach ((TranslationTarget target, TranslationResult result) in results)` | `BatchApiResult` list |
| `src/Services/Audio/PipelineOrchestrator.cs` | 248 | `(DsdConversionSettings primary, DsdConversionSettings? derived) = DsdConversionSettings.ForDsdRate(...)` | `ForDsdRate` |
| `src/CLI/Audio/DsdConvertCommand.cs` | 90 | `var (primary, derived) = DsdConversionSettings.ForDsdRate(...)` | `ForDsdRate` |

## 4. Tuple Usage Patterns

1. **Multi-value method returns** — `ForDsdRate` (primary + optional derived settings), `SortSinglePlaylistAsync` (sorted flag + write counters). Both named tuples.
2. **Pagination callback** — `PaginateAsync` fetch delegate returns `(items, nextPageToken)`; lambdas at `YouTubePlaylistService.cs:82, 113, 157` return unnamed literals matched to the named delegate type.
3. **LINQ projections** — `Select((video, index) => (index, video))` (`YouTubeTranslationService.cs:57`), `Select((item, idx) => (item.Id, idx))` (`YouTubeSortService.cs:185`).
4. **Zip pairing** — `Zip(transliterateResult.Value)` produces `(target, transliterated)` pairs (`YouTubeTranslationService.cs:159`).
5. **foreach deconstruction** — iterating tuple sequences (`YouTubeTranslationService.cs:56, 159, 206`).

## 5. Potential Issues

1. **Inconsistent deconstruction style for same method** — `ForDsdRate` deconstructed with `var` in `DsdConvertCommand.cs:90` but explicit types in `PipelineOrchestrator.cs:248`. Minor; types are obvious either way.
2. **Mixed-case inferred element names** — `YouTubeSortService.cs:185-186`: `(item.Id, idx)` → `ToDictionary(x => x.Id, x => x.idx)` relies on inferred names (`Id` PascalCase, `idx` camelCase). Fragile: changing the projection breaks the dictionary lookup silently.
3. **Unnecessary materialization** — `YouTubeTranslationService.cs:57-58`: `.Select(...).ToList()` before `foreach`; deferred LINQ would suffice. Minor perf, not tuple-specific.
4. **Unnamed tuples in `PaginateAsync` lambdas** (`YouTubePlaylistService.cs:82, 113, 157`) — element names come from the target delegate type; correct, but a reader must check the delegate signature to know element meaning.
5. **No explicit `Tuple<>`/`ValueTuple<>` usage, no `.Item1`/`.Item2` access, no custom `Deconstruct` methods** — all tuples are C# 7+ syntax tuples (ValueTuple). No heap allocation concerns.
6. **Naming convention consistent** — all named tuple elements PascalCase (`Primary`, `Derived`, `Sorted`, `WritesConsumed`, `DistinctItemsMoved`, `Items`, `NextPageToken`); deconstruction variables camelCase. Consistent with codebase conventions.
