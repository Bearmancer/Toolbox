# Console & Logging Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate Serilog/Spectre rendering conflicts, stop config/env-var sprawl across static God classes, and unify all user-visible terminal output under a new `Core.Console` namespace whose single dispatch point writes to both the terminal (Spectre) and structured log (Serilog) from one call site.

**Architecture:** A new `Core/Console/` directory contains three focused files: `Out.cs` (text output + dual dispatch), `Bar.cs` (progress bars), and `Table.cs` (grids). `AppConfig` becomes a pure env-var reader with no framework types. Serilog-specific config moves to a new `LogConfig` internal class inside `Core.Logging`. The Serilog `WriteTo.Console` sink is deleted permanently. `ServiceContext.Instance` and `LogPipeline`'s env reads are redirected through `AppConfig`.

**Tech Stack:** C# 12, .NET 8, Serilog 4.3 (file + Seq sinks only after this plan), Spectre.Console 0.57, DotNetEnv 3.1

**Tests:** Foregone per project preference. Verification is via `dotnet build` and CLI smoke runs.

---

## Context: Old → New Migration

This plan operates exclusively on the **New** project (`src/`). The **Old** project is a separate solution undergoing incremental migration. Do not treat any type in `src/` as dead code solely because it has no active callers _yet_ — the migration is ongoing. `Ui.cs` must not be deleted until every call site within `src/` is verified migrated (Task 13).

---

## Architecture: The Full Dispatch Flow

```
                         ONE CALL SITE
                         ─────────────
            Out.Warn("Rate limit hit. Retry in 30s.")
                              │
                 ┌────────────┴────────────┐
                 │                         │
           ALWAYS                     if print==true
                 │                         │
                 ▼                         ▼
   Serilog.Log.Write(Warning,     Markup.Escape(msg)
     "{ConsoleMessage}", msg)              │
                 │               AnsiConsole.MarkupLine
                 │                "[yellow]{escaped}[/]"
       ┌─────────┼─────────┐              │
       ▼         ▼         ▼              ▼
   File sink   Seq sink  (future)    Terminal
   .jsonl      :5341                 yellow text
   plain text  JSON                 ANSI codes
   no ANSI     no ANSI              human readable
```

---

## Architecture: Level Gating — Two Independent Gates

```
 LOG_LEVEL=debug
      │
      └──► AppConfig.RawLogLevel = "debug"
                    │
         ┌──────────┴──────────┐
         │                     │
         ▼                     ▼
  LogConfig                  Out.Verbose
  LevelSwitch=Debug           = true
         │ (Serilog gate)      │ (terminal gate)
  Verbose → PASS         Trace() → prints  ✓
  Debug   → PASS         Debug() → prints  ✓
  Info    → PASS         Info()  → ALWAYS  ✓
  Warn    → PASS         Warn()  → ALWAYS  ✓
  Error   → PASS         Fail()  → ALWAYS  ✓


 LOG_LEVEL=warn
      │
      └──► AppConfig.RawLogLevel = "warn"
                    │
         ┌──────────┴──────────┐
         │                     │
         ▼                     ▼
  LogConfig                  Out.Verbose
  LevelSwitch=Warning         = false
         │ (Serilog gate)      │ (terminal gate)
  Verbose → DROP         Trace() → silent  ✗
  Debug   → DROP         Debug() → silent  ✗
  Info    → DROP         Info()  → ALWAYS  ✓  ← user always sees results
  Warn    → PASS         Warn()  → ALWAYS  ✓
  Error   → PASS         Fail()  → ALWAYS  ✓
```

> `Out.Info()` always prints to terminal regardless of LOG_LEVEL — it is a user-visible result, not a telemetry entry. The two gates are fully independent.

---

## Architecture: Namespace — `Core.Console` vs `System.Console`

```
 System.Console          ← a CLASS  (static, in mscorlib)
                             ├── Console.WriteLine()
                             ├── Console.Error
                             └── Console.Out

 Core.Console            ← a NAMESPACE  (not a class, not a type)
                             ├── class Out    (Core.Console.Out)
                             ├── class Bar    (Core.Console.Bar)
                             └── class Table  (Core.Console.Table)

 ZERO COLLISION:
   using System;          → brings System.Console (class) into bare scope
   using Core.Console;    → brings Out, Bar, Table into bare scope
                            NOT the name "Console" — namespaces are not
                            imported as names, only their member types are

   Console.WriteLine()   → resolves to System.Console  ✓
   Out.Info("msg")       → resolves to Core.Console.Out ✓
```

