# Logging + Terminal Migration Plan

Rewrites the logging layer and removes the phantom CliFramework dependency in 4 phases. Every decision below is backed by verified NuGet versions and audited call sites.

## Verified Package Versions

| Package | Version | Source |
|---|---|---|
| `SerilogTracing` | `2.4.0` | NuGet (stable, Jun 2026) |
| `Serilog.Sinks.Spectre` | `0.6.0` | NuGet (stable, Apr 2026) |
| `Spectre.Console.Cli.Extensions.DependencyInjection` | `0.26.0` | NuGet (stable, Jun 2026) |
| `Spectre.Console` | `0.57.0` | Already in `Directory.Packages.props` |
| `Spectre.Console.Cli` | `0.55.0` | Already in `Directory.Packages.props` |

Compatibility chain: `Serilog.Sinks.Spectre 0.6.0` requires `Spectre.Console >= 0.55.2` (satisfied by 0.57.0). `Spectre.Console.Cli.Extensions.DependencyInjection 0.26.0` requires `Spectre.Console.Cli >= 0.55.0` (satisfied by 0.55.0).

## Call Site Audit (45 old API calls, not 11)

The previous plan stated "11 call sites." The actual count from grepping `src/Azure/*.cs`:

| Old API | Count | Fate |
|---|---|---|
| `Log.BeginOperation(...)` | 11 | Replace with `Log.Logger.StartActivity(...)` |
| `Log.Emit(new ApiRequested(...))` | 11 | **DELETE** (SerilogTracing auto-captures HTTP) |
| `Log.Emit(new ApiResponded(...))` | 11 | **DELETE** (SerilogTracing auto-captures HTTP) |
| `op.Complete()` | 11 | Replace with `activity.Complete()` |
| `op.Fail()` | 1 | Replace with `activity.Complete(LogEventLevel.Warning)` |
| **Total old API** | **45** | |

Additionally:

| Other migration point | Count | Fate |
|---|---|---|
| `Ui.Info(result)` in CLI commands | 11 | Replace with `Terminal.Info(result)` |
| `AnsiConsole.MarkupLine(...)` in Program.cs | 1 | Replace with `Terminal.Error(ex.Message)` |
| `AzureEventSourceListener.CreateConsoleLogger(...)` | 1 | DELETE |
| `using CliFramework` / `CliFramework.*` | 17 | DELETE all |

**Why delete ApiRequested/ApiResponded instead of rewrite?** SerilogTracing with `ActivityListenerConfiguration().TraceToSharedLogger()` automatically instruments `HttpClient`, which all Azure SDK clients use internally. The auto-instrumented spans capture the same data (API name, HTTP method, status code, elapsed time) that the manual `ApiRequested`/`ApiResponded` events were logging. Keeping manual logging would duplicate every HTTP call in the logs.

## Phase 0: CliFramework Replacement

CliFramework directory does not exist (`src/CliFramework/` is missing). The 2 `.csproj` files reference it, and 16 source files import its types. This is a phantom dependency that blocks compilation.

### What CliFramework provides (9 types)

| Type | Used in | Replacement |
|---|---|---|
| `CommandBase<T>` | 11 CLI commands | `AsyncCommand<T>` (Spectre built-in) |
| `ICommandModule` | `AzureCommandModule`, `CliModuleRegistry` | Delete interface. Move `ConfigureServices`/`ConfigureCommands` into `Program.cs` directly. |
| `TypeRegistrar` | `Program.cs:57` | `DependencyInjectionRegistrar` from `Spectre.Console.Cli.Extensions.DependencyInjection` |
| `TypeResolver` | (internal to TypeRegistrar) | Handled by the DI package |
| `Host.Initialize()` | `Program.cs:34` | Inline `Console.CancelKeyPress` + `AppDomain.ProcessExit` in `Program.cs` |
| `LogPipeline.Configure()` | `Program.cs:31` | Replace with `Log.Configure("app", level)` |
| `Ui` | 11 CLI commands | Replace with `Terminal` (new class in Logging project) |
| `ApiRequested` | 7 Azure services | DELETE (SerilogTracing captures HTTP) |
| `ApiResponded` | 7 Azure services | DELETE (SerilogTracing captures HTTP) |
| `OperationScope` (`BeginOperation`) | 7 Azure services | Replace with `LoggerActivity` from SerilogTracing |
| `BeginSession` | **Nobody calls it** | DELETE (dead code) |

