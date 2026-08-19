# Phase 4: CLI + DI Wiring

## Tasks

### Task 21: Create ReaderCommandModule.cs

**What to do:**
Create `src/CLI/Reader/ReaderCommandModule.cs`:

```csharp
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class ReaderCommandModule : ICommandModule
{
    public void ConfigureCommands(ICommandConfigurator configurator)
    {
        configurator.AddBranch("reader", branch =>
        {
            branch.SetDescription("PDF extraction from academic sites");
            branch.AddCommand<DownloadCommand>("download");
            branch.AddCommand<BatchCommand>("batch");
            branch.AddCommand<StatusCommand>("status");
            branch.AddCommand<HealthCommand>("health");
        });
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds (after project reference added)

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add ReaderCommandModule`

---

### Task 22: Create DownloadCommand.cs

**What to do:**
Create `src/CLI/Reader/DownloadCommand.cs`:

```csharp
using Services.Reader;
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class DownloadCommand : AsyncCommand<DownloadCommand.Settings>
{
    private readonly ReaderService _service;

    public DownloadCommand(ReaderService service) => _service = service;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<url>")] public string Url { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var uri = new Uri(settings.Url);
        var config = new DownloadConfig { Uri = uri };
        var result = await _service.DownloadAsync(config, CancellationToken.None);
        return result.Match(_ => 0, _ => 1);
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add DownloadCommand`

---

### Task 23: Create BatchCommand.cs

**What to do:**
Create `src/CLI/Reader/BatchCommand.cs`:

```csharp
using System.Collections.Concurrent;
using Services.Reader;
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class BatchCommand : AsyncCommand<BatchCommand.Settings>
{
    private readonly ReaderService _service;

    public BatchCommand(ReaderService service) => _service = service;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")] public string UrlFile { get; init; } = "";
        [CommandOption("--parallel")] public int Parallelism { get; init; } = 4;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var urls = await File.ReadAllLinesAsync(settings.UrlFile);
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.Parallelism };
        var results = new ConcurrentBag<DownloadResult>();
        await Parallel.ForEachAsync(urls, options, async (url, ct) =>
        {
            var config = new DownloadConfig { Uri = new Uri(url) };
            var result = await _service.DownloadAsync(config, ct);
            results.Add(result.Match(r => r, e => DownloadResult.Failure(config.Uri, e.First().Description)));
        });
        return results.All(r => r.Error is null) ? 0 : 1;
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add BatchCommand with parallelism`

---

### Task 24: Create StatusCommand.cs

**What to do:**
Create `src/CLI/Reader/StatusCommand.cs`:

```csharp
using Services.Reader;
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class StatusCommand : AsyncCommand
{
    private readonly ReaderState _state;

    public StatusCommand(ReaderState state) => _state = state;

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var entries = await _state.ReadAllAsync();
        foreach (var entry in entries)
            Console.WriteLine($"{entry.OriginalUrl} -> {entry.BlobUrl ?? entry.Error}");
        return 0;
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add StatusCommand`

---

### Task 25: Create HealthCommand.cs

**What to do:**
Create `src/CLI/Reader/HealthCommand.cs`:

```csharp
using Services.Reader;
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class HealthCommand : AsyncCommand
{
    private readonly BrowserSetup _browserSetup;

    public HealthCommand(BrowserSetup browserSetup) => _browserSetup = browserSetup;

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var browser = await _browserSetup.CreatePageAsync(CancellationToken.None);
        return await browser.Match(
            async page =>
            {
                await page.Page.GotoAsync("https://example.com");
                var title = await page.Page.TitleAsync();
                await page.DisposeAsync();
                Console.WriteLine($"Browser OK: {title}");
                return 0;
            },
            errors =>
            {
                Console.WriteLine($"Browser failed: {errors.First().Description}");
                return Task.FromResult(1);
            });
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add HealthCommand`

---

### Task 26: Create ReaderSetup.cs

**What to do:**
Create `src/Services/Reader/ReaderSetup.cs`:

```csharp
using Azure.Storage.Blobs;
using Core;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Reader;

public static class ReaderSetup
{
    public static void AddReaderServices(this IServiceCollection services)
    {
        var creds = ReaderCredentials.Read();

        services.AddSingleton(new BlobContainerClient(
            new Uri("https://reader.blob.core.windows.net/pdfs"),
            new Azure.Storage.StorageSharedKeyCredential(
                Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT")!,
                Environment.GetEnvironmentVariable("AZURE_STORAGE_KEY")!)));

        services.AddSingleton<ReaderCredentials>(creds);
        services.AddSingleton<BrowserSetup>();
        services.AddSingleton<GhostNoise>();
        services.AddSingleton<CaptchaSolver>();
        services.AddSingleton<AnubisPowSolver>();
        services.AddSingleton<SiteResolver>();
        services.AddSingleton<OpenAccessResolver>();
        services.AddSingleton<PdfFetcher>();
        services.AddSingleton<BlobUploader>();
        services.AddSingleton<ReaderState>(new ReaderState(
            Path.Combine(PathResolver.RepoRoot, "state", "reader", "manifest.json")));
        services.AddSingleton<ReaderService>();
    }
}
```

**Must NOT:**

- Use block-scoped namespaces
- Add comments

**Acceptance criteria:**

- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Expected: Clean build

**Commit:** `feat(reader): add ReaderSetup with DI registration`

---

### Task 27: Wire Reader into CLI and App

**What to do:**

1. Add `<ProjectReference Include="..\Services\Reader\Reader.csproj"/>` to `src/CLI/CLI.csproj` (after LastFm.csproj reference, line 12)
2. In `src/App/Program.cs`:
   - Add `using CLI.Reader;` (after `using CLI.Azure;`)
   - Add `using Services.Reader;` (after `using Services.Google;`)
   - Add `services.AddReaderServices();` after `services.AddLastFmServices();`
   - Add `ReaderCommandModule.ConfigureCommands(cfg: cfg);` after `SyncCommandModule.ConfigureCommands(cfg: cfg);`

**Must NOT:**

- Reorder existing registrations
- Change error handling

**References:**

- `src/CLI/CLI.csproj:1-13`
- `src/App/Program.cs:36-58`

**Acceptance criteria:**

- `dotnet build` succeeds
- `dotnet run --project src/App -- reader --help` shows command description

**QA:**

```bash
dotnet build
dotnet run --project src/App -- reader --help
```

Expected: Build succeeds, help output shows "PDF extraction from academic sites"

**Commit:** `feat(reader): wire Reader into CLI and App`

---

## Final Verification

```bash
dotnet build
dotnet run --project src/App -- reader health
```

Full solution builds. Smoke test: browser launches, navigates to example.com, prints title, returns 0.

**Dependencies:** Phase 3
**Blocks:** None (final phase)