In practice: `Out.cs`, `Bar.cs`, `Table.cs` never call `System.Console` — they use `AnsiConsole` (Spectre) only.

---

## Architecture: Class Naming

```
 AppConfig      "App"    = application-wide scope
                "Config" = reads and exposes configuration
                one job  : read env vars → plain C# primitives
                rule     : ONLY place in src/ that calls GetEnvironmentVariable

 LogConfig      "Log"    = logging concern (Serilog)
                "Config" = configuration derived from AppConfig strings
                one job  : RawLogLevel → LoggingLevelSwitch
                           RawLogOverrides → Dictionary<string, LogEventLevel>
                visibility: internal — nothing outside Core.Logging sees it

 LogPipeline    "Log"    = logging concern
                "Pipeline" = assembly of sinks into a working logger
                one job  : construct Serilog.Logger (file + Seq, NO Console)

 Out            unix-style; think stdout, cout, print
                one job  : write one line of text to terminal + Serilog
                methods  : Success / Fail / Warn / Info / Debug / Trace

 Bar            short for "progress bar"
                one job  : render a Spectre progress bar while work runs

 Table          exactly what it is
                one job  : render a grid of rows and columns
```

---

## Architecture: Full Picture

```
 ENV FILE (.env)
 ════════════════════════════════════════════════════════════════════
 LOG_LEVEL=debug
 LOG_OVERRIDES=Microsoft=warn
 SEQ_URL=http://localhost:5341
 SEQ_INSTANCE=dev
 ════════════════════════════════════════════════════════════════════
                              │
                       DotNetEnv.Load()
                              │
                              ▼
                        AppConfig  ◄──────── ONLY env reader
                    ┌─────────────────┐
                    │ RawLogLevel     │
                    │ RawLogOverrides │
                    │ SeqUrl          │
                    │ SeqInstance     │
                    └────────┬────────┘
             ┌───────────────┼───────────────┐
             ▼               ▼               ▼
       LogConfig        LogPipeline     ServiceContext
    ┌──────────────┐   ┌────────────┐   ┌────────────┐
    │ LevelSwitch  │   │ .WriteTo   │   │ Instance   │
    │ Overrides    │   │   .File()  │   │(=SeqInst)  │
    └──────────────┘   │   .Seq()   │   └────────────┘
           │           │ NO Console │
           │           └────────────┘
           └────────┬────────┘
                    │
             Out.Verbose=true/false
                    │
                    ▼
            ┌───────────────┐
            │  Core.Console │  ← NEW namespace
            │               │
            │  Out.Success() ├──── green  → terminal + Log(Info)
            │  Out.Fail()    ├──── red    → terminal + Log(Error)
            │  Out.Warn()    ├──── yellow → terminal + Log(Warning)
            │  Out.Info()    ├──── white  → terminal + Log(Info)
            │  Out.Debug()   ├──── grey   → terminal* + Log(Debug)
            │  Out.Trace()   ├──── dim    → terminal* + Log(Verbose)
            │               │     * only if Out.Verbose==true
            │  Bar.RunAsync()├──── Spectre Progress (no Out.* inside!)
            │  Table.Render()├──── Spectre Table
            └───────────────┘
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
    AnsiConsole          Serilog.Log
    (Spectre)                 │
    terminal            ┌─────┴──────┐
    ANSI colour         ▼            ▼
    escaped           File          Seq
                      .jsonl        :5341


 DELETED / GONE:
 ════════════════════════════════════════════════════════════════════
  ✗  WriteTo.Console                  (corrupted Spectre renders)
  ✗  AzureEventSourceListener         (ETW flood bypassed everything)
  ✗  Core.Ui (7x Console.WriteLine)   (replaced by Out)
  ✗  AppConfig.LogSwitch              (Serilog type in domain layer)
  ✗  ServiceContext env read          (duplicate SEQ_INSTANCE)
  ✗  Serilog.Sinks.Console package    (removed entirely)
```

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| **Create** | `src/Core/Console/Out.cs` | Text output + dual dispatch (Spectre + Serilog) |
| **Create** | `src/Core/Console/Bar.cs` | Progress bar facade over `AnsiConsole.Progress` |
| **Create** | `src/Core/Console/Table.cs` | Table/grid facade over `Spectre.Console.Table` |
| **Create** | `src/Core/Logging/LogConfig.cs` | Serilog-specific runtime config extracted from AppConfig |
| **Modify** | `src/Core/AppConfig.cs` | Strip Serilog types; centralize all env reads |
| **Modify** | `src/Core/Logging/LogPipeline.cs` | Remove `WriteTo.Console`; consume `LogConfig`/`AppConfig` |
| **Modify** | `src/Core/ServiceContext.cs` | Read `Instance` from `AppConfig.SeqInstance` |
| **Modify** | `src/Core/CommandBase.cs` | Replace 4 bare `AnsiConsole.MarkupLine` with `Out.*` |
| **Modify** | `src/App/Program.cs` | Replace 1 bare `AnsiConsole.MarkupLine` with `Out.Fail` |
| **Modify** | `src/CLI/Azure/AzureCommandModule.cs` | Remove `AzureEventSourceListener.CreateConsoleLogger` |
| **Modify** | `src/Core/Infrastructure/TypeResolver.cs` | Replace `Console.WriteLine` with `Serilog.Log.Warning` |
| **Migrate** | 11 `src/CLI/Azure/*.cs` command files | `Ui.Info(result)` → `Out.Info(result)` |
| **Delete** | `src/Core/Ui.cs` | Superseded — deleted only after call sites migrated |
| **Modify** | `src/Core/Core.csproj` + `Directory.Packages.props` | Remove `Serilog.Sinks.Console` |

