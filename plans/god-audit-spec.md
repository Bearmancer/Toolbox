# God Audit Spec — Caveman Ultra

## Points Enumerated

P-01: PipelineOrchestrator.cs (474) | keep | 1 job (orchestrate extract/convert). Borderline size but legitimate SRP.
P-02: YouTubeDuplicateMerger.cs (446) | keep | Destructive workflow density. Cohesive.
P-03: DsdConvertService.cs (425) | shrink | Weak god. Extract DffHeaderReader to fix pure IO leaking into facade.
P-04: YouTubeSortService.cs (421) | keep | Algorithmic density (LIS plan). Not god.
P-05: YouTubePlaylistOrchestrator.cs (418) | split | True god. Overloaded coordinator. Overlaps SyncProcessor.
P-06: YouTubeSyncProcessor.cs (383) | split | Weak god. Completes layer god with Orchestrator.
P-07: ProcessRunner.cs (361) | shrink | True god via speculative generality. 120+ lines for 1 unused path.
P-08: SacdProbeRunner.cs (357) | cut | Dead god. Prod harness shipping dead code.
P-09: DashboardHtmlGenerator.cs (364) | move | True god method/file. String density justified, but belongs in Services, not CLI.
P-10: YouTubePlaylistProcessor.cs (351) | keep | Cohesive per-playlist.
P-11: YouTubePlaylistService.cs (339) | keep | Single API facade.
P-12: SaraconService.cs (338) | keep | Distinct binary wrapper.
P-13: TextAnalyticsService.cs (255) | shrink | God by duplication. 5x identical catch/guard logic.
P-14: SpeechService.cs (284) | keep | 3 ops, distinct SDKs.
P-15: YouTubeTranslationService.cs (267) | keep | Coherent.
P-16: DffMetadataStripper.cs (285) | shrink | Dedupe FRM8 walker.
P-17: CueParser.cs (215) | keep | Parser density justified.
P-18: Generate(DashboardData) | move | Keep hand-roll. Move to Services. Splitting adds indirection.
P-19: PipelineOrchestrator RunAsync/Process/Convert | keep | Sequenced, not branching. 120 lines each.
P-20: ProcessRunner RunAsync | shrink | Timeout branches serve 1 path. Keep cancellation, cut grace logic.
P-21: DsdConvertService ConvertAndSplitAsync | shrink | Extract probe reader.
P-22: YouTubePlaylistOrchestrator ExecuteAsync x4 | split | Dispatch god. Collapse 4 points.
P-23: YouTubeSyncProcessor ProcessPlaylistsAsync | keep | Single batch loop.
P-24: YouTubeTranslationService ExecuteTranslationBatchesAsync | keep | Chunking justified.
P-25: TextAnalyticsService AnalyzeAsync x5 | shrink | Cloned methods. Extract guard.
P-26: ProbeDsdAsync walkers x4 | shrink | Duplicated walkers. Extract to single reader.
P-27: App/Program.cs Main | keep | Bootstrap wiring requires lines.
P-28: Services/Audio | keep | File-count god, not LOC god. Mean 171. Topology clean.
P-29: Services/Google | split | Layer god. Orchestrator and Processor overlap. Dedupe state loop.
P-30: Services/Azure | keep | SDK adapter necessity. No duplication aside from TextAnalytics.
P-31: CLI | move | Layer violation. DSD workflow and dashboard view live in CLI. Move to Services.
P-32: Core | shrink | Telemetry is a god (10 sinks). Shrink to 1.
P-33: App | keep | Bootstrap wiring.
P-34: Toolbox repo | keep | Build infra exemplary. General prune needed.

## Dead Code Catalog

DC-01: SacdProbeRunner harness @ Services/Audio/SacdProbeRunner.cs:1 | unconsumed | Prod harness for C:\Temp.
DC-02: PathValidator.ValidateOutputDirectory @ Services/Audio/PathValidator.cs:18 | unused | 0 callers.
DC-03: DiscState enum / PathValidator wrapper @ Services/Audio/DiscState.cs:1 | unconsumed | Trivial wrapper.
DC-04: YouTubeFetchState.ArchiveDeleted @ Services/Google/YouTube/YouTubeFetchState.cs:94 | unconsumed | Dupe of SyncProcessor archival.
DC-05: SyncResult.UpdatedSnapshots fields @ Services/Google/YouTubeSyncProcessor.cs:323 | unused | Never read after log.
DC-06: 13 Error factories @ Core/Errors.cs:9 | unconsumed | Speculative General x2, Azure x3, etc.
DC-07: SSH.NET PackageReference @ Core/Core.csproj:11 | unused | 0 Renci.SshNet usage outside CLI.
DC-08: Serilog.Sinks.Console @ Directory.Packages.props:19 | unconsumed | 0 references.
DC-09: DashboardService singleton @ Services/Google/GoogleSetup.cs:69 | unused | All methods static.
DC-10: TranslateCommand --from @ CLI/Azure/TranslateCommand.cs:59 | unused | Option registered but ignored.
DC-11: Text.Has / StartsWith @ Core/Text.cs:28 | unused | 0 callers.
DC-12: FindSaracon PATH scan @ Services/Audio/SacdProbeRunner.cs:272 | unconsumed | Dupe of ProcessRunner.IsOnPath.
DC-13: Inline d2p args @ Services/Audio/SacdProbeRunner.cs:210 | unconsumed | Dupe of BuildD2pArgs.
DC-14: EventListener wrappers @ Services/Azure/AzureSdkEventListener.cs:21 | unconsumed | Wrappers add nothing.
DC-15: YouTubeChangeDetector @ Services/Google/YouTube/YouTubeChangeDetector.cs:12 | unconsumed | 1 caller pure function.
DC-16: CombineNewAndChanged @ Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:375 | unconsumed | 1 expr, 1 caller.
DC-17: ProcessResult.ShouldBreak @ Services/Google/YouTube/YouTubeSyncProcessor.cs:45 | unconsumed | Always break on error.
DC-18: SacdProbeService @ Services/Audio/SacdProbeService.cs:3 | unconsumed | Pure delegation, 1 call site.
DC-19: PathResolver.GetStatePath @ Core/PathResolver.cs:29 | unconsumed | Trivial wrapper, 2 callers.
DC-20: ServiceNameMethods @ Core/ServiceName.cs:17 | unconsumed | 1 consumer. Inline.
DC-21: AddAzureServices returns @ Services/Azure/AzureSetup.cs:15 | unconsumed | No caller chains.
DC-22: OciConfig / Tailnet IP @ Core/OciConfig.cs:5 | unconsumed | Infra in shared.
DC-23: SerilogTracing / Spectre sink @ Core/Telemetry.cs:88 | unconsumed | 1 StartActivity call.
DC-24: FlacCompletenessChecker statics @ Services/Audio/FlacCompletenessChecker.cs:100 | unconsumed | 1 caller.
DC-25: DotNetEnv @ App/Program.cs:46 | unconsumed | Single env load.
DC-26: Text.IsEqualTo @ Core/Text.cs:23 | unconsumed | Wrapper over string.Equals.
DC-27: DashboardDataBuilder.Escape @ CLI/Dashboard/DashboardDataBuilder.cs:129 | unconsumed | Hand-rolls replaces.
DC-28: Telemetry.IsSeqReachableAsync @ Core/Telemetry.cs:91 | unconsumed | Manual TCP probe.
DC-29: YouTubeDuplicateMergePolicy Contains @ Services/Google/YouTube/YouTubeDuplicateMergePolicy.cs:39 | unused | Linear scan in hot loop.

