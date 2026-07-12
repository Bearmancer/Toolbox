# AGENTS.md — Toolbox

**Generated:** 2026-06-30 | **Commit:** 093dc80 | **Branch:** master

Extends `C:\Users\Lance\.config\opencode\AGENTS.md`. All Sisyphus directives apply.

## OVERVIEW

CLI toolbox wrapping Azure AI services, Google YouTube API, and Last.fm. .NET 10.0, Spectre.Console.Cli, Serilog, ErrorOr.

## STRUCTURE

```
New/
├── src/
│   ├── App/            # Entry point (exe). DI wiring only.
│   ├── CLI/            # Spectre.Console.Cli commands. No service logic.
│   ├── Core/           # Telemetry, errors, path resolution, text utils.
│   └── Services/
│       ├── Azure/      # Azure AI SDKs (Vision, Translate, Speech, DocIntel, OpenAI, TextAnalytics).
│       ├── Google/     # YouTube API + orchestration. Depends on Azure.TranslateService.
│       └── LastFm/     # Last.fm HTTP client + state.
├── state/              # Persisted data (youtube/{raw,processed,deleted}, lastfm/).
├── logs/               # Per-service JSONL logs (rolling, 7-day retention).
├── Directory.Build.props
└── Directory.Packages.props
```

## DEPENDENCY GRAPH

```
App → CLI, Core
CLI → Core, Services.Azure, Services.Google, Services.LastFm
Services.Google → Core, Services.Azure  (cross-service: YouTubeTranslationService → TranslateService)
Services.Azure → Core
Services.LastFm → Core
```

**Note:** Google → Azure is a lateral dependency. YouTubeTranslationService calls TranslateService directly.

## WHERE TO LOOK

| Task                       | Location                       | Notes                                                              |
| -------------------------- | ------------------------------ | ------------------------------------------------------------------ |
| Add CLI command            | `src/CLI/{Domain}/`            | Follow Spectre pattern: thin command → service call → Result.Match |
| Add Azure service          | `src/Services/Azure/`          | Add credential to AzureCredentials.cs, register in AzureSetup.cs   |
| Add Google/YouTube feature | `src/Services/Google/YouTube/` | Orchestrator handles state; processor handles per-playlist logic   |
| Add Last.fm feature        | `src/Services/LastFm/`         | Simple HTTP client pattern                                         |
| Modify telemetry           | `src/Core/Telemetry.cs`        | Per-service JSONL + optional Seq sink                              |
| Add error codes            | `src/Core/Errors.cs`           | Central taxonomy; add factory method per domain                    |
| Change build config        | `Directory.Build.props`        | Single source for TargetFramework, analyzers, warnings             |

## CONVENTIONS

- **Auth:** `.env` only. No hardcoded secrets. `AzureCredentials.Read()`, `GoogleCredentials.Read()`, env vars in LastFmSetup.
- **DI registration:** Extension methods using C# `extension(IServiceCollection)` syntax in each service's `*Setup.cs`.
- **Error handling:** `ErrorOr<T>` railway-oriented. `result.Match(onSuccess, onError)`. Error factories in `Errors.cs`.
- **JSON:** PascalCase properties. `JsonSerializerOptions { WriteIndented = true }` only. No `PropertyNamingPolicy`.
- **Logging:** `Telemetry.ForService(ServiceName.X)` scopes log entries. JSONL per service in `logs/`.
- **State:** `state/youtube/manifest.json` is the manifest. Raw/processed/deleted subdirs. No database.
- **One class per file.** No `Constants.cs`, no `Helpers.cs`. Extract to shared file only when 3+ consumers.
- **Inline constants:** `private static readonly string` at top of file.

## RULES

1. **Build-verify every edit.** Change one file → `dotnet build` → verify clean. No scattershot multi-file edits before building.
2. **Commit after each phase.** 1–3 files per commit. Atomic, revertable, descriptive message.
3. **Minimize file sprawl.** One class per file. No `Constants.cs`, no `Helpers.cs`. Extract to shared file only when 3+ consumers exist.
4. **No test NuGet packages.** No xUnit, NUnit, MSTest. Standalone `.cs` files with `Main()` for manual verification.
5. **`Directory.Build.props`/`.csproj` exclusively.** No `Directory.Build.targets`, no extra props files. `.csproj` inherits from `Directory.Build.props`; only `<RootNamespace>` and package refs — no redundant `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>`.
6. **Never skip style warnings.** No `#pragma warning disable`, no suppression attributes. `.editorconfig` is the single source of truth. Fix the code or update `.editorconfig` with justification.
7. **PascalCase JSON — never set PropertyNamingPolicy.** C# public properties are PascalCase. Omit `PropertyNamingPolicy` so JSON keys equal C# property names — no translation layer. `new JsonSerializerOptions { WriteIndented = true }` only. No analyzer exists for this; enforced by convention.
8. **Inline paths, keys, defaults.** `private static readonly string` at top of file. Extract to shared file only when 2+ consumers need the same value.
9. **Zero inline/explanatory comments.** Code is self-documenting. XML docs only where required.

## ANTI-PATTERNS (THIS PROJECT)

- **NEVER** `global::` — use `using` aliases.
- **NEVER** fully-qualified invocations inline — import via `using`.
- **NEVER** `#pragma warning disable` or suppression attributes — fix code or update `.editorconfig`.
- **NEVER** `PropertyNamingPolicy` on `JsonSerializerOptions`.
- **NEVER** test NuGet packages (xUnit, NUnit, MSTest). Standalone `.cs` with `Main()` only.
- **NEVER** `Directory.Build.targets` or extra props files.
- **NEVER** inline/explanatory comments. Code is self-documenting. XML docs only where required.

## COMMANDS

```bash
dotnet build                          # Build all projects
dotnet run --project src\App -- <cmd> # Run CLI command
dotnet run --project src\App -- sync youtube
dotnet run --project src\App -- azure translate
```

## NOTES

- .NET 10.0 preview SDK required. `SuppressNETCoreSdkPreviewMessage` is set.
- `<UseArtifactsOutput>true</UseArtifactsOutput>` — outputs in `artifacts/`, not `bin/`.
- No `.editorconfig` exists. Style enforced by convention + AGENTS.md rules.
- No CI/CD pipeline. Builds are manual.
- No test projects. Manual verification via standalone `.cs` files with `Main()`.
