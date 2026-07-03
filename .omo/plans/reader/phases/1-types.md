# Phase 1: Types + Records

## Tasks

### Task 6: Create Reader.csproj

**What to do:**
Create `src/Services/Reader/Reader.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Patchright" />
    <PackageReference Include="Polly" />
    <PackageReference Include="Moongazing.Veil" />
    <PackageReference Include="Azure.Storage.Blobs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

**Key decisions:**
- No Shared.csproj (YAGNI for solo dev)
- All browser/anti-detection files live directly in `src/Services/Reader/`
- FallbackChain<T> and ResiliencePipelineFactory inlined into PdfFetcher (only consumer)

**References:**
- `src/Services/Azure/Azure.csproj:1-18` (pattern to mirror)

**Acceptance criteria:**
- `dotnet build src/Services/Reader/Reader.csproj` succeeds (even with empty .cs files)

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```
Expected: Clean build

**Commit:** `feat(reader): add Services.Reader project skeleton`

---

### Task 7: Create DownloadConfig.cs

**What to do:**
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

**Commit:** `feat(reader): add DownloadConfig record`

---

### Task 8: Create DownloadResult.cs

**What to do:**
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

**Commit:** `feat(reader): add DownloadResult record`

---

### Task 9: Create BrowserPage.cs

**What to do:**
Create `src/Services/Reader/BrowserPage.cs`:

```csharp
using Patchright;

namespace Services.Reader;

public sealed record BrowserPage(IPage Page, IBrowserContext Context) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Page.DisposeAsync();
        await Context.DisposeAsync();
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

**Commit:** `feat(reader): add BrowserPage record`

---

### Task 10: Create ReaderCredentials.cs

**What to do:**
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

**Commit:** `feat(reader): add ReaderCredentials`

---

### Task 11: Create ReaderState.cs

**What to do:**
Create `src/Services/Reader/ReaderState.cs`:

```csharp
using System.Text.Json;
using Core;

namespace Services.Reader;

public sealed class ReaderState
{
    private readonly string _manifestPath;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ReaderState(string manifestPath) => _manifestPath = manifestPath;

    public async Task RecordAsync(DownloadResult result)
    {
        var entries = await ReadManifestAsync();
        entries.Add(result);
        await WriteManifestAsync(entries);
    }

    public async Task<List<DownloadResult>> ReadAllAsync() => await ReadManifestAsync();

    private async Task<List<DownloadResult>> ReadManifestAsync()
    {
        if (!File.Exists(_manifestPath)) return [];
        var json = await File.ReadAllTextAsync(_manifestPath);
        return JsonSerializer.Deserialize<List<DownloadResult>>(json, Json) ?? [];
    }

    private async Task WriteManifestAsync(List<DownloadResult> entries)
    {
        var tempPath = _manifestPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(entries, Json));
        File.Move(tempPath, _manifestPath, overwrite: true);
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

**Commit:** `feat(reader): add ReaderState with atomic write`

---

## Verify Phase 1

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Clean build. All types in place.

**Dependencies:** Phase 0
**Blocks:** Phase 2
