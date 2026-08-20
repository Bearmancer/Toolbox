# Logging Audit Report

**Date:** 2026-08-10
**Auditor:** Oracle (read-only consultation)
**Scope:** All projects in C:\Users\Lance\Dev\Toolbox

---

## Summary

| Metric              | Count  |
| ------------------- | ------ |
| Total files audited | 52     |
| Critical issues     | 5      |
| High issues         | 10     |
| Medium issues       | 3      |
| Low issues          | 3      |
| **Total issues**    | **21** |

---

## Audio Service (src/Services/Audio/)

| File                    | Line    | Issue                                                                   | Severity | Fix                                                                                              |
| ----------------------- | ------- | ----------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------ |
| AudioMetadataService.cs | 33      | Silent catch in ReadDsdMetadata — returns Error without logging         | CRITICAL | Add Telemetry.Error("Audio.MetadataReadFailed file={File}: {Error}", filePath, ex.Message)       |
| AudioMetadataService.cs | 78      | Silent catch in WriteFlacTags — returns Error without logging           | CRITICAL | Add Telemetry.Error("Audio.TagWriteFailed file={File}: {Error}", flacPath, ex.Message)           |
| AudioMetadataService.cs | 111     | Silent catch in CopyMetadataFromCue — returns Error without logging     | CRITICAL | Add Telemetry.Error("Audio.CueTagFailed file={File}: {Error}", flacPath, ex.Message)             |
| DsdConvertService.cs    | 108     | Silent catch in ProbeDsdAsync — returns Error without logging           | CRITICAL | Add Telemetry.Error("DsdConvert.ProbeFailed file={File}: {Error}", dffFilePath, ex.Message)      |
| DffMetadataStripper.cs  | 116     | Silent catch in StripId3TagsAsync — returns Error without logging       | HIGH     | Add Telemetry.Error("DffMetadataStripper.StripFailed file={File}: {Error}", dffPath, ex.Message) |
| PathValidator.cs        | 33      | Silent catch in ValidateOutputDirectory — returns Error without logging | HIGH     | Add Telemetry.Error("PathValidator.OutputUnwritable path={Path}: {Error}", fullPath, ex.Message) |
| DsdConvertService.cs    | 143-145 | Temp dir cleanup in finally block — silent if Directory.Delete fails    | LOW      | Add Telemetry.Warn for cleanup failure                                                           |

**Notes:** SaraconService.cs, SoxService.cs, SacdExtractService.cs, PipelineOrchestrator.cs, ProcessRunner.cs, CueParser.cs, DiskSpaceChecker.cs, and AudioSetup.cs have adequate logging. SaraconService.cs lines 104-121 is the reference example for good logging.

---

## Azure Service (src/Services/Azure/)