---

## Tasks

---

### Task 1: Create `src/Core/Logging/LogConfig.cs`

Extract Serilog-specific runtime config from `AppConfig`. Must exist before Task 2 removes those members.

**Files:**
- Create: `src/Core/Logging/LogConfig.cs`

- [ ] **Step 1: Create the file**

```csharp
// src/Core/Logging/LogConfig.cs
using Serilog.Core;
using Serilog.Events;

namespace Core.Logging;

/// <summary>
/// Serilog-specific runtime configuration derived from AppConfig raw strings.
/// Owns the LoggingLevelSwitch and per-category override map.
/// All Serilog types are confined to this class and LogPipeline.
/// AppConfig has no knowledge of Serilog after this refactor.
/// </summary>
internal static class LogConfig
{
    internal static LoggingLevelSwitch LevelSwitch { get; }
    internal static IReadOnlyDictionary<string, LogEventLevel> Overrides { get; }

    static LogConfig()
    {
        LevelSwitch = new LoggingLevelSwitch(ParseLevel(AppConfig.RawLogLevel));
        Overrides   = ParseOverrides(AppConfig.RawLogOverrides);
    }

    /// <summary>
    /// Parses "debug", "warn", "trace", etc. into a Serilog LogEventLevel.
    /// Case-insensitive. Defaults to Debug on unrecognised input.
    /// </summary>
    internal static LogEventLevel ParseLevel(string? raw)
    {
        if (raw is not null && LevelAliases.TryGetValue(raw.Trim(), out var level))
            return level;
        return LogEventLevel.Debug;
    }

    private static readonly IReadOnlyDictionary<string, LogEventLevel> LevelAliases =
        new Dictionary<string, LogEventLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["verbose"]     = LogEventLevel.Verbose,
            ["trace"]       = LogEventLevel.Verbose,  // "trace" maps to Serilog Verbose
            ["debug"]       = LogEventLevel.Debug,
            ["info"]        = LogEventLevel.Information,
            ["information"] = LogEventLevel.Information,
            ["warn"]        = LogEventLevel.Warning,
            ["warning"]     = LogEventLevel.Warning,
            ["error"]       = LogEventLevel.Error,
            ["fatal"]       = LogEventLevel.Fatal,
            ["critical"]    = LogEventLevel.Fatal,
        };

    private static Dictionary<string, LogEventLevel> ParseOverrides(string? raw)
    {
        var result = new Dictionary<string, LogEventLevel>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1) continue;
            result[pair[..eq].Trim()] = ParseLevel(pair[(eq + 1)..]);
        }
        return result;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Core/Logging/LogConfig.cs
git commit -m "feat(logging): extract LogConfig from AppConfig — Serilog types confined to Logging namespace"
```

---

### Task 2: Refactor `AppConfig` — pure env reader, plain types only

**Files:**
- Modify: `src/Core/AppConfig.cs`

- [ ] **Step 1: Replace the entire file**

