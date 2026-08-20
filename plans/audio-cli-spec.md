# Audio, CLI & Final Spec — Caveman Ultra

## Executive Summary

### Points Enumerated

P-01: LOC Removable | FEATURE AT RISK | -820 to -960 lines.
P-02: Files Reduced | LEGITIMATE SRP | 79 -> 73. Keep splits for binaries/SDKs.
P-03: Package Deps Cut | DEAD CODE | -3 scopes (Console sink, SSH.NET x5, DotNetEnv).
P-04: God Files Cut | LAYER MISPLACEMENT | 4 -> 0. Largest file 474 -> ~340.
P-05: God Module Cut | OVERENGINEERING | Audio/Google 2-layer collapse.

## Dead Code Catalog

DC-01: General.Unexpected/Internal @ Core/Errors.cs | unconsumed | 0 callers. API surface inflator.
DC-02: Text.Has/StartsWith @ Core/Text.cs | unconsumed | 0 callers. YAGNI.
DC-03: SSH.NET @ Core.csproj, Azure.csproj, Audio.csproj, Google.csproj, LastFm.csproj | unused | Declared, never used. CLI only consumer.
DC-04: Serilog.Sinks.Console @ Directory.Packages.props | unconsumed | 0 package references. Version ghost.
DC-05: PathValidator.ValidateOutputDirectory @ Services/Audio/PathValidator.cs | unconsumed | 0 callers.
DC-06: DashboardService singleton @ Services/Google/GoogleSetup.cs | unconsumed | All methods static. 0 DI consumers.
DC-07: SyncResult.UpdatedSnapshots & 3 others @ Services/Google/YouTube/ | unconsumed | Computed, logged, discarded.
DC-08: TranslateCommand --from @ CLI/Azure/TranslateCommand.cs | unconsumed | Option registered, never passed to service.
DC-09: SacdConvertCommand 24/both @ CLI/Audio/SacdConvertCommand.cs | unconsumed | Advertised but validation rejects all except 16.

## Overengineering Assessment

OE-01: DotNetEnv Package | cut | solo-dev rationale: Trivial 10-line File.ReadLines loop replaces entire 3.1.1 package for single call.
OE-02: IsSeqReachableAsync TCP probe | cut | solo-dev rationale: Platform handles backpressure. Custom probe costs 500ms startup penalty.
OE-03: TextAnalyticsService 5x Clones | cut | solo-dev rationale: 95 LOC copy-paste boilerplate. Centralize guard/runner.
OE-04: 10 Serilog Sub-loggers | cut | solo-dev rationale: YAGNI per-file tailing. Single app.jsonl with jq sufficient.
OE-05: CLI Result.Match Boilerplate | cut | solo-dev rationale: 12 copies of same success/error shape. Thin generic helper required.
OE-06: DsdConvertService + SaraconService + DffMetadataStripper + RealDffFixture FRM8 | cut | solo-dev rationale: 4 identical chunk walkers. Dedupe to single DffHeaderReader.

## Features At Risk

FR-01: PipelineOrchestrator vs DsdConvertService Split | Services/Audio | Legitimate SRP. Merging = 900 LOC god file. Distinct IO vs conversion.
FR-02: YouTubeSortService LIS | Services/Google/YouTube | Quota-optimized algo minimizes writes. Not overengineering.
FR-03: YouTubeDuplicateMerger + Policy | Services/Google/YouTube | Action separated from rules. Destructive mutations justify split.
FR-04: CueParser | Services/Audio | 8-directive custom BOM/1252 handling.
FR-05: Saracon/Sox/SacdExtract | Services/Audio | Distinct binaries with specific arg/regex requirements.
FR-06: 3 EventListeners | Services/Azure | Incompatible SDK APIs (Azure-Core, ClientModel, Speech). Adapters required.
FR-07: Hand-rolled $$""" HTML | CLI/Dashboard | Razor/Scriban addition for 1 call is net complexity increase.

## Cross-Reference Verification

CR-01: SacdProbeRunner is 422 LOC dead prod harness | MATCH | Grep confirms SacdProbeRunner (358 LOC) only called by SacdProbeService. No pipeline calls it. 100% dead operational path.
CR-02: DsdConvertCommand violates CLI/AGENTS.md | MATCH | DsdConvertCommand.cs is 182 LOC containing ProbeDsdAsync, CalculateGainAsync, ConvertFullDffAsync.
CR-03: DotNetEnv single call | MATCH | Grep confirms DotNetEnv exists only in App.csproj and Program.cs.
CR-04: SSH.NET in 5 projects | MATCH | Grep confirms SSH.NET present in 5+ project files. Only used in OciDashboardDeployer.

## Detailed Phase Plan

### Phase 1: Deletions (Zero Risk)

- C-01: Delete 13 error factories
- C-02: Delete Text extensions
- C-03/05: Drop SSH.NET from 5 csproj files
- C-04: Drop Console sink package
- A-01: Evict SacdProbeRunner to tools/
- A-02: Delete ValidateOutputDirectory
- A-03: Merge DiscState enum
- G-01: Dedupe ArchiveDeleted
- G-02: Remove DashboardService singleton
- G-03: Drop dead Sync fields
- CLI-01/02: Drop --from and fix formats

### Phase 2: Shrinks (God Reduction)

- A-04: Extract DffHeaderReader (FRM8 dedupe)
- A-05: Strip ProcessRunner completionPattern ("100%")
- A-06: Call BuildD2pArgs
- A-07: Fix HasId3Chunk sync block
- G-04/05: Inline YouTubeChangeDetector and ProcessResult wrapper
- T-01/02: Collapse loggers, drop TCP probe
- AZ-01/02/03: Centralize Azure text length guards, arg lists, and level map
- CLI-03/04/05/06: Collapse CLI boilerplate, relocate workflows
- SH-01/02: Standardize manifest paths, move Flac checkers
- AP-01/02: Dedup exception catches, fix string[] check

### Phase 3: Moves (Layering Fixes)

- M-01: Move DashboardHtmlGenerator + DataBuilder to Services/Google/YouTube/DashboardOrchestrator.cs
- M-02: Move OciConfig out of Core to CLI deploy options / OCI_HOST env
- M-03: Shift DsdConvertCommand logic into DsdConvertService

### Phase 4: Stdlib Replacements

- S-01: Use HashSet for SanitizeFileName
- S-02: Use string.Equals instead of IsEqualTo
- S-03: Use WebUtility.HtmlEncode
- S-04: 10-line loop for .env instead of DotNetEnv
- S-05: Remove TCP probe
- S-06: HashSet<string> for DuplicateMergePolicy missingIds
- S-07: ArgumentList for SpeechService