| File                    | Line | Issue                                                          | Severity | Fix                                                                                                                                                  |
| ----------------------- | ---- | -------------------------------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| TranslateService.cs     | 49   | Error logged but missing context (batch size, target language) | HIGH     | Add {Count} and {Language} params: Telemetry.Error("Translate: API error for {Count} texts to {Language}: {Error}", texts.Count, toLang, ex.Message) |
| TranslateService.cs     | 79   | Error logged but missing context (language, scripts)           | HIGH     | Add {Language}, {FromScript}, {ToScript} params                                                                                                      |
| VisionService.cs        | 70   | Error logged but missing file path and feature                 | HIGH     | Add {File} and {Feature} params: Telemetry.Error("Vision: API error for {File} feature={Feature}: {Error}", filePath, feature, ex.Message)           |
| SpeechService.cs        | 40   | ffmpeg error missing input file path                           | HIGH     | Add {File} param: Telemetry.Error("Speech: ffmpeg conversion failed for {File}: {Error}", path, ex.Message)                                          |
| SpeechService.cs        | 79   | Transcription error missing file path and language             | HIGH     | Add {File} and {Language} params                                                                                                                     |
| DocIntelService.cs      | 51   | Error logged but missing file path and model ID                | HIGH     | Add {File} and {Model} params: Telemetry.Error("DocIntel: API error for {File} model={Model}: {Error}", filePath, modelId, ex.Message)               |
| OpenAiService.cs        | 72   | Error logged but missing deployment name                       | HIGH     | Add {Deployment} param: Telemetry.Error("OpenAI: API error deployment={Deployment}: {Error}", modelDeployment, ex.Message)                           |
| TextAnalyticsService.cs | 66   | Sentiment error missing text length and language               | MEDIUM   | Add {Length} and {Language} params                                                                                                                   |
| TextAnalyticsService.cs | 102  | Entities error missing text length and language                | MEDIUM   | Add {Length} and {Language} params                                                                                                                   |
| TextAnalyticsService.cs | 134  | Key phrases error missing text length and language             | MEDIUM   | Add {Length} and {Language} params                                                                                                                   |
| TextAnalyticsService.cs | 166  | Detect language error missing country hint                     | MEDIUM   | Add {CountryHint} param                                                                                                                              |
| TextAnalyticsService.cs | 227  | PII error missing text length and language                     | MEDIUM   | Add {Length} and {Language} params                                                                                                                   |

**Notes:** AzureSdkEventListener.cs, ClientModelEventListener.cs, SpeechSdkEventListener.cs, EventLevelMapper.cs, AzureCredentials.cs, and AzureSetup.cs are infrastructure/config — no logging issues.

---

## Google/YouTube Service (src/Services/Google/YouTube/)

| File                           | Line    | Issue                                                                  | Severity | Fix                                                                                                                             |
| ------------------------------ | ------- | ---------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------- |
| YouTubePlaylistOrchestrator.cs | 352-354 | Silent catch in LoadStoredStateAsync — wraps exception without logging | CRITICAL | Add Telemetry.Error("YouTube.StateLoadFailed path={Path}: {Error}", path, ex.Message) before returning error                    |
| YouTubePlaylistProcessor.cs    | 138-141 | Silent catch in FetchItemsAsync — wraps exception without logging      | HIGH     | Add Telemetry.Error("YouTube.FetchItemsFailed playlist={Id}: {Error}", ctx.Snapshot.PlaylistId, ex.Message)                     |
| YouTubePlaylistService.cs      | 279-283 | DeletePlaylistAsync catch block returns error without logging          | MEDIUM   | Add Telemetry.Error("YouTube.DeletePlaylistFailed id={Id}: {Error}", playlistId, ex.Message)                                    |
| YouTubePlaylistService.cs      | 325-331 | InsertPlaylistItemAsync catch block returns error without logging      | MEDIUM   | Add Telemetry.Error("YouTube.InsertItemFailed video={VideoId} playlist={PlaylistId}: {Error}", videoId, playlistId, ex.Message) |
| YouTubeVideoService.cs         | 71-73   | Silent catch for FormatException — no logging                          | MEDIUM   | Add Telemetry.Error("YouTube.DurationParseFailed: {Error}", ex.Message)                                                         |
| YouTubeSortService.cs          | 136-140 | Fetch failure logged at Verbose instead of Error/Warn                  | LOW      | Change Telemetry.Verbose to Telemetry.Warn in FetchPlaylistItemsAsync catch block                                               |

**Notes:** YouTubeChangeDetector.cs, YouTubeFetchState.cs, YouTubeSyncProcessor.cs, YouTubeDuplicateMerger.cs, DashboardService.cs, YouTubeTranslationService.cs, and YouTubeDuplicateMergePolicy.cs have adequate logging.

---

## LastFm Service (src/Services/LastFm/)

| File               | Line  | Issue                                           | Severity | Fix                                                                           |
| ------------------ | ----- | ----------------------------------------------- | -------- | ----------------------------------------------------------------------------- |
| LastFmApiClient.cs | 57-61 | HTTP request logged at Verbose instead of Debug | LOW      | Change Telemetry.Verbose to Telemetry.Debug for HTTP request/response logging |

