# Phase 6: Reader Plan Modifications — Drop Polly, Hand-Roll, Merge Files

## Task 14: Create RecoverAsync extension

Create `src/Core/ErrorOrExtensions.cs`:

```csharp
namespace Core;

public static class ErrorOrExtensions
{
    public static async Task<ErrorOr<T>> RecoverAsync<T>(
        this Task<ErrorOr<T>> source,
        Func<List<Error>, Task<ErrorOr<T>>> fallback)
    {
        var result = await source;
        return result.IsError ? await fallback(result.Errors) : result;
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Name it `ElseAsync` — use `RecoverAsync` to avoid collision with future ErrorOr additions

**QA:**
```bash
dotnet build src/Core/Core.csproj
```

**Commit:** `feat(core): add RecoverAsync extension for ErrorOr fallback chaining`

---

## Task 15: Create Reader.csproj WITHOUT Polly

Create `src/Services/Reader/Reader.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Patchright" />
    <PackageReference Include="Azure.Storage.Blobs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

**Key decisions:**
- NO `Polly` — hand-rolled FallbackChain replaces it
- NO `Moongazing.Veil` — too obscure, inline HTTP stealth if needed
- Only `Patchright` (browser) + `Azure.Storage.Blobs` (upload)

**QA:**
```bash
dotnet restore && dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add Services.Reader project skeleton (no Polly)`

---

## Task 16: Create DownloadConfig.cs + DownloadResult.cs

Create `src/Services/Reader/DownloadConfig.cs`:

```csharp
namespace Services.Reader;

public sealed record DownloadConfig
{
    public required Uri Uri { get; init; }
    public string? Doi { get; init; }
    public string? DoiUrl { get; init; }
    public long MaxFileSizeBytes { get; init; } = 100 * 1024 * 1024;
}
```

Create `src/Services/Reader/DownloadResult.cs`:

```csharp
namespace Services.Reader;

public sealed record DownloadResult
{
    public required Uri OriginalUrl { get; init; }
    public string? BlobUrl { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public int? Tier { get; init; }

    public static DownloadResult Success(Uri url, string blobUrl, int tier)
        => new() { OriginalUrl = url, BlobUrl = blobUrl, Tier = tier };

    public static DownloadResult Failure(Uri url, string error)
        => new() { OriginalUrl = url, Error = error };
}
```

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add DownloadConfig + DownloadResult records`

---

## Task 17: Create ReaderCredentials.cs

Create `src/Services/Reader/ReaderCredentials.cs`:

```csharp
namespace Services.Reader;

public sealed class ReaderCredentials
{
    public string? ScrapeDoApiKey { get; init; }
    public string? ScrapflyApiKey { get; init; }
    public string? OpenAlexApiKey { get; init; }
    public string? CoreApiKey { get; init; }
    public string? CapsolverApiKey { get; init; }

    public static ReaderCredentials Read() => new()
    {
        ScrapeDoApiKey = Environment.GetEnvironmentVariable("SCRAPE_DO_API_KEY"),
        ScrapflyApiKey = Environment.GetEnvironmentVariable("SCRAPFLY_API_KEY"),
        OpenAlexApiKey = Environment.GetEnvironmentVariable("OPENALEX_API_KEY"),
        CoreApiKey = Environment.GetEnvironmentVariable("CORE_API_KEY"),
        CapsolverApiKey = Environment.GetEnvironmentVariable("CAPSOLVER_API_KEY"),
    };
}
```

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add ReaderCredentials`

---

## Task 18: Create BrowserSetup.cs WITH BrowserPage as private nested record

Create `src/Services/Reader/BrowserSetup.cs`:

```csharp
using ErrorOr;
using Patchright;

namespace Services.Reader;

public sealed class BrowserSetup
{
    private readonly Lazy<Task<IBrowser>> _browser;

    public BrowserSetup()
    {
        _browser = new Lazy<Task<IBrowser>>(CreateBrowserAsync);
    }

    public async Task<ErrorOr<BrowserPage>> CreatePageAsync(CancellationToken ct)
    {
        return await CreateContextAsync()
            .ThenAsync(async ctx =>
            {
                var page = await ctx.NewPageAsync();
                return new BrowserPage(page, ctx);
            });
    }

    private async Task<ErrorOr<IBrowserContext>> CreateContextAsync()
    {
        var browser = await _browser.Value;
        return await browser.NewContextAsync();
    }

    private static async Task<IBrowser> CreateBrowserAsync()
    {
        return await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--disable-blink-features=AutomationControlled"]
        });
    }

    public sealed record BrowserPage(IPage Page, IBrowserContext Context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Page.DisposeAsync();
            await Context.DisposeAsync();
        }
    }
}
```