```csharp
// src/Core/AppConfig.cs
namespace Core;

/// <summary>
/// Single source of truth for all environment variable reads in the New project.
/// Exposes plain C# primitive types only — no Serilog, no Spectre, no framework types.
///
/// Rule: never call Environment.GetEnvironmentVariable anywhere else in src/.
///
/// Consumers:
///   Core.Logging.LogConfig  — RawLogLevel, RawLogOverrides → Serilog types
///   Core.Logging.LogPipeline — SeqUrl, SeqInstance
///   Core.Console.Out        — RawLogLevel → bool Verbose
///   Core.ServiceContext     — SeqInstance
/// </summary>
public static class AppConfig
{
    /// <summary>Raw LOG_LEVEL string (e.g. "debug", "warn", "trace"). Null = not set.</summary>
    public static string? RawLogLevel { get; }

    /// <summary>Raw LOG_OVERRIDES string (e.g. "Microsoft=warn,System.Net=error"). Null = none.</summary>
    public static string? RawLogOverrides { get; }

    /// <summary>SEQ_URL if configured. Null disables the Seq sink entirely.</summary>
    public static string? SeqUrl { get; }

    /// <summary>Instance label for log enrichment. Defaults to "local".</summary>
    public static string SeqInstance { get; }

    static AppConfig()
    {
        RawLogLevel     = Environment.GetEnvironmentVariable("LOG_LEVEL");
        RawLogOverrides = Environment.GetEnvironmentVariable("LOG_OVERRIDES");
        SeqUrl          = Environment.GetEnvironmentVariable("SEQ_URL");
        SeqInstance     = Environment.GetEnvironmentVariable("SEQ_INSTANCE") ?? "local";
    }
}
```

- [ ] **Step 2: Build — expect errors in `LogPipeline.cs`**

```
dotnet build src/Core/Core.csproj
```

Expected: errors referencing removed `AppConfig.LogSwitch` and `AppConfig.Overrides`. Fixed in Task 3. Do not commit yet.

---

### Task 3: Update `LogPipeline` — remove Console sink, consume `LogConfig` and `AppConfig`

**Files:**
- Modify: `src/Core/Logging/LogPipeline.cs`

- [ ] **Step 1: Replace the entire file**

```csharp
// src/Core/Logging/LogPipeline.cs
using Core.Console;
using Serilog.Debugging;

namespace Core.Logging;

public static class LogPipeline
{
    private static readonly string AppDataLogRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache",
        "logs"
    );

    /// <summary>
    /// Configures Serilog with file and (optionally) Seq sinks.
    ///
    /// NO Console sink — terminal output is exclusively via Core.Console.Out.
    /// Serilog's WriteTo.Console corrupts Spectre.Console live renders (progress
    /// bars, live displays). Removing it is permanent.
    ///
    /// Sets Out.Verbose from the resolved log level so Out.Debug / Out.Trace
    /// gate correctly on the terminal without a separate flag.
    /// </summary>
    public static void Configure(string applicationName, LogEventLevel? minimumLevel = null)
    {
        var logDir = Path.Combine(AppDataLogRoot, applicationName.ToLowerInvariant());
        Directory.CreateDirectory(logDir);

        if (minimumLevel is { } explicitLevel)
            LogConfig.LevelSwitch.MinimumLevel = explicitLevel;

        var levelSwitch = LogConfig.LevelSwitch;
        var config = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch);

        foreach (var (category, level) in LogConfig.Overrides)
            config.MinimumLevel.Override(category, level);

        config
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Instance", AppConfig.SeqInstance)
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: Path.Combine(logDir, $"{applicationName.ToLowerInvariant()}-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true
            );

        if (!string.IsNullOrWhiteSpace(AppConfig.SeqUrl))
            config.WriteTo.Seq(AppConfig.SeqUrl, controlLevelSwitch: levelSwitch);

        Serilog.Log.Logger = config.CreateLogger();

#if DEBUG
        // Serilog internal errors (e.g. Seq unreachable) on stderr — debug builds only.
        // In release, this noise is mistaken by users for application errors.
        SelfLog.Enable(Console.Error);
#endif

        // Set terminal gate for Out.Debug / Out.Trace.
        // true when LOG_LEVEL is "debug", "verbose", or "trace".
        Out.Verbose = levelSwitch.MinimumLevel <= LogEventLevel.Debug;
    }

    public static void CloseAndFlush() => Serilog.Log.CloseAndFlush();
}
```

- [ ] **Step 2: Build (expect `Out` not found if Task 5 not yet done)**

```
dotnet build src/Core/Core.csproj
```

If `Out` doesn't exist yet, temporarily comment the `Out.Verbose` line and continue to Task 4. Uncomment after Task 5.

- [ ] **Step 3: Commit Tasks 2 + 3 together once build is clean**

```
git add src/Core/AppConfig.cs src/Core/Logging/LogPipeline.cs
git commit -m "refactor(config): centralize env reads in AppConfig; remove Serilog Console sink"
```

---

### Task 4: Fix `ServiceContext` — eliminate duplicate `SEQ_INSTANCE` read

**Files:**
- Modify: `src/Core/ServiceContext.cs` — lines 19–20

