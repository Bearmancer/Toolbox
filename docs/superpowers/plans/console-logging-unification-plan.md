# Console & Logging Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate Serilog/Spectre rendering conflicts, stop config/env-var sprawl across static God classes, and unify all user-visible terminal output under a new `Core.Console` namespace whose single dispatch point writes to both the terminal (Spectre) and structured log (Serilog) from one call site.

**Architecture:** A new `Core/Console/` directory contains three focused files: `Out.cs` (text output + dual dispatch), `Bar.cs` (progress bars), and `Table.cs` (grids). `AppConfig` becomes a pure env-var reader with no framework types. Serilog-specific config moves to a new `LogConfig` internal class inside `Core.Logging`. The Serilog `WriteTo.Console` sink is deleted permanently. `ServiceContext.Instance` and `LogPipeline`'s env reads are redirected through `AppConfig`.

**Tech Stack:** C# 12, .NET 8, Serilog 4.3 (file + Seq sinks only after this plan), Spectre.Console 0.57, DotNetEnv 3.1

---

## Context: Old → New Migration

This plan operates exclusively on the **New** project (`src/`). The **Old** project is a separate solution undergoing incremental migration. Do not treat any type in `src/` as dead code solely because it has no active callers _yet_ — the migration is ongoing. The `Ui.cs` stub in `src/Core/` is superseded by `Out.cs` from this plan, but it must not be deleted until every call site within `src/` is verified migrated (Task 13).

---

## Architecture: God Class Problem & Counter-Architecture

### Current: `AppConfig` does four jobs

| Responsibility | Evidence |
|---|---|
| Reads env vars | `GetEnvironmentVariable("LOG_LEVEL")`, `"LOG_OVERRIDES"` |
| Parses Serilog levels | `ParseLevel()`, `BuildLevelAliases()` returning `LogEventLevel` |
| Owns Serilog runtime switch | `public static LoggingLevelSwitch LogSwitch` |
| Owns Serilog overrides map | `public static Dictionary<string, LogEventLevel> Overrides` |

`LogPipeline.cs` reads `SEQ_URL` and `SEQ_INSTANCE` directly. `ServiceContext.cs` reads `SEQ_INSTANCE` independently. Result: `SEQ_INSTANCE` is read in **two** independent places; env reads are scattered across three files.

### Counter-Architecture: Vertical Slice by Consumer

```
AppConfig                    ← sole env reader; plain C# strings/nulls only
    │
    ├─── Core.Logging.LogConfig  (internal)
    │       string? RawLogLevel  → LoggingLevelSwitch
    │       string? RawLogOverrides → Dictionary<string, LogEventLevel>
    │       Serilog types confined here
    │
    └─── Core.Console.Out
            string? RawLogLevel → bool Verbose
            No Serilog types
```

**Rule:** Every new env var gets **one** read in `AppConfig`. Every consumer reads from `AppConfig`. `Environment.GetEnvironmentVariable` is called nowhere else.

---

## Architecture: Why the Serilog Console Sink Must Die (Permanently)

Serilog's `WriteTo.Console` calls `Console.Out.Write` on the Serilog thread. Spectre.Console's `AnsiConsole.Progress()` and live displays maintain cursor position using ANSI escape sequences and actively redraw. When Serilog fires a log event during an active Spectre live render, raw text is inserted mid-render: cursor jumps, lines corrupt, progress bar breaks.

**The only correct fix is total removal.** Terminal output flows exclusively through Spectre via `Core.Console.Out`. Structured telemetry flows through Serilog → file + Seq. These two channels never share a stream. Removing `WriteTo.Console` is permanent and non-negotiable.

---

## Architecture: Unified Dispatch Model

```
Out.Warn("Rate limit hit")
  ├─→ Serilog.Log.Write(Warning, "{ConsoleMessage}", msg)   ← searchable in Seq/file
  └─→ AnsiConsole.MarkupLine("[yellow]Rate limit hit[/]")   ← human-readable, escaped

Log.Emit(new RateLimitExceeded(...))    ← machine-only telemetry; no terminal print
```

**Level Mapping:**