## Overengineering Assessment

OE-01: ProcessRunner grace kill | cut | Speculative generality. 1 path out of many uses it. 120 lines pure overhead.
OE-02: TextAnalytics duplication | shrink | Duplication is overengineering. Extract shared guard runner.
OE-03: Azure EventListeners (3 interfaces) | keep | Adapter necessity. 3 incompatible SDKs. Solo dev: keep interfaces native.
OE-04: CLI Layer Business Logic | move | CLI generating dashboard strings/DSD workflow is SoC violation. Fix by moving.
OE-05: 10 Serilog Sinks | cut | Unnecessary overhead. 1 app.jsonl + jq filter suffices.
OE-06: God Modules (Google Layer) | split | Multiple files managing same state loop. Over-abstracted.
OE-07: Telemetry wrappers | shrink | 5 one-line wrappers over Log.Write. Use Serilog directly.
OE-08: Command Modules (1 method) | cut | 4 modules with 1 method each. YAGNI. Inline to Program.cs.

## Features At Risk

FR-01: PipelineOrchestrator orchestration | Services/Audio/PipelineOrchestrator.cs | Not dead. Sequenced logic.
FR-02: YouTubeDuplicateMerger | Services/Google/YouTube/YouTubeDuplicateMerger.cs | Not dead. Destructive API sync logic.
FR-03: Dashboard string gen | CLI/Dashboard/DashboardHtmlGenerator.cs | Not dead. Legitimate lack of Razor deps.
FR-04: CueParser custom heuristics | Services/Audio/CueParser.cs | Not dead. BOM/UTF8 fallback needed.
FR-05: Sox/Saracon wrappers | Services/Audio/SoxService.cs | Not dead. Distinct args/execution.
FR-06: LIS Sorting | Services/Google/YouTube/YouTubeSortService.cs | Not dead. Quota optimization algorithm.

## Cross-Reference Verification

CR-01: ProcessRunner is ~361 LOC | Confirmed via ProcessRunner.cs (379 LOC total) | match
CR-02: PipelineOrchestrator is ~474 LOC | Confirmed via PipelineOrchestrator.cs (464 LOC total) | match
CR-03: TextAnalyticsService 255 LOC, 5 cloned guards | Confirmed (256 LOC total, identical structures) | match
CR-04: DashboardHtmlGenerator 364 LOC, 1 method | Confirmed (365 LOC total, single Generate interpolation) | match

## Over-engineering vs SRP vs SoC — Final Verdict

| Bucket                          | Verdict       | Examples                                                                                                                               | Rationale                                                                 |
| ------------------------------- | ------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| True over-engineering — cut     | Delete/shrink | 422 LOC probe harness, 13 dead factories, duplicate FRM8/PATH, 5 dead result fields, 84 LOC repeated Match, 10 sinks for 8 empty files | Dead, duplicated, or speculative — no consumer, no caller                 |
| Legit SRP — keep split          | Keep          | PipelineOrch vs DsdConvert; Saracon/Sox/SacdExtract each distinct; LIS sort; MergePolicy                                               | Merging creates 900 LOC monsters or collapses distinct external contracts |
| Correct SoC, wrong layer — move | Relocate      | Dashboard view 508 LOC in CLI; OciConfig in Core; DSD workflow in CLI                                                                  | Separation correct, location violates layering rules                      |
| Adapter necessity — keep        | Keep          | 3 EventListeners, Google→Azure direct dep                                                                                              | Generic façade would hide, not reduce                                     |
| Gray — keep until 2nd consumer  | Lean keep     | YouTubeChangeDetector, 4 command modules, FlacChecker statics                                                                          | YAGNI says inline; cost of file < benefit of isolated tests               |