- [ ] **Step 1: Replace the `Instance` property initializer**

From:
```csharp
public string Instance { get; init; } =
    Environment.GetEnvironmentVariable("SEQ_INSTANCE") ?? "local";
```
To:
```csharp
public string Instance { get; init; } = AppConfig.SeqInstance;
```

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors (or only the `Out` error if Task 5 not done yet).

- [ ] **Step 3: Commit**

```
git add src/Core/ServiceContext.cs
git commit -m "fix(config): ServiceContext.Instance reads AppConfig.SeqInstance — removes second SEQ_INSTANCE env read"
```

---

### Task 5: Create `src/Core/Console/Out.cs`

Single dispatch point. Every user-visible message goes through here. Escape gate makes it impossible for any input — including Azure error messages with `[` and `]` — to crash Spectre.

**Files:**
- Create: `src/Core/Console/Out.cs`

- [ ] **Step 1: Create directory**

```
New-Item -ItemType Directory -Force src/Core/Console
```

- [ ] **Step 2: Create the file**

```csharp
// src/Core/Console/Out.cs
using Serilog.Events;
using Spectre.Console;

namespace Core.Console;

/// <summary>
/// Unified terminal output facade. Single dispatch for all user-visible messages.
///
/// Each method dispatches to two independent channels:
///   1. Spectre.Console — ANSI-coloured, escape-safe terminal rendering
///   2. Serilog         — {ConsoleMessage} property in structured log (no ANSI codes)
///
/// Level mapping:
///   Out.Trace()   → dim grey     terminal* + Serilog Verbose
///   Out.Debug()   → grey         terminal* + Serilog Debug
///   Out.Info()    → white        terminal  + Serilog Information
///   Out.Success() → green        terminal  + Serilog Information
///   Out.Warn()    → yellow       terminal  + Serilog Warning
///   Out.Fail()    → red          terminal  + Serilog Error
///   * only when Verbose == true
///
/// Call Out.* for user-visible messages.
/// Call Log.Emit(new SomeEvent()) for machine-only structured telemetry.
/// </summary>
public static class Out
{
    // volatile: written once at startup on main thread, read from any thread.
    private static volatile bool _verbose;

    /// <summary>
    /// When true, Debug() and Trace() print to the terminal.
    /// Set by LogPipeline.Configure() from LOG_LEVEL.
    /// True when LOG_LEVEL is "debug", "verbose", or "trace".
    /// </summary>
    public static bool Verbose
    {
        get => _verbose;
        internal set => _verbose = value;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Green. Serilog Information. Completed operations, saved items.</summary>
    public static void Success(string msg) =>
        Emit("green", msg, LogEventLevel.Information, print: true);

    /// <summary>Red. Serilog Error. Exceptions surfaced to user, fatal failures.</summary>
    public static void Fail(string msg) =>
        Emit("red", msg, LogEventLevel.Error, print: true);

    /// <summary>Yellow. Serilog Warning. Retries, degraded results, non-fatal issues.</summary>
    public static void Warn(string msg) =>
        Emit("yellow", msg, LogEventLevel.Warning, print: true);

    /// <summary>White. Serilog Information. Status updates, neutral messages.</summary>
    public static void Info(string msg) =>
        Emit("white", msg, LogEventLevel.Information, print: true);

    /// <summary>Grey. Serilog Debug. Per-step detail. Terminal only if Verbose.</summary>
    public static void Debug(string msg) =>
        Emit("grey", msg, LogEventLevel.Debug, print: _verbose);

    /// <summary>Dim. Serilog Verbose. Per-item loop output. Terminal only if Verbose.</summary>
    public static void Trace(string msg) =>
        Emit("dim", msg, LogEventLevel.Verbose, print: _verbose);

    // ── Private ─────────────────────────────────────────────────────────────

    private static void Emit(string colour, string msg, LogEventLevel logLevel, bool print)
    {
        // Always dispatch to Serilog. {ConsoleMessage} = plain text, no ANSI, searchable.
        Serilog.Log.Write(logLevel, "{ConsoleMessage}", msg);

        if (!print) return;

        // Escape BEFORE interpolation. Azure error messages, ARM codes, and resource
        // names contain [ and ] — without Escape, Spectre parses them as markup and throws.
        var escaped = Markup.Escape(msg ?? string.Empty);
        AnsiConsole.MarkupLine($"[{colour}]{escaped}[/]");
    }
}
```

- [ ] **Step 3: Un-comment `Out.Verbose` line in `LogPipeline.Configure` if commented**