| `Out` method | Console colour | Serilog level | Console gate |
|---|---|---|---|
| `Out.Trace(msg)` | `dim` | `Verbose` | Only if `Verbose == true` |
| `Out.Debug(msg)` | `grey` | `Debug` | Only if `Verbose == true` |
| `Out.Info(msg)` | `white` | `Information` | Always |
| `Out.Success(msg)` | `green` | `Information` | Always |
| `Out.Warn(msg)` | `yellow` | `Warning` | Always |
| `Out.Fail(msg)` | `red` | `Error` | Always |

Serilog still applies its own level filter independently. `Out.Verbose` is a console gate only — it does not suppress Serilog writes.

**Formatting differs per channel by design.** Terminal: coloured human sentences. Serilog: `{ConsoleMessage}` property — plain text, no ANSI codes, searchable.

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| **Create** | `src/Core/Console/Out.cs` | Text output + dual dispatch (Spectre + Serilog) |
| **Create** | `src/Core/Console/Bar.cs` | Progress bar facade over `AnsiConsole.Progress` |
| **Create** | `src/Core/Console/Table.cs` | Table/grid facade over `Spectre.Console.Table` |
| **Create** | `src/Core/Logging/LogConfig.cs` | Serilog-specific runtime config extracted from AppConfig |
| **Modify** | `src/Core/AppConfig.cs` | Strip Serilog types; centralize all env reads; plain types only |
| **Modify** | `src/Core/Logging/LogPipeline.cs` | Remove `WriteTo.Console`; consume `LogConfig`/`AppConfig`; gate `SelfLog` |
| **Modify** | `src/Core/ServiceContext.cs` | Read `Instance` from `AppConfig.SeqInstance` |
| **Modify** | `src/Core/CommandBase.cs` | Replace 4 bare `AnsiConsole.MarkupLine` calls with `Out.*` |
| **Modify** | `src/App/Program.cs` | Replace 1 bare `AnsiConsole.MarkupLine` call with `Out.Fail` |
| **Modify** | `src/CLI/Azure/AzureCommandModule.cs` | Remove `AzureEventSourceListener.CreateConsoleLogger` |
| **Modify** | `src/Core/Infrastructure/TypeResolver.cs` | Replace `Console.WriteLine` with `Serilog.Log.Warning` |
| **Migrate** | 11 `src/CLI/Azure/*.cs` command files | `Ui.Info(result)` → `Out.Info(result)` |
| **Delete** | `src/Core/Ui.cs` | Superseded — deleted only after all call sites verified migrated |
| **Modify** | `src/Core/Core.csproj` + `Directory.Packages.props` | Remove `Serilog.Sinks.Console` package |

---

## Tasks

---

### Task 1: Create `src/Core/Logging/LogConfig.cs`

Extract Serilog-specific runtime config from `AppConfig`. This must exist before Task 2 removes those members from `AppConfig`.

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

Remove all Serilog types. Centralize all env reads (including the `SEQ_URL` and `SEQ_INSTANCE` reads currently scattered in `LogPipeline` and `ServiceContext`).

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
/// Rule: if you need a new env var, add ONE read here. Never call
/// Environment.GetEnvironmentVariable anywhere else in src/.
///
/// Consumers:
///   Core.Logging.LogConfig — reads RawLogLevel, RawLogOverrides → produces Serilog types
///   Core.Logging.LogPipeline — reads SeqUrl, SeqInstance
///   Core.Console.Out — reads RawLogLevel → produces bool Verbose
///   Core.ServiceContext — reads SeqInstance
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// Raw LOG_LEVEL string (e.g. "debug", "warn", "trace").
    /// Null means not set — consumers should default to Debug.
    /// </summary>
    public static string? RawLogLevel { get; }

    /// <summary>
    /// Raw LOG_OVERRIDES string (e.g. "Microsoft=warn,System.Net=error").
    /// Null means no per-category overrides.
    /// </summary>
    public static string? RawLogOverrides { get; }

    /// <summary>
    /// SEQ_URL if configured. Null disables the Seq sink entirely.
    /// </summary>
    public static string? SeqUrl { get; }

    /// <summary>
    /// Instance label for log enrichment. Defaults to "local".
    /// </summary>
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

- [ ] **Step 2: Build — expect errors**

```
dotnet build src/Core/Core.csproj
```

