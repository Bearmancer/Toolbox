HANDOFF CONTEXT
===============

GOAL
----
Complete Railway Transformation: refactor all service methods from throw/try-catch to ErrorOr pipelines, terminate at CLI via .Match(), then verify build.

WORK COMPLETED
--------------
- Batch 1 committed as "Phase 1 done" (005d00a).
- TranslateService.TranslateBatchAsync returns ErrorOr<List<TranslationResult>>.
- YouTubeVideoService.GetVideoDurationsAsync returns ErrorOr<Dictionary<string, TimeSpan>>.
- LastFmService retired Option<T> for ErrorOr<T>.
- Errors.cs has factories for DocIntel, Speech, Vision, OpenAI, Translate, TextAnalytics.
- SyncCounters reverted to mutable class (per user feedback — do not make it immutable again).
- Redundant `with` expression removed from YouTubeChangeDetector.cs.
- .gitignore updated for .omo state tracking.

CURRENT STATE
-------------
- HEAD at 005d00a "Phase 1 done". Working tree clean (no uncommitted source changes).
- .omo/plans/railway-transformation.md exists with 28 tasks. Batch 1 (5 tasks) checked complete. Batches 2-4 (23 tasks) unchecked.
- Build is BROKEN. Expected until Batch 4 finishes — ErrorOr return types in Batch 1 break callers in YouTubePlaylistProcessor, YouTubeTranslationService, YouTubePlaylistOrchestrator.

REMAINING TASKS (23)
---------------------
Batch 2 (10): Change 6 Azure service files from Task<string> to Task<ErrorOr<string>>.
  Files: VisionService.cs, TextAnalyticsService.cs (5 methods), OpenAiService.cs, SpeechSttService.cs, DocIntelService.cs, SpeechTtsService.cs.

Batch 2.5 (6): Add .Match() terminal to CLI commands.
  Files: VisionCommand.cs, TranslateCommand.cs, SpeechSttCommand.cs, NerCommand.cs, PhrasesCommand.cs, DocIntelCommand.cs.

Batch 3 (4): Railwayize YouTube internals + fix unsafe .Value access.
  Files: YouTubePlaylistProcessor.cs (2 methods), YouTubeTranslationService.cs, LastFmService.cs.

Batch 4 (3): Orchestrator pipeline.
  Files: YouTubeSortService.cs, YouTubePlaylistOrchestrator.cs (2 methods).

ANTIPATIENRS TO AVOID
---------------------
- Do NOT make SyncCounters immutable again (reverted by user).
- Do NOT use IReadOnlyList/IReadOnlyDictionary in signatures — use concrete List/Dictionary.
- Do NOT add code comments of any kind.
- Do NOT add business logic to CLI layer — CLI is .Match() only.
- Do NOT use global:: qualifiers or fully qualified inline invocations.
- Do NOT use #pragma warning disable or any suppression attributes.
- Do NOT suppress type errors with `as any` or `@ts-ignore` (C# equivalent: no casts that hide errors).
- Do NOT add test NuGet packages — manual dotnet run verification only.
- Do NOT put multiple classes in one file.
- Do NOT skip dotnet build between batches.
- Do NOT add XML doc comments unless required by build.
- Do NOT implement anything outside the railway-transformation.md plan scope.
- Do NOT use PropertyNamingPolicy in JSON serialization — PascalCase is default.
- Do NOT commit until a batch is fully complete.

EXECUTION ORDER
---------------
1. Batch 2 (Azure services) — parallel dispatch across 6 files.
2. dotnet build.
3. Batch 2.5 (CLI .Match()) — parallel, 6 files, small changes.
4. dotnet build.
5. Batch 3 (YouTube/LastFm) — includes unsafe .Value fix.
6. dotnet build.
7. Batch 4 (Orchestrator) — last batch, build should pass.
8. dotnet build final verification.
9. Commit.

KEY FILES
---------
- .omo/plans/railway-transformation.md — master plan, task details, acceptance criteria
- src/Core/Errors.cs — central error taxonomy
- src/Services/Azure/VisionService.cs — Batch 2 target
- src/Services/Azure/TextAnalyticsService.cs — Batch 2 target (5 methods)
- src/Services/Azure/OpenAiService.cs — Batch 2 target
- src/Services/Azure/SpeechSttService.cs — Batch 2 target (keep finally cleanup)
- src/Services/Azure/DocIntelService.cs — Batch 2 target
- src/Services/Azure/SpeechTtsService.cs — Batch 2 target
- src/CLI/Azure/VisionCommand.cs — Batch 2.5 target (and 5 other command files)
- src/Services/Google/YouTube/YouTubePlaylistProcessor.cs — Batch 3 target
- src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs — Batch 4 target

STANDARD PATTERNS
-----------------
Railway for Batch 2: return ErrorOr<string> instead of string. ValidateInput returns Errors.Validation.* instead of throwing. CallApi wrapped in try-catch returning Errors.[Service].ApiError. Telemetry wraps chain via using var _ = Telemetry.ForService(...).
CLI .Match(): return await service.MethodAsync(args, ct).Match(success => { Console.WriteLine(success); return 0; }, error => { Console.Error.WriteLine(error.Description); return 1; });
Error wrapping: ArgumentOutOfRangeException -> Errors.Validation.InvalidInput. InvalidOperationException -> Errors.[Service].ApiError. ArgumentException -> Errors.Validation.InvalidInput.

TO CONTINUE:
1. Press 'n' for a new session, or run 'opencode' in a new terminal.
2. Paste this entire document as your first message.
3. Add: "Continue from handoff. Start Batch 2."