Confirm `src/Core/Logging/LogPipeline.cs` ends `Configure()` with:
```csharp
Out.Verbose = levelSwitch.MinimumLevel <= LogEventLevel.Debug;
```

- [ ] **Step 4: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Commit (include Tasks 2, 3, 4 if not yet committed)**

```
git add src/Core/Console/Out.cs src/Core/AppConfig.cs src/Core/Logging/LogPipeline.cs src/Core/ServiceContext.cs
git commit -m "feat(console): Out — unified terminal+Serilog dispatch; AppConfig centralized; Console sink removed"
```

---

### Task 6: Create `src/Core/Console/Bar.cs`

**Files:**
- Create: `src/Core/Console/Bar.cs`

- [ ] **Step 1: Create the file**

```csharp
// src/Core/Console/Bar.cs
using Spectre.Console;

namespace Core.Console;

/// <summary>
/// Progress bar facade over Spectre.Console's AnsiConsole.Progress.
///
/// Do NOT call Out.* inside the work delegate while the bar renders —
/// Out writes to AnsiConsole which conflicts with a live Progress display.
/// Log.Emit inside the delegate is safe — Serilog writes to file only.
///
/// Usage:
///   await Bar.RunAsync("Uploading files", async task =>
///   {
///       task.MaxValue = files.Count;
///       foreach (var file in files)
///       {
///           await UploadAsync(file);
///           task.Increment(1);
///       }
///   });
/// </summary>
public static class Bar
{
    /// <summary>Renders a progress bar while async work runs.</summary>
    public static async Task RunAsync(string title, Func<ProgressTask, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var safeTitle = Markup.Escape(title ?? string.Empty);

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn()
            )
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(safeTitle);
                await work(task);
                task.Value = task.MaxValue;
            });
    }

    /// <summary>Synchronous variant. Prefer RunAsync for async work.</summary>
    public static void Run(string title, Action<ProgressTask> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var safeTitle = Markup.Escape(title ?? string.Empty);

        AnsiConsole.Progress()
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn()
            )
            .Start(ctx =>
            {
                var task = ctx.AddTask(safeTitle);
                work(task);
                task.Value = task.MaxValue;
            });
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Core/Console/Bar.cs
git commit -m "feat(console): add Bar — progress bar facade over Spectre.Console.Progress"
```

---

### Task 7: Create `src/Core/Console/Table.cs`

**Files:**
- Create: `src/Core/Console/Table.cs`

- [ ] **Step 1: Create the file**

```csharp
// src/Core/Console/Table.cs
using Spectre.Console;

namespace Core.Console;

/// <summary>
/// Table/grid output facade over Spectre.Console.Table.
/// All headers and cell content escape-sanitized — bracket characters in data
/// (Azure resource names, ARM IDs, etc.) cannot crash Spectre.
///
/// One-shot:
///   Table.Render(
///       ["Name", "Region", "SKU"],
///       [["storage-prod", "eastus", "Standard_LRS"],
///        ["storage-dr",   "westus", "Standard_GRS"]]
///   );
///
/// Fluent builder:
///   new Table.Builder()
///       .AddColumn("Name").AddColumn("Status")
///       .AddRow("storage-prod", "Active")
///       .Render();
/// </summary>
public static class Table
{
    /// <summary>Renders a table to the terminal.</summary>
    public static void Render(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var table = new Spectre.Console.Table();
        table.Border(TableBorder.Rounded);

        foreach (var header in headers)
            table.AddColumn(new TableColumn(Markup.Escape(header ?? string.Empty)));

        foreach (var row in rows)
            table.AddRow(row.Select(cell => Markup.Escape(cell ?? string.Empty)).ToArray());

        AnsiConsole.Write(table);
    }

    /// <summary>Fluent builder for tables constructed incrementally.</summary>
    public sealed class Builder
    {
        private readonly List<string>   _headers = [];
        private readonly List<string[]> _rows    = [];

        public Builder AddColumn(string header) { _headers.Add(header); return this; }
        public Builder AddRow(params string[] cells) { _rows.Add(cells); return this; }

        public void Render() =>
            Table.Render(_headers, _rows.Select(r => (IReadOnlyList<string>)r).ToList());
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Core/Console/Table.cs
git commit -m "feat(console): add Table — escape-safe grid facade with static Render and fluent Builder"
```

---

### Task 8: Fix `CommandBase` — replace 4 bare `AnsiConsole.MarkupLine` calls

These 4 error handlers crash if `ex.Message` contains `[` or `]` — which Azure SDK messages routinely do (ARM codes, retry headers).

**Files:**
- Modify: `src/Core/CommandBase.cs`

- [ ] **Step 1: Add `using Core.Console;` to the using block**