Expected: errors in `LogPipeline.cs` referencing removed `AppConfig.LogSwitch` and `AppConfig.Overrides`. These are fixed in Task 3. Do not commit yet.

---

### Task 3: Update `LogPipeline` — remove Console sink, consume `LogConfig` and `AppConfig`

Fix the build errors from Task 2. Remove `WriteTo.Console`. Replace direct env reads with `AppConfig` properties. Gate `SelfLog` behind `#if DEBUG`.

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
    /// NO Console sink. Terminal output is exclusively via Core.Console.Out.
    /// Serilog's WriteTo.Console corrupts Spectre.Console live renders — removing
    /// it is permanent. See architecture notes in the plan.
    ///
    /// Also sets Out.Verbose from the resolved log level so that Out.Debug and
    /// Out.Trace gate correctly on the terminal without a separate flag.
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
        // Serilog internal errors (e.g. Seq unreachable) on stderr in debug only.
        // In production this noise is mistaken by users for application errors.
        SelfLog.Enable(Console.Error);
#endif

        // Gate Out.Debug / Out.Trace console output.
        // Verbose == true when LOG_LEVEL is debug, verbose, or trace.
        Out.Verbose = levelSwitch.MinimumLevel <= LogEventLevel.Debug;
    }

    public static void CloseAndFlush() => Serilog.Log.CloseAndFlush();
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: errors about `Out` not existing (Task 5 creates it). If so, temporarily comment out the `Out.Verbose = ...` line, build to confirm only that error remains, then re-add the comment. Continue to Task 4.

- [ ] **Step 3: Commit Tasks 2 + 3 together once Task 5 is done and build is clean**

```
git add src/Core/AppConfig.cs src/Core/Logging/LogPipeline.cs
git commit -m "refactor(config): centralize env reads in AppConfig; remove Serilog Console sink from LogPipeline"
```

---

### Task 4: Fix `ServiceContext` — eliminate duplicate `SEQ_INSTANCE` read

**Files:**
- Modify: `src/Core/ServiceContext.cs` — lines 19–20 only

- [ ] **Step 1: Replace the `Instance` property initializer**

Change lines 19–20 from:
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

Expected: 0 errors (or only the `Out` missing error if Task 5 not done yet).

- [ ] **Step 3: Commit**

```
git add src/Core/ServiceContext.cs
git commit -m "fix(config): ServiceContext.Instance reads AppConfig.SeqInstance — removes second SEQ_INSTANCE env read"
```

---

### Task 5: Create `src/Core/Console/Out.cs`

The central piece. Every user-visible message goes through here. Escape gate prevents Spectre markup crashes on any input including Azure error messages with brackets.

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
/// Unified terminal output facade. Single dispatch point for all user-visible messages.
///
/// Each method dispatches to two independent channels:
///   1. Spectre.Console — ANSI-coloured, escape-safe terminal rendering
///   2. Serilog         — structured log entry written to file + Seq
///
/// Formatting differs between channels intentionally:
///   Terminal  →  "[green]Operation completed[/]"
///   Serilog   →  {ConsoleMessage: "Operation completed"}  (plain text, no ANSI)
///
/// Call Out.* when the user should see a message.
/// Call Log.Emit(new SomeEvent(...)) when only machine-readable telemetry is needed.
///
/// Thread safety: Verbose is written once at startup (LogPipeline.Configure) on the
/// main thread before any command executes. volatile ensures all threads see the value.
/// </summary>
public static class Out
{
    private static volatile bool _verbose;

