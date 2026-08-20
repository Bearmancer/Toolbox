# Phase 4: CLI + DI

## Tasks

### Task 18: Create PristineSetup.cs

`src/Services/Pristine/PristineSetup.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Services.Pristine;

public static class PristineSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPristineServices()
        {
            services.AddSingleton<PristineBrowser>();
            services.AddSingleton<PristineDownloader>();
            services.AddSingleton<PristineLoginService>();
            services.AddSingleton<PristineAlbumService>();
            services.AddSingleton<PristinePollService>();
            services.AddSingleton<PristineOrchestrator>();
            services.AddHttpClient<PristineDownloader>();
            return services;
        }
    }
}
```

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineSetup DI`

---

### Task 19: Create PristineCommandModule.cs

`src/CLI/Pristine/PristineCommandModule.cs`:

```csharp
using Spectre.Console.Cli;

namespace CLI.Pristine;

public static class PristineCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg)
    {
        cfg.AddBranch("pristine", b =>
        {
            b.SetDescription("Pristine Classical PASC downloader");
            b.AddCommand<PristineLoginCommand>("login");
            b.AddCommand<PristineDownloadCommand>("download");
        });
    }
}
```

**QA:** `dotnet build src/CLI/CLI.csproj` (after adding Pristine reference)

**Commit:** `feat(pristine): add PristineCommandModule`

---

### Task 20: Create PristineLoginCommand.cs

`src/CLI/Pristine/PristineLoginCommand.cs`:

```csharp
using Services.Pristine;
using Spectre.Console.Cli;

public sealed class PristineLoginCommand(PristineLoginService service) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext ctx) => await service.LoginAsync() ? 0 : 1;
}
```

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineLoginCommand`

---

### Task 21: Create PristineDownloadCommand.cs

`src/CLI/Pristine/PristineDownloadCommand.cs`:

```csharp
using Services.Pristine;
using Spectre.Console.Cli;

public sealed class PristineDownloadCommand(PristineOrchestrator orch) : AsyncCommand<PristineDownloadCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-c|--code")] public string[] Codes { get; init; } = [];
        [CommandOption("-o|--out-dir")] public string? OutDir { get; init; }
        [CommandOption("-H|--headless")] public bool Headless { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext ctx, Settings s)
    {
        if (s.Headless) Environment.SetEnvironmentVariable("PRISTINE_HEADLESS", "1");
        var result = await orch.DownloadAsync(s.Codes.Length > 0 ? s.Codes : null, s.OutDir);
        return result.Match(_ => 0, e => { AnsiConsole.MarkupLine($"[red]{e.First().Description}[/]"); return 1; });
    }
}
```

Mirrors `toolkit pristine download --code --out-dir --headless`.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineDownloadCommand`

---

### Task 22: Wire Pristine into CLI + App

1. `src/CLI/CLI.csproj` add `<ProjectReference Include="..\Services\Pristine\Pristine.csproj" />`
2. `src/App/Program.cs`:
   - `using Services.Pristine;`
   - after `services.AddLastFmServices();` → `services.AddPristineServices();`
   - after `SyncCommandModule.ConfigureCommands(cfg);` → `PristineCommandModule.ConfigureCommands(cfg);`

**QA:**

```bash
dotnet build
dotnet run --project src/App -- pristine --help
dotnet run --project src/App -- pristine login --help
dotnet run --project src/App -- pristine download --help
```

**Commit:** `feat(pristine): wire Pristine into CLI+App`

## Final verification

```bash
dotnet build
dotnet run --project src/App -- pristine --help
# live smoke (requires PRISTINE_BASE_OUT_DIR + auth.json):
# dotnet run --project src/App -- pristine download -c PASC552 --headless
```

**Dependencies:** Phase 3
**Blocks:** None

## Firefox DevTools MCP live-check (post-wire, pre-release)

Before marking done, repeat Phase 1 Task 10 live-check against real site with new C# selectors — confirm poll captures `.flac` URLs via `firefox-devtools_list_network_requests urlContains=.flac` during playback. If selector drift, patch and re-QA.