- [ ] **Step 2: Replace line 41**

```csharp
// Before
AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
// After
Out.Fail(ex.Message);
```

- [ ] **Step 3: Replace line 53**

```csharp
// Before
AnsiConsole.MarkupLine("[red]Error:[/] Rate limited. Please retry.");
// After
Out.Warn("Rate limited. Please retry.");
```

- [ ] **Step 4: Replace line 59**

```csharp
// Before
AnsiConsole.MarkupLine($"[red]Error:[/] Azure error ({ex.Status}): {ex.Message}");
// After
Out.Fail($"Azure error ({ex.Status}): {ex.Message}");
```

- [ ] **Step 5: Replace line 65**

```csharp
// Before
AnsiConsole.MarkupLine($"[red]Error:[/] Unexpected error: {ex.Message}");
// After
Out.Fail($"Unexpected error: {ex.Message}");
```

- [ ] **Step 6: Remove `using Spectre.Console;` if nothing else uses it**

`Spectre.Console.Cli` is a separate using — check if bare `Spectre.Console` is still needed. If not, remove it.

- [ ] **Step 7: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```
git add src/Core/CommandBase.cs
git commit -m "fix(commands): replace 4 unescaped AnsiConsole.MarkupLine in CommandBase with Out.* — kills markup crash in error handlers"
```

---

### Task 9: Fix `Program.cs` — replace `AnsiConsole.MarkupLine` with `Out.Fail`

**Files:**
- Modify: `src/App/Program.cs`

- [ ] **Step 1: Add `using Core.Console;`**

- [ ] **Step 2: Replace line 51**

```csharp
// Before
AnsiConsole.MarkupLine($"[red]Configuration error:[/] {ex.Message}");
// After
Out.Fail($"Configuration error: {ex.Message}");
```

- [ ] **Step 3: Remove `using Spectre.Console;` if no longer used**

- [ ] **Step 4: Build**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add src/App/Program.cs
git commit -m "fix(app): replace unescaped AnsiConsole.MarkupLine in Program.cs with Out.Fail"
```

---

### Task 10: Fix `AzureCommandModule` — remove `AzureEventSourceListener`

`AzureEventSourceListener.CreateConsoleLogger(EventLevel.LogAlways)` at line 25 dumps all Azure SDK internal ETW events directly to `Console.Out`. It bypasses every abstraction, floods stdout, and buries real errors under SDK noise.

**Files:**
- Modify: `src/CLI/Azure/AzureCommandModule.cs`

- [ ] **Step 1: Delete line 25**

Remove:
```csharp
AzureEventSourceListener.CreateConsoleLogger(EventLevel.LogAlways);
```

- [ ] **Step 2: Remove `using Azure.Core.Diagnostics;` and `using System.Diagnostics.Tracing;` if now unused**

- [ ] **Step 3: Build**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/CLI/Azure/AzureCommandModule.cs
git commit -m "fix(azure): remove AzureEventSourceListener.CreateConsoleLogger — ETW bypass deleted"
```

---

### Task 11: Fix `TypeResolver` — DI errors to Serilog

**Files:**
- Modify: `src/Core/Infrastructure/TypeResolver.cs` — line 24 only

- [ ] **Step 1: Replace line 24**

```csharp
// Before
Console.WriteLine($"DI Error resolving {type.Name}: {ex}");
// After
Serilog.Log.Warning(ex, "DI resolution failed for {TypeName}", type.Name);
```

The `throw` on line 25 is preserved — DI errors must not be swallowed.

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Core/Infrastructure/TypeResolver.cs
git commit -m "fix(di): route TypeResolver DI errors to Serilog.Log.Warning — no longer lost in stdout"
```

---

### Task 12: Migrate 11 Azure command files — `Ui.Info` → `Out.Info`

**Files to migrate** (all `src/CLI/Azure/`):

| File | Line | Change |
|---|---|---|
| `ChatCommand.cs` | 18 | `Ui.Info(result)` → `Out.Info(result)` |
| `DocIntelCommand.cs` | 23 | `Ui.Info(result)` → `Out.Info(result)` |
| `LanguageCommand.cs` | 17 | `Ui.Info(result)` → `Out.Info(result)` |
| `NerCommand.cs` | 17 | `Ui.Info(result)` → `Out.Info(result)` |
| `PhrasesCommand.cs` | 17 | `Ui.Info(result)` → `Out.Info(result)` |
| `PiiCommand.cs` | 17 | `Ui.Info(result)` → `Out.Info(result)` |
| `SentimentCommand.cs` | 17 | `Ui.Info(result)` → `Out.Info(result)` |
| `SpeechSttCommand.cs` | 18 | `Ui.Info(result)` → `Out.Info(result)` |
| `SpeechTtsCommand.cs` | 18 | `Ui.Info(result)` → `Out.Info(result)` |
| `TranslateCommand.cs` | 18 | `Ui.Info(result)` → `Out.Info(result)` |
| `VisionCommand.cs` | 22 | `Ui.Info(result)` → `Out.Info(result)` |

> All 11 print a raw service response — `Out.Info` (white, neutral) is correct. If a call site is clearly printing a success confirmation, use `Out.Success`. Use per-site judgement.

- [ ] **Step 1: In each file add `using Core.Console;`**

- [ ] **Step 2: Replace `Ui.Info(result)` with `Out.Info(result)` in each file**

- [ ] **Step 3: Remove `using Core;` if it was imported only for `Ui`**

- [ ] **Step 4: Build full solution**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add src/CLI/Azure/
git commit -m "migrate(commands): Ui.Info → Out.Info in all 11 Azure command files"
```