    /// <summary>
    /// When true, Debug() and Trace() print to the terminal.
    /// False by default. Set by LogPipeline.Configure() from LOG_LEVEL.
    /// True when LOG_LEVEL is "debug", "verbose", or "trace".
    /// </summary>
    public static bool Verbose
    {
        get => _verbose;
        internal set => _verbose = value;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Green terminal. Serilog Information. Use for: completed operations, saved items.</summary>
    public static void Success(string msg) =>
        Emit("green", msg, LogEventLevel.Information, print: true);

    /// <summary>Red terminal. Serilog Error. Use for: exceptions surfaced to user, fatal failures.</summary>
    public static void Fail(string msg) =>
        Emit("red", msg, LogEventLevel.Error, print: true);

    /// <summary>Yellow terminal. Serilog Warning. Use for: retries, degraded results, non-fatal issues.</summary>
    public static void Warn(string msg) =>
        Emit("yellow", msg, LogEventLevel.Warning, print: true);

    /// <summary>White terminal. Serilog Information. Use for: status updates, neutral messages.</summary>
    public static void Info(string msg) =>
        Emit("white", msg, LogEventLevel.Information, print: true);

    /// <summary>Grey terminal (Verbose only). Serilog Debug. Use for: per-step detail.</summary>
    public static void Debug(string msg) =>
        Emit("grey", msg, LogEventLevel.Debug, print: _verbose);

    /// <summary>Dim terminal (Verbose only). Serilog Verbose. Use for: per-item loop output, HTTP detail.</summary>
    public static void Trace(string msg) =>
        Emit("dim", msg, LogEventLevel.Verbose, print: _verbose);

    // ── Private ─────────────────────────────────────────────────────────────

    private static void Emit(string colour, string msg, LogEventLevel logLevel, bool print)
    {
        // Always write to Serilog — Serilog's level switch filters independently.
        // {ConsoleMessage} property: plain text, no ANSI codes, fully searchable in Seq.
        Serilog.Log.Write(logLevel, "{ConsoleMessage}", msg);

        if (!print) return;

        // Markup.Escape BEFORE interpolation.
        // Azure error messages, ARM codes, and resource names contain [ and ] characters.
        // Without Escape, Spectre parses these as markup → InvalidOperationException crash.
        var escaped = Markup.Escape(msg ?? string.Empty);
        AnsiConsole.MarkupLine($"[{colour}]{escaped}[/]");
    }
}
```

- [ ] **Step 3: Un-comment `Out.Verbose` line in `LogPipeline.Configure` if it was commented out**

Verify `src/Core/Logging/LogPipeline.cs` ends `Configure()` with:
```csharp
Out.Verbose = levelSwitch.MinimumLevel <= LogEventLevel.Debug;
```

- [ ] **Step 4: Build full Core project**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Commit Tasks 2, 3, 4, 5 together**

```
git add src/Core/AppConfig.cs src/Core/Logging/LogPipeline.cs src/Core/ServiceContext.cs src/Core/Console/Out.cs
git commit -m "feat(console): Out — unified terminal+Serilog dispatch with escape gate; AppConfig centralized; Console sink removed"
```

---

### Task 6: Create `src/Core/Console/Bar.cs`

Progress bar facade. Hides Spectre's verbose Progress API. Callers get `Run`/`RunAsync` with a `ProgressTask` to advance.

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
/// Important: Do NOT call Out.* inside the work delegate while the bar is rendering.
/// Out writes to AnsiConsole which conflicts with a live Progress render.
/// Use Log.Emit inside the delegate — Serilog writes to file only (no Console sink).
///
/// Usage:
///   await Bar.RunAsync("Uploading files", async task =>
///   {
///       for (var i = 0; i &lt; files.Count; i++)
///       {
///           await UploadAsync(files[i]);
///           task.Increment(1);
///       }
///   });
/// </summary>
public static class Bar
{
    /// <summary>
    /// Renders a progress bar while <paramref name="work"/> executes asynchronously.
    /// <paramref name="work"/> receives a <see cref="ProgressTask"/> to advance.
    ///   task.Increment(amount) — advance by amount (default MaxValue is 100)
    ///   task.MaxValue = n      — set total units before starting
    ///   task.Value = n         — jump to specific value
    /// </summary>
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
                task.Value = task.MaxValue; // guarantee 100% on completion
            });
    }

    /// <summary>
    /// Synchronous variant. Prefer <see cref="RunAsync"/> for async work.
    /// </summary>
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

Table facade. Both headers and cells are escape-sanitized. Static `Render` for one-shot use; fluent `Builder` for incremental construction.

**Files:**
- Create: `src/Core/Console/Table.cs`

- [ ] **Step 1: Create the file**

```csharp
// src/Core/Console/Table.cs
using Spectre.Console;

namespace Core.Console;

/// <summary>
/// Table/grid output facade over Spectre.Console.Table.
/// All headers and cell content escape-sanitized — callers cannot crash Spectre
/// with bracket characters in data (Azure resource names, ARM IDs, etc.).
///
/// One-shot usage:
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
    /// <param name="headers">Column header labels.</param>
    /// <param name="rows">Each element is one row; inner list is cells in column order.</param>
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