**Key decision:** `BrowserPage` is a private nested record inside `BrowserSetup`. No separate file. 10-line record, single consumer.

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add BrowserSetup with nested BrowserPage record`

---

## Task 19: Create GhostNoise.cs + CaptchaSolver.cs + AnubisPowSolver.cs

Create `src/Services/Reader/GhostNoise.cs` — same as original plan (40 lines of JS injection).

Create `src/Services/Reader/CaptchaSolver.cs` — same as original plan (45 lines).

Create `src/Services/Reader/AnubisPowSolver.cs` — same as original plan (30 lines).

**QA after each:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add GhostNoise + CaptchaSolver + AnubisPowSolver`

---

## Task 20: Create SiteResolver.cs + OpenAccessResolver.cs

Create `src/Services/Reader/SiteResolver.cs` — same as original plan (65 lines with SSRF protection).

Create `src/Services/Reader/OpenAccessResolver.cs` — same as original plan (9 providers, inline foreach).

**QA after each:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add SiteResolver + OpenAccessResolver`

---

## Task 21: Create PdfFetcher.cs WITH hand-rolled FallbackChain (NO Polly)

Create `src/Services/Reader/PdfFetcher.cs`:

```csharp
using System.Net.Http.Headers;
using Core;
using ErrorOr;

namespace Services.Reader;

public sealed class PdfFetcher(
    ReaderCredentials credentials,
    BrowserSetup browserSetup,
    GhostNoise ghostNoise)
{
    private const int MaxFileSizeMb = 100;
    private const int MaxRetriesPerTier = 3;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9)];

    public async Task<ErrorOr<byte[]>> FetchAsync(Uri url, CancellationToken ct)
    {
        return await new FallbackChain<byte[]>()
            .Try(ct => RetryAsync(ct2 => FetchViaHttpClient(url, ct2), ct))
            .Try(ct => RetryAsync(ct2 => FetchViaScrapingApi(url, ct2), ct))
            .Try(ct => RetryAsync(ct2 => FetchViaHttpCloak(url, ct2), ct))
            .Try(ct => FetchViaBrowser(url, ct))
            .ExecuteAsync(ct);
    }

    private static async Task<ErrorOr<T>> RetryAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> action, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxRetriesPerTier; attempt++)
        {
            var result = await action(ct);
            if (result.IsSuccess) return result;
            if (attempt < MaxRetriesPerTier - 1)
                await Task.Delay(RetryDelays[attempt], ct);
        }
        return await action(ct);
    }

    private static async Task<ErrorOr<byte[]>> FetchViaHttpClient(Uri url, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        try
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return ValidatePdf(bytes);
        }
        catch (Exception ex) { return Errors.Reader.FetchFailed(url.ToString(), ex.Message); }
    }

    private async Task<ErrorOr<byte[]>> FetchViaScrapingApi(Uri url, CancellationToken ct)
    {
        if (credentials.ScrapeDoApiKey is null)
            return Errors.Reader.ScrapingApiKeyMissing;
        using var http = new HttpClient();
        var apiUrl = $"https://api.scrape.do?url={Uri.EscapeDataString(url.ToString())}";
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ScrapeDoApiKey);
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return ValidatePdf(bytes);
        }
        catch (Exception ex) { return Errors.Reader.FetchFailed(url.ToString(), ex.Message); }
    }

    private static async Task<ErrorOr<byte[]>> FetchViaHttpCloak(Uri url, CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler
        {
            EnableHttp3 = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        try
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return ValidatePdf(bytes);
        }
        catch (Exception ex) { return Errors.Reader.FetchFailed(url.ToString(), ex.Message); }
    }

    private async Task<ErrorOr<byte[]>> FetchViaBrowser(Uri url, CancellationToken ct)
    {
        var pageResult = await browserSetup.CreatePageAsync(ct);
        if (pageResult.IsError) return Errors.Reader.BrowserSetupFailed;

        await using var page = pageResult.Value;
        await ghostNoise.ApplyAll(page.Page);
        try
        {
            await page.Page.GotoAsync(url.ToString(), new() { WaitUntil = WaitUntilState.NetworkIdle });
            var pdfBytes = await page.Page.PdfAsync();
            return ValidatePdf(pdfBytes);
        }
        catch (Exception ex) { return Errors.Reader.FetchFailed(url.ToString(), ex.Message); }
    }

    private static ErrorOr<byte[]> ValidatePdf(byte[] bytes)
    {
        var maxSize = MaxFileSizeMb * 1024 * 1024;
        if (bytes.Length > maxSize)
            return Errors.Reader.FileTooLarge(bytes.Length, maxSize);
        if (bytes.Length < 5 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
            return Errors.Reader.InvalidPdfResponse;
        return bytes;
    }

    private sealed class FallbackChain<T>
    {
        private readonly List<Func<CancellationToken, Task<ErrorOr<T>>>> _tiers = [];

        public FallbackChain<T> Try(Func<CancellationToken, Task<ErrorOr<T>>> tier)
        { _tiers.Add(tier); return this; }

        public async Task<ErrorOr<T>> ExecuteAsync(CancellationToken ct)
        {
            var errors = new List<string>();
            foreach (var tier in _tiers)
            {
                var result = await tier(ct);
                if (result.IsSuccess) return result;
                errors.AddRange(result.Errors.Select(e => e.Description));
            }
            return Errors.Reader.AllTiersFailed(string.Join("; ", errors));
        }
    }
}
```

**Key decisions:**
- NO Polly dependency. `FallbackChain<T>` is a private nested class (20 LOC).
- `RetryAsync` is a private static helper (10 LOC) — exponential backoff within each tier.
- Each tier is a named method: `FetchViaHttpClient`, `FetchViaScrapingApi`, `FetchViaHttpCloak`, `FetchViaBrowser`.
- `ValidatePdf` checks magic bytes + size limit after every tier.
- Errors accumulate across tiers for debugging visibility.

**Must NOT:**
- Import Polly
- Use Polly's `ResiliencePipeline`
- Separate FallbackChain into its own file

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add PdfFetcher with hand-rolled FallbackChain (no Polly)`