---

### Task 13: Delete `Core/Ui.cs`

Only after Task 12 produces a clean build.

**Files:**
- Delete: `src/Core/Ui.cs`

- [ ] **Step 1: Verify zero remaining `Ui.` references**

```
Select-String -Path src -Filter "*.cs" -Pattern "\bUi\." -Recurse
```

Expected: **0 matches.** Fix any remaining call sites before proceeding.

- [ ] **Step 2: Delete**

```
Remove-Item src/Core/Ui.cs
```

- [ ] **Step 3: Build**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "remove(core): delete Ui.cs stub — superseded by Core.Console.Out"
```

---

### Task 14: Remove `Serilog.Sinks.Console` package

Prevents future re-introduction of `WriteTo.Console`.

**Files:**
- Modify: `src/Core/Core.csproj`
- Modify: `src/Directory.Packages.props`

- [ ] **Step 1: Remove from `Core.csproj`**

Delete:
```xml
<PackageReference Include="Serilog.Sinks.Console" />
```

- [ ] **Step 2: Remove from `Directory.Packages.props`**

Delete:
```xml
<PackageVersion Include="Serilog.Sinks.Console" Version="6.1.1" />
```

- [ ] **Step 3: Build**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Core/Core.csproj src/Directory.Packages.props
git commit -m "chore(deps): remove Serilog.Sinks.Console — no Console sink in architecture"
```

---

### Task 15: Smoke test

- [ ] **Step 1: Build release**

```
dotnet build src/ --configuration Release
```

Expected: 0 errors.

- [ ] **Step 2: Run a normal command**

```
dotnet run --project src/App -- azure chat "hello"
```

Expected:
- Coloured output from `Out.Info` or `Out.Success`
- **No** `[HH:mm:ss INF]` Serilog console log lines
- **No** Azure SDK ETW noise
- **No** Spectre markup parse exceptions

- [ ] **Step 3: Verify escape safety**

```
dotnet run --project src/App -- azure chat "[test bracket input]"
```

Expected: `[test bracket input]` appears literally — not parsed as markup, no crash.

- [ ] **Step 4: Confirm log file written**

```
Get-ChildItem "$HOME\.cache\logs\app\" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content | Select-Object -First 3
```

Expected: compact JSON lines with `ConsoleMessage` properties.

- [ ] **Step 5: Commit**

```
git commit --allow-empty -m "chore: smoke test passed — Core.Console complete, Serilog/Spectre conflict resolved"
```

---

## Self-Review

| Requirement | Task |
|---|---|
| New `Console` namespace / directory | Tasks 5–7 |
| `Out.cs` with text output | Task 5 |
| `Bar.cs` separate file | Task 6 |
| `Table.cs` separate file | Task 7 |
| Log + console at one call site | Task 5 |
| `Out.Warn` → console + `Log.Warning` | Task 5 |
| `Out.Trace` → console + `Log.Verbose` | Task 5 |
| Different formatting per channel | Task 5 (`{ConsoleMessage}` vs ANSI) |
| Serilog/Spectre conflict resolved | Task 3 |
| God class counter-architecture | Tasks 1–2 |
| Config sprawl eliminated | Tasks 2, 4 |
| `AzureEventSourceListener` removed | Task 10 |
| `TypeResolver` `Console.WriteLine` fixed | Task 11 |
| All `Ui.*` call sites migrated | Task 12 |
| `Ui.cs` deleted | Task 13 |
| `Serilog.Sinks.Console` package removed | Task 14 |
| Tests foregone | Throughout — build + smoke only |