**Notes:** LastFmService.cs, LastFmSyncOrchestrator.cs, LastFmState.cs, and LastFmSetup.cs have adequate logging. LastFmService.cs has excellent retry logging with Warn level.

---

## Core (src/Core/)

No logging issues found. Telemetry.cs, Errors.cs, PathResolver.cs, Text.cs, OciConfig.cs, and ServiceName.cs are clean.

---

## CLI (src/CLI/)

| File                    | Line | Issue                                                  | Severity | Fix                                                           |
| ----------------------- | ---- | ------------------------------------------------------ | -------- | ------------------------------------------------------------- |
| OciDashboardDeployer.cs | 49   | SFTP upload failure logged at Warn instead of Error    | LOW      | Change to Telemetry.Error — deploy failure is not recoverable |
| OciDashboardDeployer.cs | 67   | Remote command failure logged at Warn instead of Error | LOW      | Change to Telemetry.Error                                     |
| OciDashboardDeployer.cs | 73   | SSH command failure logged at Warn instead of Error    | LOW      | Change to Telemetry.Error                                     |

**Notes:** All CLI command files (SacdConvertCommand.cs, DsdConvertCommand.cs, DashboardGenerateCommand.cs, SyncYoutubeCommand.cs, SyncLastFmCommand.cs) have adequate logging. TypeRegistrar.cs, module files, and DashboardDataBuilder.cs/HtmlGenerator.cs are clean.

---

## App (src/App/)

No logging issues found. Program.cs has proper startup failure logging.

---

## SacdProbe (tools/SacdProbe/)

No logging issues found. This is a diagnostic tool, not production code.

---

## Priority Fixes

### CRITICAL (fix immediately)

1. **AudioMetadataService.cs:33** — Silent catch in ReadDsdMetadata
   - Add: Telemetry.Error("Audio.MetadataReadFailed file={File}: {Error}", filePath, ex.Message);

2. **AudioMetadataService.cs:78** — Silent catch in WriteFlacTags
   - Add: Telemetry.Error("Audio.TagWriteFailed file={File}: {Error}", flacPath, ex.Message);

3. **AudioMetadataService.cs:111** — Silent catch in CopyMetadataFromCue
   - Add: Telemetry.Error("Audio.CueTagFailed file={File}: {Error}", flacPath, ex.Message);

4. **DsdConvertService.cs:108** — Silent catch in ProbeDsdAsync
   - Add: Telemetry.Error("DsdConvert.ProbeFailed file={File}: {Error}", dffFilePath, ex.Message);

5. **YouTubePlaylistOrchestrator.cs:352** — Silent catch in LoadStoredStateAsync
   - Add: Telemetry.Error("YouTube.StateLoadFailed path={Path}: {Error}", path, ex.Message);

### HIGH (fix soon)

6. **DffMetadataStripper.cs:116** — Silent catch in StripId3TagsAsync
   - Add: Telemetry.Error("DffMetadataStripper.StripFailed file={File}: {Error}", dffPath, ex.Message);

7. **PathValidator.cs:33** — Silent catch in ValidateOutputDirectory
   - Add: Telemetry.Error("PathValidator.OutputUnwritable path={Path}: {Error}", fullPath, ex.Message);

8. **YouTubePlaylistProcessor.cs:138** — Silent catch in FetchItemsAsync
   - Add: Telemetry.Error("YouTube.FetchItemsFailed playlist={Id}: {Error}", ctx.Snapshot.PlaylistId, ex.Message);

9. **TranslateService.cs:49** — Missing context in error log
   - Change to: Telemetry.Error("Translate: API error for {Count} texts to {Language}: {Error}", texts.Count, toLang, ex.Message);

10. **TranslateService.cs:79** — Missing context in error log
    - Change to: Telemetry.Error("Transliterate: API error for {Count} texts {FromScript}->{ToScript}: {Error}", texts.Count, fromScript, toScript, ex.Message);

