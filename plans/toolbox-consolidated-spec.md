# Toolbox Consolidated Spec Sheet — Caveman Ultra

> **Source:** [`toolbox-spec.md`](file:///C:/Users/Lance/Dev/Toolbox/toolbox-spec.md) (890 lines, 13 prompt sections)
> **Generated:** 2026-08-17 via 5 parallel spec-consolidator subagents
> **Domain specs:** [`youtube-seams-spec.md`](file:///C:/Users/Lance/.gemini/antigravity-cli/brain/93b092b1-47d9-4967-93e9-11f0d64edfbb/youtube-seams-spec.md) · [`youtube-architecture-spec.md`](file:///C:/Users/Lance/.gemini/antigravity-cli/brain/93b092b1-47d9-4967-93e9-11f0d64edfbb/youtube-architecture-spec.md) · [`telemetry-spec.md`](file:///C:/Users/Lance/.gemini/antigravity-cli/brain/93b092b1-47d9-4967-93e9-11f0d64edfbb/telemetry-spec.md) · [`audio-cli-spec.md`](file:///C:/Users/Lance/.gemini/antigravity-cli/brain/93b092b1-47d9-4967-93e9-11f0d64edfbb/audio-cli-spec.md) · [`god-audit-spec.md`](file:///C:/Users/Lance/.gemini/antigravity-cli/brain/93b092b1-47d9-4967-93e9-11f0d64edfbb/god-audit-spec.md)

---

## 0. Executive Summary

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| LOC removable | — | — | **-820 to -960** |
| Files | 79 | 73 | -6 |
| Package deps | — | — | -3 scopes |
| God files | 4 | 0 | largest 474→~340 |
| God modules | Audio 21 files, Google 2-layer | 15 files, 1 layer | -6 files, -1 layer |

**Principle:** delete > shrink > move > keep. Every keep justified.

---

## 1. YouTube Data Seams — Produced ↔ Consumed Gap Map

### 1.1 Sort/Plan Seams (N-01 to N-05)

| ID | Data | Verdict | Evidence |
|----|------|---------|----------|
| N-01 | SortResult.LastSortCompleted | **consumed** | SortService:406→SyncProcessor:271. Resume works. Repositioned vs DistinctItemsMoved = dupe metric |
| N-02 | SortPlan TotalItems/LisSize | **unconsumed** | Produced ComputeSortPlan. Logged :225. Dead outside log. Drop field or keep for dashboard |
| N-03 | SortPassResult Failures | **logical-error** | Produced→logged→collapsed to ApiError string. Count lost. Missing aggregate |
| N-04 | PlaylistUpdate Item+NewPosition | **consumed** | SortService:219→:268. No gap |
| N-05 | Translation DetectedLanguage hi | **logical-error** | Transliterate success counts as translated en. Over-count bug |

### 1.2 Dashboard/Sort/Video/Merge Seams (S-09 to S-17)

| ID | Data | Verdict | Evidence |
|----|------|---------|----------|
| S-09 | SyncResult.SkippedVideos | **unconsumed** | Logged Finalize. Never read CLI. Dead aggregate |
| S-10 | ChangeDetectionResult.UnchangedPlaylists | **unconsumed** | Dead list. Never iterated outside detector |
| S-11 | SortStatistics Attempted/Modified | **logical-error** | Produced→logged→discarded. CLI never sees. Missing output |
| S-12 | SyncResult.TotalVideos vs ProcessedIds.Count | **unused** | Dupe metric. Keep one |
| S-13 | DashboardService.reverseLookup TryAdd | **logical-error** | Sanitized Title collides. Data drops. Fix: key by PlaylistId |
| S-14 | YouTubeVideo.Description | **consumed** | Display off. Search on. 1.5MB bloat. Keep for search |
| S-15 | YouTubeFetchState top-level LastChecked/LastUpdated | **unconsumed** | Written 5 places. Never read. Dead or missing throttle |
| S-16 | PlaylistSnapshot.LastChecked | **unconsumed** | PlaylistService:203. Never read. ETag+count gates cache |
| S-17 | HmsTimeSpanConverter scope | **unused** | Merger isolated JsonOptions. Reuse FetchState.JsonOptions |

### 1.3 Fresh Unconsumed Seams (S-01 to S-08)

| ID | Data | Verdict |
|----|------|---------|
| S-01 | TranslatedTitle / DetectedLanguage | unconsumed — dashboard drops language |
| S-02 | YouTubeVideo.Description payload | consumed — search needs it, display off, 1.5MB bloat |
| S-03 | YouTubeVideo.Duration | overengineering — stored hh:mm:ss, reformatted same string |
| S-04 | PlaylistSnapshot.ReportedVideoCount | consumed — detector logs delta |
| S-05 | SortStatistics | logical-error — duplicate S-11 |
| S-06 | SyncResult TotalVideos/SkippedVideos | unconsumed — duplicate S-09/S-12 |
| S-07 | ChangeDetectionResult UnchangedPlaylists | unconsumed — duplicate S-10 |
| S-08 | DashboardData PlaylistCount/VideoCount | unconsumed — scaffolding dead |

---

## 2. Speculative Taxonomy — YT.RateLimit Unreachable

### 2.1 Error Code Producer→Consumer Map

| Code | Consumer | Producer | Status |
|------|----------|----------|--------|
| YT.RateLimit | SyncProcessor:79 ✓ | **0 producers** | **logical-error** — fix producer mappers |
| YT.QuotaExceeded | SyncProcessor:79 ✗ (not checked) | SortService:312 ✓ | **logical-error** — fix consumer check |
| YT.PlaylistNotFound | **0 consumers** | **0 producers** | **dead** — delete |
| YT.VideoNotFound | **0 consumers** | **0 producers** | **dead** — delete |
| Azure.AuthFailed | SyncProcessor:88 ✓ | **0 producers** | **logical-error** — map at Translate boundary |
| Azure.RateLimit | SyncProcessor:79 ✓ | **0 producers** | **logical-error** — map at Translate boundary |
| Azure.ServiceUnavailable | **0 consumers** | **0 producers** | **dead** — delete |

### 2.2 Fix Spec

- PlaylistService.Delete/Insert/Fetch: catch `GoogleApiException` → typed mapper (429→RateLimit, 403+quota→QuotaExceeded, 404→PlaylistNotFound, else→ApiError)
- SyncProcessor:79: add `"YT.QuotaExceeded"` to check
- TranslateService: catch HttpRequestException 429→Azure.RateLimit, 401/403→Azure.AuthFailed

---

## 3. YouTube Architecture — 13 Files, 3146 LOC

### 3.1 Feature Keep Map (F1-F10)

| # | Feature | Owner | Status |
|---|---------|-------|--------|
| F1 | State cache manifest.json | YouTubeFetchState.cs | **KEEP** |
| F2 | Change detection ETag+count | YouTubeChangeDetector.cs | **KEEP** (inline file, keep logic) |
| F3 | Bulk sync Load→Detect→Merge→Process | Orchestrator + SyncProcessor | **KEEP** |
| F4 | Single-playlist sync by title | Orchestrator.ProcessTitlePipelineAsync | **KEEP** (fix layer bypass) |
| F5 | Duplicate merge | DuplicateMerger + Policy | **KEEP** |
| F6 | Sort playlists LIS | SortService 421 LOC | **KEEP** (OrderBy not equivalent) |
| F7 | Resume incomplete sort | Orchestrator.ExecuteWithSortAsync | **KEEP** |
| F8 | Translate titles batch | TranslationService | **KEEP** |
| F9 | Archive + incremental save | SyncProcessor | **KEEP** |
| F10 | Logging youtube.jsonl | Orchestrator/SyncProcessor | **KEEP** (fix missing ForService) |

> **Bottom line:** 0 features proposed for removal. Every delete/shrink preserves F1-F10.

### 3.2 Buckets (YouTube-only)

**A — DEAD (safe delete, 0 feature loss):**
- Errors PlaylistNotFound/VideoNotFound factories
- SyncResult.UpdatedSnapshots, SyncOutcome.IdsWithNewVideos
- DuplicateMergeOutcome.GroupsProcessed/Deferred
- ProcessResult.ShouldBreak, CombineNewAndChanged inline
- DashboardService DI registration, FetchState.ArchiveDeleted duplicate

**B — DUPE (shrink to one source):**
- StateRoot/Manifest path×5 → PathResolver constant
- dict-filter Where(!ids).ToDictionary ×2 → WithoutIds helper
- Delete/Insert ApiError copy-paste → typed GoogleApiException mapper

**C — YAGNI FILE (collapse inline):**
- YouTubeChangeDetector 62 LOC → inline unless tested
- 4 Execute* dispatch sprawl → 2 methods with SyncOptions
- ProcessResult.ShouldBreak → ErrorOr propagate

**D — LAYER MISPLACE:**
- Title path bypasses SyncProcessor → delegate to SyncProcessor

**E — KEEP (legitimate SRP):**
- SortService LIS, Merger+Policy, PaginateAsync, Processor checkpoint, Translation batching

---

## 4. God File Audit

### 4.1 God Files Ranked

| File | LOC | Verdict | Action |
|------|-----|---------|--------|
| PipelineOrchestrator.cs | 474 | **keep** | 1 job. Borderline size, not true god |
| YouTubeDuplicateMerger.cs | 446 | **keep** | Destructive workflow density. Cohesive |
| DsdConvertService.cs | 425 | **shrink** | Extract DffHeaderReader → ~340 |
| YouTubeSortService.cs | 421 | **keep** | LIS algorithmic density |
| YouTubePlaylistOrchestrator.cs | 418 | **split** | True god. 4 entry points → 2. Layer overlap |
| YouTubeSyncProcessor.cs | 383 | **split** | Weak god. Completes layer god with Orchestrator |
| ProcessRunner.cs | 361 | **shrink** | Speculative generality. CompletionPattern serves 1 path → ~240 |
| SacdProbeRunner.cs | 357 | **cut** | Dead god. C:\Temp harness in prod |
| DashboardHtmlGenerator.cs | 364 | **move** | God method. Keep hand-roll, relocate to Services |
| TextAnalyticsService.cs | 255 | **shrink** | 5× cloned guard/catch → central runner → ~160 |

### 4.2 God Methods Top 5

| Method | File | LOC | Action |
|--------|------|-----|--------|
| Generate(DashboardData) | DashboardHtmlGenerator | ~364 | move to Services |
| RunAsync(binary,args,...) | ProcessRunner | ~180 | drop grace-kill branches |
| ExecuteAsync ×4 variants | YouTubePlaylistOrchestrator | 418 total | collapse dispatch god |
| ConvertAndSplitAsync+ProbeDsdAsync | DsdConvertService | ~200 | extract probe reader |
| AnalyzeAsync ×5 clones | TextAnalyticsService | 5×~45 | extract guard |

### 4.3 God Modules

| Module | Files | LOC | Verdict |
|--------|-------|-----|---------|
| Services/Audio | 21 | 3601 | File-count god. Prune harness → 15 files |
| Services/Google | 13 | 3146 | Layer god. Dedupe orchestration → 1 layer |
| Services/Azure | 12 | 1084 | **keep** — SDK adapter necessity |
| CLI | ~22 | ~1618 | Layer violation. Move 508 LOC to Services |
| Core | 6 | 416 | Telemetry god (10 sinks). Shrink |

---

## 5. Telemetry Pipeline

### 5.1 Bugs Found

| ID | Bug | Impact | Fix |
|----|-----|--------|-----|
| T-01 | File sink ignores LevelSwitch | --verbose Verbose invisible in *.jsonl | Propagate LevelSwitch to AddServiceLogger |
| T-02 | Seq TCP probe blocks startup | 500ms penalty, TCP≠HTTP health | Drop probe, let Serilog retry |
| T-03 | DsdConvertService Debug no ForService | audio.jsonl empty on single-file path | Wrap Telemetry.Log to enforce scope |
| T-04 | SDK listeners bypass ServiceName enum | String literal "SdkDiagnostics" | Force enum routing |
| T-05 | LogPaths global mutable state | Global set in try/finally, not AsyncLocal | Convert to Serilog Enricher |
| T-06 | 8/10 log files 0B | Missing ForService + level drop | Enforce scope, fix level |

### 5.2 Overengineering Verdict

| Pattern | Verdict | Solo-dev Rationale |
|---------|---------|-------------------|
| 10 per-service JSONL loggers | **KEEP** (fix, not cut) | Clean routing pattern. Empty files = missing callers not bad architecture |
| Seq TCP probe | **CUT** | Native Serilog retry sufficient |
| LogPaths custom formatter | **CUT** | Serilog Enricher does this natively |
| 5 one-line Telemetry wrappers | **SHRINK** | One Telemetry.Log(ServiceName, level, template) |

---

## 6. Audio & CLI

### 6.1 Audio Harness Relocation

| Item | LOC | Status | Action |
|------|-----|--------|--------|
| SacdProbeRunner.cs | 357 | dead prod path — 0 pipeline callers | Move to tools/sacd-probe/ |
| SacdProbeService.cs | 15 | pure delegation — 1 call site | Delete |
| RealDffFixture.cs | 50 | C:\Temp hardcode in prod | Delete |
| PathValidator.cs | 25 | 0 callers | Delete |
| DiscState.cs | 10 | single enum file | Merge into AudioModels |

### 6.2 CLI Layer Violations

| Command | LOC in CLI | Violation | Fix |
|---------|-----------|-----------|-----|
| DsdConvertCommand | 133 | probe→gain→convert workflow | Move to DsdConvertService.ConvertSingleFileAsync |
| SacdConvertCommand | 30 | report generation | Move to PipelineOrchestrator return |
| DashboardGenerateCommand + SyncYoutubeCommand | 20 duplicate | Builder→Generator→WriteAllText | Single DashboardService.GenerateAndPersistAsync |
| 7× Azure commands | 84 | Result.Match boilerplate | CliResult.ToExitCode helper |

---

## 7. Overengineering vs SRP vs SoC — Master Verdict

> **Constraint:** Solo dev, not enterprise. Single interface for single invocation = terrible overhead. Clean code + architectural clarity paramount.

| Bucket | Verdict | Examples | Rationale |
|--------|---------|----------|-----------|
| **True over-engineering — CUT** | Delete/shrink | 422 LOC probe harness, 13 dead factories, 4× FRM8 walkers, 5 dead result fields, 84 LOC repeated Match, 10 sinks for 8 empty files | Dead, duplicated, or speculative. No second consumer, no caller, no behavior |
| **Legit SRP — KEEP** | Keep split | PipelineOrch vs DsdConvert (merging=900 LOC); Saracon/Sox/SacdExtract (distinct binaries); LIS sort (quota optimization); MergePolicy (destructive rules) | Merging creates god monsters or collapses distinct external contracts |
| **Correct SoC, wrong layer — MOVE** | Relocate | Dashboard 508 LOC in CLI; OciConfig in Core; DSD workflow in CLI | Separation correct, location violates layering rules |
| **Adapter necessity — KEEP** | Keep | 3 EventListeners (incompatible SDKs); Google→Azure direct dep | Generic façade hides, not reduces. Interface for Translate speculative until 2nd provider |
| **Gray — lean keep** | Keep until 2nd consumer | YouTubeChangeDetector, 4 command modules, FlacChecker statics | YAGNI says inline; SRP says testable unit. Decision: inline if <60 LOC AND untested |

### When is it overengineering? (Solo-dev rationale)

1. **Single interface, single impl, no test seam needed** → overengineering. Delete interface.
2. **Wrapper that adds 0 behavior** (Text.IsEqualTo over string.Equals) → overengineering. Inline.
3. **File-per-trivial-thing** (DiscState 10 LOC enum, PathValidator 25 LOC 0 callers) → overengineering. Merge/delete.
4. **Speculative generality** (ProcessRunner 6 TerminationReasons for 1 path) → overengineering. Cut to what's used.
5. **Copy-paste abstraction** (5× identical TextAnalytics guard) → overengineering *by duplication*. Extract shared runner.
6. **NOT overengineering:** distinct binary wrappers (Saracon ≠ Sox ≠ SacdExtract), quota-critical algorithms (LIS sort), destructive policy separation (MergePolicy), SDK adapter necessity (3 EventListeners). These are *legitimate* even for solo dev because merging collapses distinct external contracts.

---

## 8. Dead Code Catalog — Unconsumed vs Unused

### Unconsumed (no code path reads it)

| # | Symbol | File | Evidence |
|---|--------|------|----------|
| 1 | Errors.PlaylistNotFound/VideoNotFound | Core/Errors.cs:31,33 | 0 producers, 0 consumers |
| 2 | Errors.General.Unexpected/Internal | Core/Errors.cs:9 | 0 callers in repo |
| 3 | Errors.Validation.RequiredField | Core/Errors.cs:21 | 0 callers |
| 4 | Errors.Azure.ServiceUnavailable | Core/Errors.cs:50 | 0+0 |
| 5 | Text.Has/StartsWith | Core/Text.cs:28 | 0 callers |
| 6 | SyncResult.UpdatedSnapshots | YouTubeSyncProcessor.cs:326 | Populated→never read |
| 7 | SyncOutcome.IdsWithNewVideos | YouTubePlaylistOrchestrator.cs:396 | Computed→discarded |
| 8 | DuplicateMergeOutcome.GroupsProcessed/Deferred | YouTubeDuplicateMerger.cs:14 | Logged→discarded |
| 9 | PathValidator.ValidateOutputDirectory | Services/Audio/PathValidator.cs:18 | 0 callers |
| 10 | SacdProbeService | Services/Audio/SacdProbeService.cs:3 | Pure delegation, 0 pipeline callers |
| 11 | DashboardService singleton DI | GoogleSetup.cs:69 | All methods static |
| 12 | Serilog.Sinks.Console | Directory.Packages.props:19 | 0 PackageReference |
| 13 | TranslateCommand --from | CLI/Azure/TranslateCommand.cs:59 | Registered→ignored |
| 14 | SyncResult.SkippedVideos | SyncProcessor→Orchestrator | Logged→never read |
| 15 | ChangeDetectionResult.UnchangedPlaylists | ChangeDetector→Orchestrator | Never iterated |
| 16 | PlaylistSnapshot.LastChecked | PlaylistService:203 | Written→never read |
| 17 | YouTubeFetchState.LastChecked/LastUpdated | FetchState:13 | Written 5 places→never read |
| 18 | DashboardData PlaylistCount/VideoCount | DashboardDataBuilder:20 | Scaffolding→never rendered |

### Unused (code path exists but never triggered at runtime)

| # | Symbol | File | Evidence |
|---|--------|------|----------|
| 1 | SSH.NET in 5 non-CLI projects | Core/Azure/Audio/Google/LastFm .csproj | Declared, 0 Renci usage outside CLI |
| 2 | SacdConvertCommand 24/both format | CLI/Audio/SacdConvertCommand.cs:18 | Advertised→validation rejects |
| 3 | ProcessResult.ShouldBreak=false | YouTubeSyncProcessor.cs:335 | Always true on error |
| 4 | Verbose-level file logs | Telemetry.cs:64 | restrictedToMinimumLevel:Debug drops |
| 5 | HmsTimeSpanConverter in Merger | YouTubeDuplicateMerger.cs | 0 TimeSpan fields in manifest |

### Consumed but logical-error (not dead — fix, don't delete)

| # | Symbol | File | Bug |
|---|--------|------|-----|
| 1 | YT.RateLimit | Errors.cs:27→SyncProcessor:79 | Consumed, never produced (producers emit ApiError) |
| 2 | YT.QuotaExceeded | SortService:312 | Produced, consumer never checks |
| 3 | Azure.AuthFailed/RateLimit | SyncProcessor:88 | Consumed, TranslateService emits generic |
| 4 | S-13 reverseLookup | DashboardService:74 | Sanitized title collision drops data |
| 5 | N-05 transliterate count | TranslationService:213 | hi transliterated counts as translated en |

---

## 9. Implementation Plan — 4 Phases

### Phase 1: Deletions (zero risk, ~10 min)
**-530 LOC, 0 behavior change**
- C-01: Delete 13 error factories
- C-02: Delete Text.Has/StartsWith
- C-03: Drop SSH.NET from 5 csproj
- C-04: Drop Sinks.Console PackageVersion
- A-01: Evict harness to tools/sacd-probe
- A-02: Delete PathValidator
- A-03: Merge DiscState enum
- G-01: Delete duplicate ArchiveDeleted
- G-02: Remove DashboardService singleton registration
- G-03: Drop 4 dead result fields
- CLI-01/02: Drop --from, fix format validation

### Phase 2: Shrinks (god reduction)
**-290 LOC**
- A-04: Extract DffHeaderReader (4→1 FRM8 walker)
- A-05: Strip ProcessRunner grace-kill (~-120)
- T-01/02: Fix file sink level gate, drop TCP probe
- AZ-01: Centralize TextAnalytics guard (~-95)
- CLI-03: CliResult.ToExitCode helper (~-60)
- CLI-04: Move DsdConvertCommand workflow to service
- G-04: Inline ChangeDetector + CombineNewAndChanged
- GOD-01: Collapse 4 Execute* → 2 methods
- SH-01: PathResolver constants

### Phase 3: Moves (boundary fixes)
**0 LOC removed, ~521 relocated**
- M-01: DashboardDataBuilder+HtmlGenerator → Services/Google/DashboardOrchestrator
- M-02: OciConfig → CLI/Dashboard env
- M-03: DsdConvertCommand workflow → DsdConvertService

### Phase 4: Stdlib replacements
**-1 dep**
- S-04: DotNetEnv → 10-line File.ReadLines
- S-01: SanitizeFileName → HashSet
- S-06: MergePolicy → HashSet<string>
- S-07: SpeechService → ArgumentList

### Verification after each phase:
```bash
dotnet build                                    # TreatWarningsAsErrors
dotnet run --project tools/ProbeVerify           # if retained
# tail state/logs/youtube.jsonl not empty
# dashboard generate html identical diff
```

---

## 10. Prompt Section Index

| # | Section | Spec Lines | Subagent | Artifact |
|---|---------|-----------|----------|----------|
| 1 | YouTube Seams Hunt N-01..N-05 | 1-29 | YouTube Data Seams | youtube-seams-spec.md |
| 2 | Dashboard/Sort/Merge Seams S-09..S-17 | 31-77 | YouTube Data Seams | youtube-seams-spec.md |
| 3 | YouTube Fresh Unconsumed S-01..S-08 | 79-134 | YouTube Data Seams | youtube-seams-spec.md |
| 4 | YouTube Deletion Reassessment | 136-201 | YouTube Data Seams | youtube-seams-spec.md |
| 5 | YouTube Full Spec (13 files) | 203-334 | YouTube Architecture | youtube-architecture-spec.md |
| 6 | Singular Telemetry Gate | 336-392 | Telemetry/Logging | telemetry-spec.md |
| 7 | Re-bucketed Spec (Buckets A-E) | 394-465 | YouTube Architecture | youtube-architecture-spec.md |
| 8 | Logging Pipeline | 467-533 | Telemetry/Logging | telemetry-spec.md |
| 9 | Orchestrator 418 Deep Dive | 535-602 | YouTube Architecture | youtube-architecture-spec.md |
| 10 | Probe + Debug | 604-640 | Audio/CLI | audio-cli-spec.md |
| 11 | Final Spec (Sections 0-9) | 642-751 | Audio/CLI | audio-cli-spec.md |
| 12 | God Audit | 753-826 | God Audit/OE Judge | god-audit-spec.md |
| 13 | Ponytail Review | 828-890 | God Audit/OE Judge | god-audit-spec.md |
