# AGENTS.md — Toolbox

**Generated:** 2026-07-31 | **Commit:** 70dd931 | **Branch:** master

Extends `C:\Users\Lance\.config\opencode\AGENTS.md`. All Sisyphus directives apply.

## OVERVIEW

CLI toolbox wrapping Azure AI services, Google YouTube API, Last.fm, and SACD audio conversion. .NET 11.0, Spectre.Console.Cli, Serilog, ErrorOr.

## STRUCTURE

```
Toolbox/
├── Toolbox.slnx           # Solution file (6 projects)
├── .editorconfig          # Single source of truth for code style
├── src/
│   ├── App/               # Entry point (exe). DI wiring only.
│   ├── CLI/               # Spectre.Console.Cli commands. No service logic.
│   │   ├── Azure/         # translate, docintel, vision, stt, ner, phrases
│   │   ├── Audio/         # sacd-convert, dsd-convert
│   │   ├── Dashboard/     # generate + OCI deploy
│   │   └── Sync/          # youtube, lastfm
│   ├── Core/              # Telemetry, errors, path resolution, text utils.
│   └── Services/
│       ├── Audio/         # SACD ISO extraction + DSD→FLAC (sacd_extract, saracon, sox)
│       ├── Azure/         # Azure AI SDKs (Vision, Translate, Speech, DocIntel, OpenAI, TextAnalytics)
│       ├── Google/        # YouTube API + orchestration + dashboard. Depends on Azure.TranslateService.
│       └── LastFm/        # Last.fm HTTP client + sync orchestrator + state.
├── state/                 # Persisted data (youtube/, lastfm/, dashboard/)
├── logs/                  # Per-service JSONL logs (rolling, 7-day retention).
├── Directory.Build.props
└── Directory.Packages.props
```

## DEPENDENCY GRAPH

```
App → CLI, Core
CLI → Core, Services.Azure, Services.Google, Services.LastFm, Services.Audio
Services.Google → Core, Services.Azure  (cross-service: YouTubeTranslationService → TranslateService)
Services.Audio → Core
Services.Azure → Core
Services.LastFm → Core
```

## WHERE TO LOOK

| Task                       | Location                                          | Notes                                                                |
| -------------------------- | ------------------------------------------------- | -------------------------------------------------------------------- |
| Add CLI command            | `src/CLI/{Domain}/`                               | Follow Spectre pattern: thin command → service call → Result.Match   |
| Add Azure service          | `src/Services/Azure/`                             | Add credential to AzureCredentials.cs, register in AzureSetup.cs     |
| Add Google/YouTube feature | `src/Services/Google/YouTube/`                    | Orchestrator handles state; processor handles per-playlist logic     |
| Add Last.fm feature        | `src/Services/LastFm/`                            | LastFmApiClient for HTTP, LastFmSyncOrchestrator for sync flow       |
| Add audio conversion       | `src/Services/Audio/`                             | DsdConvertService is facade; PipelineOrchestrator sequences          |
| Dashboard generation       | `src/CLI/Dashboard/`                              | DashboardDataBuilder → DashboardHtmlGenerator → OciDashboardDeployer |
| Modify telemetry           | `src/Core/Telemetry.cs`                           | Per-service JSONL + optional Seq sink                                |
| Add error codes            | `src/Core/Errors.cs`                              | Central taxonomy; add factory method per domain                      |
| Change build config        | `Directory.Build.props`                           | Single source for TargetFramework, analyzers, warnings               |
| Change code style          | `.editorconfig`                                   | Naming, var usage, patterns, diagnostics — all as errors             |

## CONVENTIONS

- **Auth:** `.env` only. No hardcoded secrets. `AzureCredentials.Read()`, `GoogleCredentials.Read()`, env vars in LastFmSetup.
- **DI registration:** Extension methods using C# `extension(IServiceCollection)` syntax in each service's `*Setup.cs`.
- **Error handling:** `ErrorOr<T>` railway-oriented. `result.Match(onSuccess, onError)`. Error factories in `Errors.cs`.
- **JSON:** PascalCase properties. `JsonSerializerOptions { WriteIndented = true }` only. No `PropertyNamingPolicy`.
- **Logging:** `Telemetry.ForService(ServiceName.X)` scopes log entries. JSONL per service in `logs/`.
- **State:** `state/youtube/manifest.json`, `state/lastfm/scrobbles.json`, `state/dashboard/`. No database.
- **One class per file.** No `Constants.cs`, no `Helpers.cs`. Extract to shared file only when 3+ consumers.
- **Inline constants:** `private static readonly string` at top of file.
- **Code style:** `.editorconfig` is the single source of truth. All rules enforced as `error` severity.

## RULES

1. **Build-verify every edit.** Change one file → `dotnet build` → verify clean.
2. **Commit after each phase.** 1–3 files per commit. Atomic, revertable, descriptive message.
3. **Minimize file sprawl.** One class per file. No `Constants.cs`, no `Helpers.cs`.
4. **No test NuGet packages.** No xUnit, NUnit, MSTest. Standalone `.cs` files with `Main()` for manual verification.
5. **`Directory.Build.props`/`.csproj` exclusively.** No `Directory.Build.targets`, no extra props files.
6. **Never skip style warnings.** No `#pragma warning disable`, no suppression attributes.
7. **PascalCase JSON — never set PropertyNamingPolicy.**
8. **Inline paths, keys, defaults.** `private static readonly string` at top of file.
9. **Zero inline/explanatory comments.** Code is self-documenting. XML docs only where required.

## ANTI-PATTERNS (THIS PROJECT)

- **NEVER** `global::` — use `using` aliases.
- **NEVER** fully-qualified invocations inline — import via `using`.
- **NEVER** `#pragma warning disable` or suppression attributes.
- **NEVER** `PropertyNamingPolicy` on `JsonSerializerOptions`.
- **NEVER** test NuGet packages (xUnit, NUnit, MSTest). Standalone `.cs` with `Main()` only.
- **NEVER** `Directory.Build.targets` or extra props files.
- **NEVER** inline/explanatory comments.

## COMMANDS

```bash
dotnet build                          # Build all projects
dotnet run --project src\App -- <cmd> # Run CLI command
dotnet run --project src\App -- sync youtube
dotnet run --project src\App -- sync lastfm
dotnet run --project src\App -- audio sacd-convert <iso>
dotnet run --project src\App -- azure translate
dotnet run --project src\App -- dashboard generate
```

## NOTES

- .NET 11.0 preview SDK required. `SuppressNETCoreSdkPreviewMessage` is set.
- `<UseArtifactsOutput>true</UseArtifactsOutput>` — outputs in `artifacts/`, not `bin/`.
- `.editorconfig` exists at repo root. All style rules enforced as errors.
- `Toolbox.slnx` is the solution file (SDK-style, 6 projects).
- No CI/CD pipeline. Builds are manual.
- No test projects. Manual verification via standalone `.cs` files with `Main()`.
- Sub-AGENTS.md files exist in `src/CLI/`, `src/Core/`, `src/Services/Audio/`, `src/Services/Azure/`, `src/Services/Google/`, `src/Services/LastFm/` for domain-specific guidance.