### Step-by-step removal

1. Remove `<ProjectReference Include="..\CliFramework\CliFramework.csproj" />` from `src/App/App.csproj` and `src/CLI/CLI.csproj`
2. Delete `using CliFramework;` and `using CliFramework.Infrastructure;` from all 16 source files
3. Delete `using CliFramework.Modules;` from `AzureCommandModule.cs` and `CliModuleRegistry.cs`
4. Delete the `ICommandModule` interface usage. `AzureCommandModule` becomes a plain class with two static methods called directly from `Program.cs`
5. `CliModuleRegistry` becomes unnecessary after step 4. Delete the file.

### New command base class pattern

**Before** (CliFramework):
```csharp
using CliFramework;

public class TranslateCommand(TranslateService service) : CommandBase<TranslateCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.TranslateAsync(s.Text, s.To, s.From, ct);
        Ui.Info(result);
        return 0;
    }
}
```

**After** (Spectre built-in):
```csharp
using Spectre.Console.Cli;

public class TranslateCommand(TranslateService service) : AsyncCommand<TranslateCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.TranslateAsync(s.Text, s.To, s.From, ct);
        Terminal.Info(result);
        return 0;
    }
}
```

Changes: `CommandBase<T>` → `AsyncCommand<T>`. `ExecuteCommandAsync` → `ExecuteAsync`. `Ui.Info` → `Terminal.Info`. Remove `using CliFramework;`.

### New DI registration pattern

**Before** (custom TypeRegistrar):
```csharp
var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);
```

**After** (Spectre DI package):
```csharp
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);
```

### Module elimination

**Before** (`AzureCommandModule.cs` implements `ICommandModule`):
```csharp
public class AzureCommandModule : ICommandModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration config) { ... }
    public void ConfigureCommands(IConfigurator config) { ... }
}
```

**After** (inline in `Program.cs`):
```csharp
// In Program.cs — just call the methods directly
AzureServices.Configure(services, configuration);
// ...
app.Configure(cfg => AzureCommands.Configure(cfg));
```

`AzureCommandModule` gets renamed to `AzureServices` (static class, two static methods, no interface). `ConfigureCommands` becomes `AzureCommands.Configure(IConfigurator)`. Alternatively, keep the instance methods on the class and just stop implementing the interface.

## Phase 1: Foundation (Log.cs + Terminal.cs + Packages)

### Log.cs (complete, 50 lines)

This is a full rewrite. The file is currently empty (0 lines).

```csharp
using Serilog;
using Serilog.Events;
using SerilogTracing;

namespace Logging;

public static class Log
{
    private static IDisposable? Listener;
    public static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    public static void Configure(string application, LogEventLevel level = LogEventLevel.Information)
    {
        LevelSwitch.MinimumLevel = level;

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", application.ToLowerInvariant());
        Directory.CreateDirectory(logDir);

        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.File(
                Path.Combine(logDir, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
            .WriteTo.Spectre()
            .CreateLogger();

        Listener = new ActivityListenerConfiguration().TraceToSharedLogger();
    }

    public static void CloseAndFlush()
    {
        Listener?.Dispose();
        Serilog.Log.CloseAndFlush();
    }
}
```