---

## Task 22: Create BlobUploader.cs

Create `src/Services/Reader/BlobUploader.cs` — same as original plan (25 lines).

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add BlobUploader with SHA-256 dedup`

---

## Task 23: Create ReaderService.cs WITH RecoverAsync (NOT ElseAsync)

Create `src/Services/Reader/ReaderService.cs`:

```csharp
using Core;
using ErrorOr;

namespace Services.Reader;

public sealed class ReaderService(
    BlobUploader blobUploader,
    OpenAccessResolver oalResolver,
    SiteResolver siteResolver,
    PdfFetcher pdfFetcher,
    ReaderState state)
{
    public async Task<ErrorOr<DownloadResult>> DownloadAsync(DownloadConfig config, CancellationToken ct)
    {
        return await ResolveDoiIfNeeded(config, ct)
            .ThenAsync(cfg => oalResolver.TryResolveAsync(cfg, ct))
            .RecoverAsync(async _ => await FallbackToFetch(config, ct))
            .ThenAsync(bytes => blobUploader.UploadAsync(bytes, ct))
            .ThenAsync(blobUrl => RecordResultAsync(blobUrl, config))
            .Match(
                blobUrl => DownloadResult.Success(config.Uri, blobUrl, 0),
                errors => DownloadResult.Failure(config.Uri, errors.First().Description));
    }

    private async Task<ErrorOr<DownloadConfig>> ResolveDoiIfNeeded(DownloadConfig config, CancellationToken ct)
    {
        if (config.Doi is not null) return config;
        if (config.DoiUrl is null) return config;
        var resolved = await siteResolver.ExtractDoiAsync(config.DoiUrl);
        return resolved.IsSuccess ? config with { Doi = resolved.Value } : config;
    }

    private async Task<ErrorOr<byte[]>> FallbackToFetch(DownloadConfig config, CancellationToken ct)
    {
        return await siteResolver.ResolveAsync(config.Uri)
            .ThenAsync(url => pdfFetcher.FetchAsync(new Uri(url), ct));
    }

    private async Task<ErrorOr<string>> RecordResultAsync(string blobUrl, DownloadConfig config)
    {
        await state.RecordAsync(DownloadResult.Success(config.Uri, blobUrl, 0));
        return blobUrl;
    }
}
```

**Key decision:** Uses `RecoverAsync` (from `ErrorOrExtensions.cs`) instead of `ElseAsync`. The chain reads: resolve DOI → try open access → RECOVER via direct fetch → upload → record.

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add ReaderService orchestrator with RecoverAsync chain`

---

## Task 24: Create ReaderState.cs (merged into ReaderService as private methods — SKIP separate file)

**DECISION: SKIP.** ReaderState is 35 lines of JSON read/write. Merge into ReaderService as private methods.

Instead, add these private methods directly inside `ReaderService.cs`:

```csharp
    private readonly string _manifestPath = Path.Combine(PathResolver.RepoRoot, "state", "reader", "manifest.json");
    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { WriteIndented = true };

    private async Task RecordResultAsync(string blobUrl, DownloadConfig config)
    {
        var entries = await ReadManifestAsync();
        entries.Add(DownloadResult.Success(config.Uri, blobUrl, 0));
        await WriteManifestAsync(entries);
    }

    public async Task<IReadOnlyList<DownloadResult>> ReadAllAsync() => await ReadManifestAsync();

    private async Task<List<DownloadResult>> ReadManifestAsync()
    {
        if (!File.Exists(_manifestPath)) return [];
        var json = await File.ReadAllTextAsync(_manifestPath);
        return System.Text.Json.JsonSerializer.Deserialize<List<DownloadResult>>(json, Json) ?? [];
    }

    private async Task WriteManifestAsync(List<DownloadResult> entries)
    {
        var tempPath = _manifestPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, System.Text.Json.JsonSerializer.Serialize(entries, Json));
        File.Move(tempPath, _manifestPath, overwrite: true);
    }
```

**No separate `ReaderState.cs` file.** State persistence is an internal detail of ReaderService.

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): inline ReaderState into ReaderService as private methods`

---

## Task 25: Create ReaderSetup.cs

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

        services.AddSingleton(creds);
        services.AddSingleton<BrowserSetup>();
        services.AddSingleton<GhostNoise>();
        services.AddSingleton<CaptchaSolver>();
        services.AddSingleton<AnubisPowSolver>();
        services.AddSingleton<SiteResolver>();
        services.AddSingleton<OpenAccessResolver>();
        services.AddSingleton<PdfFetcher>();
        services.AddSingleton<BlobUploader>();
        services.AddSingleton<ReaderService>();
    }
}
```

**Note:** No `ReaderState` registration — state is inlined in ReaderService.

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```

**Commit:** `feat(reader): add ReaderSetup with DI registration`

---

## Task 26: Create CLI commands (merge StatusCommand into DownloadCommand)

Create `src/CLI/Reader/ReaderCommandModule.cs`:

```csharp
using Spectre.Console.Cli;

namespace CLI.Reader;

public static class ReaderCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg) =>
        cfg.AddBranch("reader", b =>
        {
            b.SetDescription("PDF extraction from academic sites");
            b.AddCommand<DownloadCommand>("download");
            b.AddCommand<BatchCommand>("batch");
            b.AddCommand<HealthCommand>("health");
        });
}
```

**Note:** No `status` subcommand — merged into DownloadCommand as `--status` flag.

Create `src/CLI/Reader/DownloadCommand.cs`:

```csharp
using Core;
using Services.Reader;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Reader;

public sealed class DownloadCommand(ReaderService service) : AsyncCommand<DownloadCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("URL to download PDF from")]
        [CommandArgument(0, "[url]")]
        public string? Url { get; init; }

        [System.ComponentModel.Description("Show download history")]
        [CommandOption("--status")]
        public bool Status { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.Status)
        {
            var entries = await service.ReadAllAsync();
            foreach (var entry in entries)
                AnsiConsole.MarkupLine($"{entry.OriginalUrl} -> {entry.BlobUrl ?? entry.Error}");
            return 0;
        }

        if (settings.Url is null)
        {
            AnsiConsole.MarkupLine("[red]URL is required. Use --status to view history.[/]");
            return 1;
        }

        var config = new DownloadConfig { Uri = new Uri(settings.Url) };
        var result = await service.DownloadAsync(config, CancellationToken.None);
        return result.Match(_ => 0, _ => 1);
    }
}
```

**Note:** `--status` flag replaces separate StatusCommand.

Create `src/CLI/Reader/BatchCommand.cs` — same as original plan (25 lines).

Create `src/CLI/Reader/HealthCommand.cs` — same as original plan (25 lines).

**QA:**
```bash
dotnet build src/CLI/CLI.csproj
```

**Commit:** `feat(reader): add CLI commands (download + batch + health, status merged into download)`

---

## Task 27: Wire Reader into solution + App

1. Add to `Toolbox.slnx`:
```xml
<Project Path="src/Services/Reader/Reader.csproj" />
```

2. Add `<ProjectReference Include="..\Services\Reader\Reader.csproj"/>` to `src/CLI/CLI.csproj`.

3. In `src/App/Program.cs`:
   - Add `using CLI.Reader;`
   - Add `using Services.Reader;`
   - Add `services.AddReaderServices();` after `services.AddLastFmServices();`
   - Add `ReaderCommandModule.ConfigureCommands(cfg: cfg);` after `SyncCommandModule.ConfigureCommands(cfg: cfg);`

4. Add `Reader` to `ServiceName` enum in `src/Core/ServiceName.cs`.

5. Add `ServiceName.Reader => "reader",` to switch in `src/Core/ServiceNameExtensions.cs`.

6. Add `Errors.Reader` class to `src/Core/Errors.cs` (same as original plan — 12 error factory methods).

**QA:**
```bash
dotnet restore && dotnet build
dotnet run --project src/App -- reader --help
```
Expected: Build succeeds. Help shows "PDF extraction from academic sites".

**Commit:** `feat(reader): wire Reader into solution + App + Core`