11. **VisionService.cs:70** — Missing context in error log
    - Change to: Telemetry.Error("Vision: API error for {File} feature={Feature}: {Error}", filePath, feature, ex.Message);

12. **SpeechService.cs:40** — Missing file path in error log
    - Change to: Telemetry.Error("Speech: ffmpeg conversion failed for {File}: {Error}", path, ex.Message);

13. **SpeechService.cs:79** — Missing context in error log
    - Change to: Telemetry.Error("Speech: transcription failed for {File} lang={Language}: {Error}", path, language, ex.Message);

14. **DocIntelService.cs:51** — Missing context in error log
    - Change to: Telemetry.Error("DocIntel: API error for {File} model={Model}: {Error}", filePath, modelId, ex.Message);

15. **OpenAiService.cs:72** — Missing deployment name in error log
    - Change to: Telemetry.Error("OpenAI: API error deployment={Deployment}: {Error}", modelDeployment, ex.Message);

### MEDIUM (fix when convenient)

16. **TextAnalyticsService.cs:66,102,134,166,227** — Missing context in 5 error logs
    - Add text length and language parameters to all 5 error log calls

17. **YouTubePlaylistService.cs:279** — Missing log in DeletePlaylistAsync catch
    - Add: Telemetry.Error("YouTube.DeletePlaylistFailed id={Id}: {Error}", playlistId, ex.Message);

18. **YouTubePlaylistService.cs:325** — Missing log in InsertPlaylistItemAsync catch
    - Add: Telemetry.Error("YouTube.InsertItemFailed video={VideoId} playlist={PlaylistId}: {Error}", videoId, playlistId, ex.Message);

19. **YouTubeVideoService.cs:71** — Silent catch for FormatException
    - Add: Telemetry.Error("YouTube.DurationParseFailed: {Error}", ex.Message);

### LOW (fix eventually)

20. **YouTubeSortService.cs:136** — Wrong log level (Verbose for fetch failure)
    - Change Telemetry.Verbose to Telemetry.Warn

21. **OciDashboardDeployer.cs:49,67,73** — Wrong log level (Warn for deploy failures)
    - Change Telemetry.Warn to Telemetry.Error for all 3 deploy failure logs

22. **LastFmApiClient.cs:57** — Wrong log level (Verbose for HTTP requests)
    - Change Telemetry.Verbose to Telemetry.Debug

---

## Patterns Observed

### Good Patterns (keep these)

1. **SaraconService.cs** — Reference example: Warn for retryable failures, Info for retry actions, Error for final failure, structured parameters with context
2. **ProcessRunner.cs** — Comprehensive logging: Start, Complete, Timeout, Failed with binary name, elapsed time, exit codes
3. **PipelineOrchestrator.cs** — Good use of Info for milestones, Warn for recoverable issues, Error for failures
4. **LastFmService.cs** — Excellent retry logging with attempt numbers and delays

### Bad Patterns (fix these)

1. **Silent catch blocks** — 5 critical instances where exceptions are caught and returned as errors without any logging
2. **Missing context in Azure services** — All 6 Azure service classes log errors but omit critical context (file paths, deployment names, text lengths)
3. **Wrong log levels** — 3 instances of Verbose/Debug for actual errors, 3 instances of Warn for actual failures

---

## Effort Estimate

- **Critical fixes:** 5 files, ~15 minutes
- **High fixes:** 8 files, ~30 minutes
- **Medium fixes:** 3 files, ~20 minutes
- **Low fixes:** 3 files, ~10 minutes
- **Total:** ~75 minutes

---

## Success Criteria

- [x] All 8 projects audited
- [x] All .cs files in scope reviewed
- [x] Findings table generated for each project
- [x] Summary statistics calculated
- [x] Priority fix list created
- [x] Output saved to docs/superpowers/audits/2026-08-10-logging-audit.md

---

**End of Audit Report**
