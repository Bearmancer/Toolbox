# AGENTS.md — Toolbox

Extends `C:\Users\Lance\.config\opencode\AGENTS.md`. All Sisyphus directives apply. NEVER WRITE PARAGRAPHS OF EXPLANATIONS OR OPTIONS. ALWAYS USE QA EXCLUSIVELY WITH EXPANSIVE EXPLANATIONS FOR ALL OPTIONS ALONGSIDE THEIR RATIONALE, PROS AND CONS. Utilize Codegraph liberally. 

## Architecture

### Authentication

Relies exclusively on .env for holding all endpoints and secrets. i.e. not hardcoding them inside classes. 

### Workflow: 
```
App (exe) → CLI (commands) → Services (Azure + Google) → Core (base)
```

### Namespaces:
- **App**: entry point. Does not deal with CLI logic. Only adds CLI services one at a time.
- **Core**: Telemetry (Serilog), utilities.
- **CLI**: Exclusively deals with CLI-related logic. Do **not** insert service logic in their CLI layer.
- **Service**: Core logic of all services. Do not muddle by inserting CLI logic.

## Anti-Patterns

- **NEVER** use `global::` — use `using` aliases instead for disambiguation
- **NEVER** use fully-qualified method invocations inline (e.g., `Google.Apis.Requests.BatchRequest`). Always import via `using` directive or `using` aliases when needed to resolve conflicts.

## Design

- Maximize simplicity. Justify every abstraction or orchestration layer explicitly.
- Logging is per-service JSONL only — no single-file raw dump.
- State persisted to `state/` at `Directory.GetCurrentDirectory()`. Subdirs: `state/youtube/{raw,playlists,deleted}/`. `state/youtube/manifest.json` is the manifest. No database.

## Rules

1. **Build-verify every edit.** Change one file → `dotnet build` → verify clean. No scattershot multi-file edits before building.
2. **Commit after each phase.** 1–3 files per commit. Atomic, revertable, descriptive message.
3. **Minimize file sprawl.** One class per file. No `Constants.cs`, no `Helpers.cs`. Extract to shared file only when 3+ consumers exist.
4. **No test NuGet packages.** No xUnit, NUnit, MSTest. Standalone `.cs` files with `Main()` for manual verification.
5. **`Directory.Build.props`/`.csproj` exclusively.** No `Directory.Build.targets`, no extra props files. `.csproj` inherits from `Directory.Build.props`; only `<RootNamespace>` and package refs — no redundant `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>`.
6. **Never skip style warnings.** No `#pragma warning disable`, no suppression attributes. `.editorconfig` is the single source of truth. Fix the code or update `.editorconfig` with justification.
7. **PascalCase JSON — never set PropertyNamingPolicy.** C# public properties are PascalCase. Omit `PropertyNamingPolicy` so JSON keys equal C# property names — no translation layer. `new JsonSerializerOptions { WriteIndented = true }` only. No analyzer exists for this; enforced by convention.

## File-Level Constants

8. **Inline paths, keys, defaults.** `private static readonly string` at top of file. Extract to shared file only when 2 or more consumers need the same value.

```csharp
public class YoutubeService(YouTubeService yt)
{
    static readonly string StateRoot = Path.Combine(Directory.GetCurrentDirectory(), "state", "youtube");
}
```

## Comments

9. **Zero inline/explanatory comments.** Code is self-documenting.