        /// <summary>Adds a column with the given header label.</summary>
        public Builder AddColumn(string header) { _headers.Add(header); return this; }

        /// <summary>Adds a row. Cell count should match column count.</summary>
        public Builder AddRow(params string[] cells) { _rows.Add(cells); return this; }

        /// <summary>Renders the table to the terminal.</summary>
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

The error handlers in `CommandBase` are the highest-severity crash risk. Azure exception messages contain `[` and `]` (ARM error codes, retry headers, resource names) — without `Markup.Escape`, Spectre throws `InvalidOperationException` in the error handler itself.

**Files:**
- Modify: `src/Core/CommandBase.cs`

- [ ] **Step 1: Add `using Core.Console;`**

Add to the using block:
```csharp
using Core.Console;
```

- [ ] **Step 2: Replace line 41**

From:
```csharp
AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
```
To:
```csharp
Out.Fail(ex.Message);
```

- [ ] **Step 3: Replace line 53**

From:
```csharp
AnsiConsole.MarkupLine("[red]Error:[/] Rate limited. Please retry.");
```
To:
```csharp
Out.Warn("Rate limited. Please retry.");
```

- [ ] **Step 4: Replace line 59**

From:
```csharp
AnsiConsole.MarkupLine($"[red]Error:[/] Azure error ({ex.Status}): {ex.Message}");
```
To:
```csharp
Out.Fail($"Azure error ({ex.Status}): {ex.Message}");
```

- [ ] **Step 5: Replace line 65**

From:
```csharp
AnsiConsole.MarkupLine($"[red]Error:[/] Unexpected error: {ex.Message}");
```
To:
```csharp
Out.Fail($"Unexpected error: {ex.Message}");
```

- [ ] **Step 6: Remove `using Spectre.Console;` if unused**

Check: after these replacements, does anything in `CommandBase.cs` still reference `AnsiConsole` or `Markup`? If not, remove:
```csharp
using Spectre.Console;
```
`Spectre.Console.Cli` (still needed for `AsyncCommand`, `CommandSettings`, `CommandContext`) is a separate using — keep it.

- [ ] **Step 7: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```
git add src/Core/CommandBase.cs
git commit -m "fix(commands): replace 4 unescaped AnsiConsole.MarkupLine in CommandBase with Out.* — eliminates markup crash risk in error handlers"
```

---

### Task 9: Fix `Program.cs` — replace `AnsiConsole.MarkupLine` with `Out.Fail`

**Files:**
- Modify: `src/App/Program.cs`

- [ ] **Step 1: Add `using Core.Console;`**

Add to the using block.

- [ ] **Step 2: Replace line 51**

From:
```csharp
AnsiConsole.MarkupLine($"[red]Configuration error:[/] {ex.Message}");
```
To:
```csharp
Out.Fail($"Configuration error: {ex.Message}");
```

- [ ] **Step 3: Remove `using Spectre.Console;` if no longer used**

Check: any remaining `AnsiConsole.*` calls in `Program.cs`? If none, remove `using Spectre.Console;`.

- [ ] **Step 4: Build full solution**

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

Line 25 routes all Azure SDK internal ETW events directly to `Console.Out` at `LogAlways`. This includes HTTP retries, authentication token refreshes, connection events. It bypasses every abstraction, floods stdout during progress renders, and buries actual errors under SDK noise.

**Files:**
- Modify: `src/CLI/Azure/AzureCommandModule.cs`

- [ ] **Step 1: Delete line 25**

Remove:
```csharp
AzureEventSourceListener.CreateConsoleLogger(EventLevel.LogAlways);
```

- [ ] **Step 2: Remove associated usings if now unused**

Check if `AzureEventSourceListener` or `EventLevel` appear anywhere else in the file. If not, remove:
```csharp
using Azure.Core.Diagnostics;
using System.Diagnostics.Tracing;
```

- [ ] **Step 3: Build**

```
dotnet build src/
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/CLI/Azure/AzureCommandModule.cs
git commit -m "fix(azure): remove AzureEventSourceListener.CreateConsoleLogger — bypassed all output abstractions at LogAlways"
```

---

### Task 11: Fix `TypeResolver` — DI errors to Serilog instead of `Console.WriteLine`

**Files:**
- Modify: `src/Core/Infrastructure/TypeResolver.cs` — line 24 only

- [ ] **Step 1: Replace line 24**

From:
```csharp
Console.WriteLine($"DI Error resolving {type.Name}: {ex}");
```
To:
```csharp
Serilog.Log.Warning(ex, "DI resolution failed for {TypeName}", type.Name);
```

The `throw` on line 25 is preserved — DI errors must not be silently swallowed.

- [ ] **Step 2: Build**

```
dotnet build src/Core/Core.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Core/Infrastructure/TypeResolver.cs
git commit -m "fix(di): route TypeResolver DI errors to Serilog.Log.Warning — no longer lost in stdout noise"
```

---

### Task 12: Migrate 11 Azure command files — `Ui.Info` → `Out.Info`

All 11 Azure command files call `Ui.Info(result)` to print the service response. This maps to `Out.Info`. After this task, `Ui.cs` has no call sites and can be deleted.

**Files to migrate** (all in `src/CLI/Azure/`):

| File | Line | Current | Target |
|---|---|---|---|
| `ChatCommand.cs` | 18 | `Ui.Info(result)` | `Out.Info(result)` |
| `DocIntelCommand.cs` | 23 | `Ui.Info(result)` | `Out.Info(result)` |
| `LanguageCommand.cs` | 17 | `Ui.Info(result)` | `Out.Info(result)` |
| `NerCommand.cs` | 17 | `Ui.Info(result)` | `Out.Info(result)` |
| `PhrasesCommand.cs` | 17 | `Ui.Info(result)` | `Out.Info(result)` |
| `PiiCommand.cs` | 17 | `Ui.Info(result)` | `Out.Info(result)` |
| `SentimentCommand.cs` | 17 | `Ui.Info(result)` | `Out.Info(result)` |
| `SpeechSttCommand.cs` | 18 | `Ui.Info(result)` | `Out.Info(result)` |
| `SpeechTtsCommand.cs` | 18 | `Ui.Info(result)` | `Out.Info(result)` |
| `TranslateCommand.cs` | 18 | `Ui.Info(result)` | `Out.Info(result)` |
| `VisionCommand.cs` | 22 | `Ui.Info(result)` | `Out.Info(result)` |

> **Mapping note:** All 11 current call sites print a raw service response string. `Out.Info` (white, neutral) is correct. If during migration you find a call site that clearly prints a success confirmation, use `Out.Success`. If printing an error, use `Out.Fail`. Use per-site judgement — do not map blindly.

- [ ] **Step 1: In each file, add `using Core.Console;`**

Each file needs:
```csharp
using Core.Console;
```

- [ ] **Step 2: Replace `Ui.Info(result)` with `Out.Info(result)` in each file**

```csharp
// Before
Ui.Info(result);

// After
Out.Info(result);
```

- [ ] **Step 3: Remove `using Core;` if it was imported only for `Ui`**

After replacing `Ui.Info`, check if `Core` namespace is still needed in the file (e.g. for `CommandBase<T>` or `CommandSettings`). If `Ui` was the only reason, remove `using Core;`. If `Core` is needed for other types, keep it.

- [ ] **Step 4: Build full solution**

```
dotnet build src/
```

Expected: 0 errors. If any file reports `Ui not found`, that file was missed — fix it before proceeding.

- [ ] **Step 5: Commit**

```
git add src/CLI/Azure/
git commit -m "migrate(commands): Ui.Info → Out.Info in all 11 Azure command files"
```

---

### Task 13: Delete `Core/Ui.cs`

Only execute after Task 12 produces a clean build.

**Files:**
- Delete: `src/Core/Ui.cs`

- [ ] **Step 1: Verify zero remaining `Ui.` references in `src/`**

```
Select-String -Path src -Filter "*.cs" -Pattern "\bUi\." -Recurse
```

Expected: **0 matches.** If any appear, fix those call sites first.

- [ ] **Step 2: Delete**

```
Remove-Item src/Core/Ui.cs
```

- [ ] **Step 3: Build full solution**

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

Removing the package prevents accidental re-introduction of `WriteTo.Console` in future — the package will no longer be resolvable.

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

- [ ] **Step 3: Build full solution**

```
dotnet build src/
```

Expected: 0 errors. If any file fails claiming `WriteTo.Console` is unknown, that file re-introduced the sink — find and remove it.

- [ ] **Step 4: Commit**

```
git add src/Core/Core.csproj src/Directory.Packages.props
git commit -m "chore(deps): remove Serilog.Sinks.Console — no Console sink in architecture"
```

---

### Task 15: Smoke test — end-to-end verification

- [ ] **Step 1: Build release**

```
dotnet build src/ --configuration Release
```

Expected: 0 errors, 0 warnings about removed members.

- [ ] **Step 2: Run a normal command and inspect terminal output**

```
dotnet run --project src/App -- azure chat "hello"
```

Expected:
- Terminal shows coloured output (white/green) from `Out.Info` or `Out.Success`
- **No** `[HH:mm:ss INF]` Serilog console log lines
- **No** Azure SDK ETW noise lines (HTTP retries, auth events)
- **No** `Spectre.Console` markup parse exceptions

- [ ] **Step 3: Verify escape safety with bracket input**

```
dotnet run --project src/App -- azure chat "[test]"
```

Expected: `[test]` appears literally in the output — not parsed as markup. No crash.

- [ ] **Step 4: Force an error and verify error output is clean**

Pass a bad/missing argument to trigger the `ArgumentException` catch in `CommandBase`. Confirm the error message prints in red without crashing even if the message contains brackets.

- [ ] **Step 5: Confirm log file is written**

```
Get-ChildItem "$HOME\.cache\logs\app\" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content | Select-Object -First 5
```

Expected: compact JSON lines. `Out.*` calls appear as `{"ConsoleMessage": "..."}` properties.

- [ ] **Step 6: Final commit**

```
git commit --allow-empty -m "chore: smoke test passed — Core.Console namespace complete, Serilog/Spectre conflict resolved"
```

---

## Self-Review

### Spec Coverage

| Requirement | Task |
|---|---|
| New `Console` namespace / directory | Tasks 5–7 |
| `Out.cs` text output | Task 5 |
| `Bar.cs` separate file | Task 6 |
| `Table.cs` separate file | Task 7 |
| Log + console centralized at one call site | Task 5 (`Out.Emit`) |
| `Out.Warn` → console yellow + `Log.Warning` | Task 5 |
| `Out.Trace` → console dim + `Log.Verbose` | Task 5 |
| Different formatting per channel | Task 5 (ANSI colour vs `{ConsoleMessage}` property) |
| Serilog/Spectre conflict resolved | Task 3 (Console sink removed) |
| God class counter-architecture documented | Architecture section; Tasks 1–2 |
| Config sprawl / dupes eliminated | Tasks 2, 4 (AppConfig centralized; ServiceContext + LogPipeline consume it) |
| `AzureEventSourceListener` removed | Task 10 |
| `TypeResolver` `Console.WriteLine` fixed | Task 11 |
| All `Ui.*` call sites migrated | Task 12 |
| `Ui.cs` deleted | Task 13 |
| `Serilog.Sinks.Console` package removed | Task 14 |

### Type Consistency

- `AppConfig.SeqInstance` (string) → consumed by `ServiceContext.Instance` (string init) ✓
- `AppConfig.SeqUrl` (string?) → consumed by `LogPipeline.Configure` conditional ✓
- `LogConfig.LevelSwitch` (internal) → consumed only within `Core.Logging` ✓
- `Out.Verbose` (bool, volatile) → set in `LogPipeline.Configure`, read in `Out.Debug`/`Out.Trace` ✓
- `Out.Fail(string)` → called in `CommandBase` catch blocks with interpolated `string` ✓
- `Bar.RunAsync(string, Func<ProgressTask, Task>)` → no callers yet (migration-ready) ✓
- `Table.Builder.AddRow(params string[])` → `Table.Render` receives `IReadOnlyList<string>` ✓