**Key decisions:**
- `LevelSwitch` is public and static so `Terminal` can read it. No getter method needed.
- `Configure()` replaces `LogPipeline.Configure()`. It does NOT replace `BeginSession()` (dead code, never called anywhere).
- The `ActivityListenerConfiguration().TraceToSharedLogger()` is stored as a static field `Listener` (PascalCase, no underscore) to keep it alive for the process lifetime. If this were a `using var` inside `Configure()`, it would be disposed when the method returns, and HttpClient would produce zero spans.
- `CloseAndFlush()` disposes the listener before flushing Serilog, ensuring clean shutdown.
- `WriteTo.Spectre()` routes Serilog output through `AnsiConsole`, preventing interleaving with Spectre live renderers.
- No `BeginSession()` method. No session concept in the new design.
- **File sink uses absolute path.** `Path.Combine(UserProfile, ".cache", application)` resolves to `~/.cache/<app>/`. CWD-independent. Serilog's `RollingInterval.Day` auto-generates date-suffixed filenames (`log-20260622.txt`), so the base path is just `"log-.txt"`.
- **Class naming.** Our class is `Log` in namespace `Logging`. To avoid collision with `Serilog.Log`, **never add `using Serilog;` to files that also import `using Logging;`**. Service files only need `using Logging;` and (where `LogEventLevel` is used) `using Serilog.Events;`. Inside `Log.cs` itself, `Serilog.Log.Logger` is fully qualified.

### Terminal.cs (complete, 35 lines)

```csharp
using Serilog.Events;
using Spectre.Console;

namespace Logging;

public static class Terminal
{
    private static LogEventLevel CurrentLevel => Log.LevelSwitch.MinimumLevel;

    private static void Write(LogEventLevel requiredLevel, string color, string message)
    {
        if (CurrentLevel > requiredLevel) return;
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
    }

    public static void Trace(string message) => Write(LogEventLevel.Verbose, "darkgrey", message);
    public static void Debug(string message) => Write(LogEventLevel.Debug, "grey", message);
    public static void Info(string message) => Write(LogEventLevel.Information, "white", message);
    public static void Success(string message) => Write(LogEventLevel.Information, "green", message);
    public static void Warning(string message) => Write(LogEventLevel.Warning, "yellow", message);
    public static void Error(string message) => Write(LogEventLevel.Error, "red", message);
    public static void Fatal(string message) => Write(LogEventLevel.Fatal, "red", message);
}
```

**Level mapping:**
- `Trace()` → `LogEventLevel.Verbose` (framework internals)
- `Debug()` → `LogEventLevel.Debug` (application detail)
- `Info()` → `LogEventLevel.Information` (normal operation)
- `Success()` → `LogEventLevel.Information` (green variant)
- `Warning()` → `LogEventLevel.Warning` (unexpected but handled)
- `Error()` → `LogEventLevel.Error` (failure)
- `Fatal()` → `LogEventLevel.Fatal` (unrecoverable)

`Markup.Escape(message)` prevents Spectre markup injection from user-supplied strings. This is a security requirement.

### Logging.csproj changes

**Before:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Logging</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Enrichers.Environment" />
    <PackageReference Include="Serilog.Enrichers.Thread" />
    <PackageReference Include="Serilog.Formatting.Compact" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Sinks.Seq" />
  </ItemGroup>
</Project>
```

**After:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Logging</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Enrichers.Environment" />
    <PackageReference Include="Serilog.Enrichers.Thread" />
    <PackageReference Include="Serilog.Formatting.Compact" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Sinks.Seq" />
    <PackageReference Include="Serilog.Sinks.Spectre" />
    <PackageReference Include="SerilogTracing" />
    <PackageReference Include="Spectre.Console" />
  </ItemGroup>
</Project>
```

Changes: Remove `Serilog.Sinks.Console`. Add `Serilog.Sinks.Spectre`, `SerilogTracing`, and `Spectre.Console`. Spectre.Console is needed because `Terminal.cs` references `AnsiConsole.MarkupLine` and `Markup.Escape`.

### Directory.Packages.props additions

Add these 3 entries to the existing `<ItemGroup>`:

```xml
<PackageVersion Include="Serilog.Sinks.Spectre" Version="0.6.0" />
<PackageVersion Include="SerilogTracing" Version="2.4.0" />
<PackageVersion Include="Spectre.Console.Cli.Extensions.DependencyInjection" Version="0.26.0" />
```

The `Spectre.Console` and `Spectre.Console.Cli` versions are already present (0.57.0 and 0.55.0).

## Phase 2: Azure Service Rewrite

### Before/After: TranslateService.cs

**Before (current code):**
```csharp
using Azure.AI.Translation.Text;
using Logging;

namespace App.Services.Azure;

public class TranslateService(TextTranslationClient client)
{
    public async Task<string> TranslateAsync(
        string text, string toLang, string fromLang = "en", CancellationToken ct = default)
    {
        using var op = Log.BeginOperation("Translate.Translate");

        if (text.Length > Constants.TranslatorMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 50K");

        Log.Emit(new ApiRequested("Translate", "Translate", $"{fromLang}->{toLang}"));
        var startTime = DateTime.UtcNow;
        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        Log.Emit(new ApiResponded("Translate", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}
```

**After:**
```csharp
using Azure.AI.Translation.Text;
using Logging;
using Serilog.Events;

namespace App.Services.Azure;

public class TranslateService(TextTranslationClient client)
{
    public async Task<string> TranslateAsync(
        string text, string toLang, string fromLang = "en", CancellationToken ct = default)
    {
        using var activity = Log.Logger.StartActivity("Translate {FromLang} -> {ToLang}", fromLang, toLang);

        if (text.Length > Constants.TranslatorMaxChars)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text length {text.Length} exceeds 50K");

        var response = await client.TranslateAsync(toLang, [text], fromLang, ct);
        activity.Complete();
        return $"{fromLang} -> {toLang}: {response.Value[0].Translations[0].Text}";
    }
}
```

**What changed:**
- `Log.BeginOperation(...)` → `Log.Logger.StartActivity(...)` (SerilogTracing)
- `Log.Emit(new ApiRequested(...))` → **DELETED** (auto-instrumented)
- `Log.Emit(new ApiResponded(...))` → **DELETED** (auto-instrumented)
- `var startTime = DateTime.UtcNow` + manual elapsed calc → **DELETED** (auto-captured)
- `op.Complete()` → `activity.Complete()`
- Kept `using Logging;` (for `Log.Logger`). Added `using Serilog.Events;` (for `LogEventLevel` if needed).
- **No `using Serilog;`**. Service files never import `Serilog` directly. This avoids the `Log` name collision between `Logging.Log` (our class) and `Serilog.Log`. If `LogEventLevel` is needed (e.g., `DocIntelService`), import `using Serilog.Events;` instead.

### Pattern for all 7 services

Every service follows the same mechanical transformation:

1. Replace `using var op = Log.BeginOperation("...")` with `using var activity = Log.Logger.StartActivity("...")`
2. Delete both `Log.Emit(new ApiRequested(...))` and `Log.Emit(new ApiResponded(...))` lines
3. Delete `var startTime = DateTime.UtcNow` and the elapsed time calculation
4. Replace `op.Complete()` with `activity.Complete()`
5. Replace `op.Fail()` with `activity.Complete(LogEventLevel.Warning)` (only DocIntelService)

**Import rule (critical):** Service files get `using Logging;` and `using Serilog.Events;` (if `LogEventLevel` is used). They must NOT have `using Serilog;`. This prevents the `Log` name collision between our `Logging.Log` class and `Serilog.Log`.

**DocIntelService special case** (line 52, `op.Fail()`):
```csharp
// Before:
if (result.Pages.Count is 0)
{
    op.Fail();
    throw new InvalidOperationException("Model returned no pages");
}

// After:
if (result.Pages.Count is 0)
{
    activity.Complete(LogEventLevel.Warning);
    throw new InvalidOperationException("Model returned no pages");
}
```

