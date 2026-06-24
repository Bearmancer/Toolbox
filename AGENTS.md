# AGENTS.md — AzureAI

Extends `C:\Users\Lance\.config\opencode\AGENTS.md`. All Sisyphus directives apply.

## Architecture

```
App (exe) → CLI (commands) → Services (Azure + Google) → Core (base)
```

- **Core**: shared models, Telemetry (Serilog), utilities. No project refs.
- **Services.Azure**: 8 Azure AI SDK wrappers. Primary constructor injection.
- **Services.Google**: YouTube Data API v3 via `Google.Apis.YouTube.v3`. OAuth via `GoogleWebAuthorizationBroker`.
- **CLI**: Spectre.Console.Cli 0.55.0. Commands under `CLI.Azure.*`, `CLI.Google.YouTube.*`.
- **App**: entry point. Loads `.env` via DotNetEnv, wires DI, creates `CommandApp`.

## Anti-Patterns

- **NEVER** use `global::` — use `using` aliases instead for disambiguation
- **NEVER** use fully-qualified method invocations inline (e.g., `Google.Apis.Requests.BatchRequest`). Always import via `using` directive or `using` alias
- **Using aliases** ARE allowed for namespace disambiguation when shadowing occurs (e.g., `using GoogleRequests = Google.Apis.Requests;`)
- **NEVER** name project namespaces after NuGet package root namespaces (e.g., `Services.Google` shadows `Google.Apis.*`). If unavoidable, use `using` aliases for the conflicting package

## Design

- Migrating features from `../Old` — do **not** copy files directly; integrate meaningfully.
- Do **not** delete unused methods without QA tool confirmation.
- **Always** use QA tool for multi-option decisions and removals. Each option must include rationale, pros, and cons.
- All service logic lives in `src/Services`. CLI contains no service logic.
- Maximize simplicity. Justify every abstraction or orchestration layer explicitly.
- Logging is per-service JSONL only — no single-file raw dump.
- State persisted to `state/` at `Directory.GetCurrentDirectory()`. Subdirs: `state/youtube/{raw,playlists,deleted}/`. `state/youtube/manifest.json` is the manifest. No database.

## Logging Pipeline

```
App code → Telemetry.Info/Debug/Warn/Error → Serilog → Sinks:
                                                    ├── Spectre.Console (rich terminal)
                                                    ├── logs/app.jsonl      (all, compact JSON)
                                                    ├── logs/azure.jsonl    (filtered: Service=Azure)
                                                    ├── logs/google.jsonl   (filtered: Service=Google)
                                                    └── Seq :5341           (if TCP reachable)
```

- **Telemetry** wraps Serilog static methods — `Info()`, `Debug()`, `Warn()`, `Error()`, `StartActivity()`.
- **Spectre sink** renders log events via Spectre.Console markup (`[green]`, `[red]` etc.).
- **`ForService("Google")`** pushes a `Service` property into `LogContext` for sink filtering and per-service JSONL files.
- **`--verbose` flag in `Program.cs` calls `Telemetry.Configure(debug: true)`, setting `MinimumLevel` to `Debug`.
- **File sinks** write per-service compact JSONL to `logs/`, rolling daily, retained 7 days.
- **Seq** is conditional — checked via TCP handshake on startup; skipped silently if unreachable.
- Orchestrator uses `Telemetry.Debug()` directly — Serilog handles level filtering internally. No `if (IsDebugEnabled)` guards.

## Solo-Dev Rules

1. **Build-verify every edit.** Change one file → `dotnet build` → verify clean. No scattershot multi-file edits before building.
2. **Commit after each phase.** 1–3 files per commit. Atomic, revertable, descriptive message.
3. **Minimize file sprawl.** One class per file. No `Constants.cs`, no `Helpers.cs`. Extract to shared file only when 3+ consumers exist.
4. **No test NuGet packages.** No xUnit, NUnit, MSTest. Standalone `.cs` files with `Main()` for manual verification.
5. **`Directory.Build.props`/`.csproj` exclusively.** No `Directory.Build.targets`, no extra props files. `.csproj` inherits from `Directory.Build.props`; only `<RootNamespace>` and package refs — no redundant `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>`.
6. **Never skip style warnings.** No `#pragma warning disable`, no suppression attributes. `.editorconfig` is the single source of truth. Fix the code or update `.editorconfig` with justification.

## C# Conventions

8. **PascalCase for everything except locals/parameters.** No underscore prefixes. Enforced by `.editorconfig` lines 129–167.
9. **`new()` when type is apparent.** `Foo x = new()`, not `var x = new Foo()`. Enforced by IDE0090.
10. **Collection expressions.** `[1, 2, 3]`, not `new List<int> { 1, 2, 3 }`. Enforced by IDE0300–0306.
11. **Primary constructors for DI.** All services and commands. Enforced by IDE0290.
12. **Records for DTOs.** Immutable data → `record` or `record struct`. `init` for optional fields.
13. **Expression-bodied for single-line.** Properties, accessors, lambdas → `=>`. Methods → only when single-line. Constructors → never. Enforced by `.editorconfig` lines 199–205.
14. **Switch expressions over switch statements.** Enforced by IDE0066.
15. **File-scoped namespaces.** `namespace Foo;`, never `namespace Foo { ... }`. Enforced by `.editorconfig` line 211.
16. **PascalCase JSON — never set PropertyNamingPolicy.** C# public properties are PascalCase. Omit `PropertyNamingPolicy` so JSON keys equal C# property names — no translation layer. `new JsonSerializerOptions { WriteIndented = true }` only. No analyzer exists for this; enforced by convention.

## Null Handling

16. **`is null` over `== null`.** `is { }` over `!= null` where the bound variable is used.
17. **`??` over ternary null checks.** `x ?? "default"`, not `x is null ? "default" : x`.
18. **`??=` over `if (x is null) x = y`.**
19. **`?.` for safe navigation.** `obj?.Property`, not `if (obj is not null) obj.Property`.

## Type Choices

20. **Enum over string.** For any finite set of values. Never switch on string literals. Exception: environment variable keys and API endpoint URLs.
21. **`foreach` only.** Never `for` with an index variable.
22. **Static when no instance needed.** Mark methods `static` when they don't access instance state. Enforced by CA1822.

## File-Level Constants

23. **Inline paths, keys, defaults.** `private static readonly string` at top of file. Extract to shared file only when 3+ consumers need the same value.

```csharp
public class YoutubeService(YouTubeService yt)
{
    static readonly string StateRoot = Path.Combine(Directory.GetCurrentDirectory(), "state", "youtube");
}
```

## Credentials & Paths

24. **All keys/secrets in `.env`.** Never hardcode. `.env` is gitignored (line 7 of `.gitignore`). Loaded via DotNetEnv in `Program.cs`. Google OAuth uses system env vars (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`).
25. **`AppContext.BaseDirectory` for repo-relative paths.** Never `Directory.GetCurrentDirectory()`.

## Comments

26. **Zero inline/explanatory comments.** Code is self-documenting.