### Files to modify (7 Azure services)

| File | BeginOp | Emit (delete) | Complete | Fail |
|---|---|---|---|---|
| `TranslateService.cs` | 1 | 2 | 1 | 0 |
| `OpenAiService.cs` | 1 | 2 | 1 | 0 |
| `SpeechSttService.cs` | 1 | 2 | 1 | 0 |
| `SpeechTtsService.cs` | 1 | 2 | 1 | 0 |
| `TextAnalyticsService.cs` | 5 | 10 | 5 | 0 |
| `VisionService.cs` | 1 | 2 | 1 | 0 |
| `DocIntelService.cs` | 1 | 2 | 1 | 1 |

## Phase 3: Program.cs Rewrite

### Verbose flag parsing (Issue #4)

The `--verbose` flag must be parsed BEFORE `app.RunAsync(args)`. Spectre owns the args and will parse known flags. Unknown flags like `--verbose` are ignored by Spectre (it only parses flags declared in command `Settings` classes). So `args.Contains("--verbose")` runs first, then the raw args are passed to Spectre unchanged.

### Complete Program.cs

```csharp
using CLI;
using CLI.Azure;
using Logging;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

        if (!File.Exists(envPath))
        {
            await Console.Error.WriteLineAsync(
                $".env not found at {envPath}. Create one at the repo root with all required keys."
            );
            return 2;
        }

        Env.TraversePath().Load();

        // Parse --verbose BEFORE Spectre gets the args
        var level = args.Contains("--verbose")
            ? LogEventLevel.Debug
            : LogEventLevel.Information;

        Log.Configure("app", level);

        // Wire shutdown handlers (replaces Host.Initialize())
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Log.CloseAndFlush(); };
        AppDomain.ProcessExit += (_, _) => Log.CloseAndFlush();

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();

        try
        {
            AzureServices.ConfigureServices(services, configuration);
        }
        catch (InvalidOperationException ex)
        {
            Terminal.Error($"Configuration error: {ex.Message}");
            return 2;
        }

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(cfg =>
        {
            cfg.SetApplicationName("app");
            cfg.SetApplicationVersion("1.0.0");
            AzureCommands.Configure(cfg);
        });

        return await app.RunAsync(args);
    }
}
```

**What changed from original Program.cs:**
- Removed `using CliFramework;` and `using CliFramework.Infrastructure;`
- Added `using Spectre.Console.Cli.Extensions.DependencyInjection;`
- `LogPipeline.Configure("app")` → `Log.Configure("app", level)` with verbose parsing
- `Host.Initialize()` → inline `Console.CancelKeyPress` + `AppDomain.ProcessExit`
- `AnsiConsole.MarkupLine($"[red]Configuration error:[/] ...")` → `Terminal.Error(...)` (Issue #5)
- `var registrar = new TypeRegistrar(services)` → `var registrar = new DependencyInjectionRegistrar(services)`
- Removed `foreach (var module in modules)` loop, replaced with direct `AzureServices.ConfigureServices(...)` call
- Removed `module.ConfigureCommands(cfg)` loop, replaced with `AzureCommands.Configure(cfg)`
- Removed `CliModuleRegistry` usage entirely

### Shutdown handler inlining

The old `Host.Initialize()` registered `Ctrl+C` and `ProcessExit` handlers that called `Log.CloseAndFlush()`. The new code inlines this in 2 lines:

```csharp
Console.CancelKeyPress += (_, e) => { e.Cancel = true; Log.CloseAndFlush(); };
AppDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
```

No `Host` class needed. `e.Cancel = true` prevents immediate termination so Serilog can flush.

## Phase 4: CLI Command Updates

### All 11 commands

Every command file gets the same 3 changes:

1. Remove `using CliFramework;`
2. `CommandBase<T>` → `AsyncCommand<T>`
3. `ExecuteCommandAsync(...)` → `ExecuteAsync(...)`
4. `Ui.Info(result)` → `Terminal.Info(result)`

**Files:** `ChatCommand.cs`, `LanguageCommand.cs`, `NerCommand.cs`, `PhrasesCommand.cs`, `DocIntelCommand.cs`, `SentimentCommand.cs`, `SpeechTtsCommand.cs`, `VisionCommand.cs`, `PiiCommand.cs`, `TranslateCommand.cs`, `SpeechSttCommand.cs`

### AzureCommandModule.cs cleanup

Remove:
- `using System.Diagnostics.Tracing;`
- `using CliFramework.Modules;`
- `AzureEventSourceListener.CreateConsoleLogger(EventLevel.LogAlways);` (line 25)
- The `ICommandModule` interface implementation

Rename class to `AzureServices`, keep `ConfigureServices` as a static method. Extract `ConfigureCommands` into a separate `AzureCommands` static class or keep as second static method.

### CliModuleRegistry.cs

DELETE this file. No longer needed.

## File Change Summary

### Create (2 files)

| File | Lines | Purpose |
|---|---|---|
| `src/Logging/Log.cs` | ~55 | `Configure()` + `CloseAndFlush()` + `LevelSwitch`. Absolute file path, PascalCase fields. Full rewrite from empty file. |
| `src/Logging/Terminal.cs` | ~35 | 7 level-gated methods, auto-escaping, wraps AnsiConsole |

### Modify (18 files)

| File | Change |
|---|---|
| `src/Logging/Logging.csproj` | Remove `Serilog.Sinks.Console`. Add `Serilog.Sinks.Spectre`, `SerilogTracing`, `Spectre.Console` |
| `src/Directory.Packages.props` | Add 3 new PackageVersion entries |
| `src/App/App.csproj` | Remove CliFramework ProjectReference |
| `src/CLI/CLI.csproj` | Remove CliFramework ProjectReference |
| `src/App/Program.cs` | Full rewrite (see above) |
| `src/CLI/Azure/AzureCommandModule.cs` | Remove interface, rename, delete event source listener |
| `src/Azure/TranslateService.cs` | Rewrite logging (see before/after above) |
| `src/Azure/OpenAiService.cs` | Same pattern |
| `src/Azure/SpeechSttService.cs` | Same pattern |
| `src/Azure/SpeechTtsService.cs` | Same pattern |
| `src/Azure/TextAnalyticsService.cs` | Same pattern (5 methods) |
| `src/Azure/VisionService.cs` | Same pattern |
| `src/Azure/DocIntelService.cs` | Same pattern (includes `op.Fail()` → `activity.Complete(LogEventLevel.Warning)`) |
| `src/CLI/Azure/*.cs` (11 files) | `CommandBase` → `AsyncCommand`, `Ui.Info` → `Terminal.Info`, remove `using CliFramework` |

### Delete (1 file + references)

| File | Reason |
|---|---|
| `src/CLI/CliModuleRegistry.cs` | Module system eliminated. Commands registered directly. |

## Migration Steps (dependency-safe order)

### Phase 0: CliFramework removal
1. Add `Spectre.Console.Cli.Extensions.DependencyInjection` to `Directory.Packages.props`
2. Remove CliFramework `<ProjectReference>` from `App.csproj` and `CLI.csproj`
3. Remove all `using CliFramework*` imports from 16 source files
4. In `AzureCommandModule.cs`: remove `ICommandModule` interface, remove `AzureEventSourceListener` line
5. Rename `AzureCommandModule` → `AzureServices` (static class, static methods)
6. Delete `CliModuleRegistry.cs`
7. Update 11 command files: `CommandBase<T>` → `AsyncCommand<T>`, `ExecuteCommandAsync` → `ExecuteAsync`
8. Build should now fail (missing `TypeRegistrar`, `Ui`, `LogPipeline`, `Host`). Fix with phases 1-3.

### Phase 1: Foundation
9. Add `SerilogTracing`, `Serilog.Sinks.Spectre`, `Spectre.Console` to `Directory.Packages.props`
10. Update `Logging.csproj` (add 3 packages, remove `Serilog.Sinks.Console`)
11. Write `src/Logging/Log.cs` (full rewrite)
12. Write `src/Logging/Terminal.cs`
13. Build: should succeed for `Logging` project only.

### Phase 2: Azure services
14. Rewrite all 7 Azure service files (45 call sites → SerilogTracing)
15. Build: should succeed for `Azure` project.

### Phase 3: Program.cs + CLI commands
16. Rewrite `Program.cs` (verbose parsing, DI registrar, shutdown handlers, error output)
17. Replace `Ui.Info(result)` with `Terminal.Info(result)` in all 11 CLI commands
18. Build: full solution should compile.

### Phase 4: Verification
19. `dotnet build` — zero errors
20. `dotnet run -- --help` — CLI works
21. Grep checks (see below)

## Verification Criteria

| Check | Command | Expected |
|---|---|---|
| Build succeeds | `dotnet build` | Exit code 0, zero errors |
| No CliFramework refs | `rg 'CliFramework' src/` | Zero matches |
| No old logging API | `rg 'Log\.Emit\|Log\.BeginOperation' src/` | Zero matches |
| No bare Console | `rg 'Console\.WriteLine' src/` | Zero matches (except `Program.cs` env check) |
| No bare AnsiConsole | `rg 'AnsiConsole\.' src/` | Only in `Terminal.cs` |
| No ApiRequested/ApiResponded | `rg 'ApiRequested\|ApiResponded' src/` | Zero matches |
| All terminal output via Terminal | `rg 'Terminal\.' src/CLI/` | 11 matches (one per command) |
| SerilogTracing setup present | `rg 'TraceToSharedLogger' src/Logging/Log.cs` | 1 match |
| Listener field PascalCase | `rg '_listener\|_Listener' src/Logging/` | Zero matches (no underscore-prefixed fields) |
| No `using Serilog;` in service files | `rg 'using Serilog;' src/Azure/` | Zero matches (services use `using Serilog.Events;` only) |
| Absolute file path in sink | `rg 'SpecialFolder\.UserProfile' src/Logging/Log.cs` | 1 match |
| Module extensibility documented | `rg 'Module system extensibility' .omo/plans/` | 1 match |

## Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| `AsyncCommand<T>` API differs from `CommandBase<T>` | Low | Only method name changes (`ExecuteCommandAsync` → `ExecuteAsync`). Same signature otherwise. |
| `DependencyInjectionRegistrar` has different behavior | Low | Same DI container, just a bridge class. Well-tested package (6K+ downloads, Spectre official wiki recommends it). |
| SerilogTracing `ActivityListener` lifetime | Medium | Stored in `Listener` field (PascalCase) to keep alive for process lifetime. If disposed early, HttpClient stops producing spans. |
| `WriteTo.Spectre()` + `Terminal.cs` double console output | Low | They're complementary. Serilog sink handles structured log events. Terminal handles user-facing messages. Both route through AnsiConsole's queue. |
| Missing CliFramework types at compile time | **High** | Pre-existing issue. Phase 0 must complete before anything else compiles. Run phases in order. |

## Out of Scope

- **Module system extensibility** — The module system (`ICommandModule`) is eliminated because only Azure has commands. If future services (Music, Sync, etc.) are added, reintroduce a simple list of `(Action<IServiceCollection>, Action<IConfigurator>)` tuples in `Program.cs`. Do not recreate the full `ICommandModule` interface.
- **Structured output (tables, panels)** — `Terminal.Info(string)` covers current use cases.
- **Progress bars / spinners** — Not currently used.
- **DI container refactoring** — Keep `ServiceCollection` pattern. Just swap the Spectre bridge.
- **SerilogTracing.Expressions** — Not needed. Standard Serilog formatting is sufficient.
- **Sampling** — Default `Sample.AllTraces()` is fine for a CLI tool.
