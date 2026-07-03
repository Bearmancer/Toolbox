# Phase 3: Services

## Tasks

### Task 16: Create SiteResolver.cs

**What to do:**
Create `src/Services/Reader/SiteResolver.cs`:

```csharp
using Core;

namespace Services.Reader;

public sealed class SiteResolver
{
    private static readonly IdnMapping Idn = new();
    private static readonly IReadOnlyDictionary<string, Func<Uri, string>> SimpleRoutes = new Dictionary<string, Func<Uri, string>>
    {
        [".sciencedirect.com"] = u => $"https://www.sciencedirect.com{u.AbsolutePath}",
        [".nature.com"] = u => $"https://www.nature.com{u.AbsolutePath}",
        [".springer.com"] = u => $"https://link.springer.com{u.AbsolutePath}",
        [".cell.com"] = u => $"https://www.cell.com{u.AbsolutePath}",
        [".thelancet.com"] = u => $"https://www.thelancet.com{u.AbsolutePath}",
        [".jamanetwork.com"] = u => $"https://jamanetwork.com{u.AbsolutePath}",
        [".nejm.org"] = u => $"https://www.nejm.org{u.AbsolutePath}",
        [".bmj.com"] = u => $"https://www.bmj.com{u.AbsolutePath}",
        [".ieee.org"] = u => $"https://ieeexplore.ieee.org{u.AbsolutePath}",
        [".mdpi.com"] = u => $"https://www.mdpi.com{u.AbsolutePath}",
        [".pnas.org"] = u => $"https://www.pnas.org{u.AbsolutePath}",
        [".jstor.org"] = u => $"https://www.jstor.org{u.AbsolutePath}",
        [".oxfordacademic.com"] = u => $"https://academic.oup.com{u.AbsolutePath}",
        [".tandfonline.com"] = u => $"https://www.tandfonline.com{u.AbsolutePath}",
        [".sagepub.com"] = u => $"https://journals.sagepub.com{u.AbsolutePath}",
    };

    private static readonly string[] BrowserRequired = [".researchgate.net", ".academia.edu"];

    public ErrorOr<string> Resolve(Uri url) =>
        DecodePunycode(url)
            .Then(host => ValidateSsrf(host))
            .Then(host => RouteToPdfUrl(host, url));

    private static ErrorOr<string> DecodePunycode(Uri url) =>
        Idn.GetUnicode(url.Host);

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase) { "localhost", "[::1]", "::1" };
    private static readonly string[] BlockedPrefixes = ["127.0.0.", "10.", "192.168.", "172.16.", "172.17.", "172.18.", "172.19.", "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.", "172.26.", "172.27.", "172.28.", "172.29.", "172.30.", "172.31."];

    private static ErrorOr<string> ValidateSsrf(string host) =>
        BlockedHosts.Contains(host) || BlockedPrefixes.Any(p => host.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            ? Errors.Reader.SSRFBlocked(host)
            : host;

    private static ErrorOr<string> RouteToPdfUrl(string host, Uri url) =>
        SimpleRoutes
            .Where(kv => host.EndsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value(url))
            .FirstOrDefault() is { } matched
                ? matched
                : BrowserRequired.Any(b => host.EndsWith(b, StringComparison.OrdinalIgnoreCase))
                    ? url.ToString()
                    : Errors.Reader.NoMatchingRoute(url.ToString());
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use `.Contains()` for SSRF check (only `.EndsWith()` and `.StartsWith()`)

**Acceptance criteria:**
- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```
Expected: Clean build

**Commit:** `feat(reader): add SiteResolver with SSRF protection`

---

### Task 17: Create OpenAccessResolver.cs

**What to do:**
Create `src/Services/Reader/OpenAccessResolver.cs` with 9 provider methods (OpenAlex, CORE, GetFTR, Semantic Scholar, CrossRef, arXiv, PMC, OpenAIRE, EuropePMC). Each method has rate limiting via SemaphoreSlim.

**Key points:**
- Uses inline foreach loop (not FallbackChain<T> — that's private to PdfFetcher)
- 10 req/s throttle via SemaphoreSlim(10, 10)
- Each provider returns `ErrorOr<string?>` (URL or null)

**References:**
- Master plan Section 6d for full implementation

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Use FallbackChain<T> (not accessible here)

**Acceptance criteria:**
- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```
Expected: Clean build

**Commit:** `feat(reader): add OpenAccessResolver with 9 providers`

---

### Task 18: Create PdfFetcher.cs

**What to do:**
Create `src/Services/Reader/PdfFetcher.cs` with 4-tier fallback chain:
1. HttpClient (free, always tried first)
2. Scraping API (paid, requires API key)
3. HttpCloak (HTTP/3+QUIC)
4. Browser (Patchright + GhostNoise + CaptchaSolver + AnubisPowSolver)

**Key points:**
- FallbackChain<T> is a PRIVATE NESTED CLASS inside PdfFetcher (~15 LOC)
- ResiliencePipelineFactory methods are PRIVATE STATIC METHODS inside PdfFetcher (~50 LOC)
- Size + %PDF guard after EVERY tier
- MaxFileSizeMb = 100

**References:**
- Master plan Section 6c for full implementation

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Separate FallbackChain<T> into its own file (only consumer)

**Acceptance criteria:**
- `dotnet build src/Services/Reader/Reader.csproj` succeeds

**QA:**
```bash
dotnet build src/Services/Reader/Reader.csproj
```
Expected: Clean build

**Commit:** `feat(reader): add PdfFetcher with 4-tier fallback`

---

### Task 19: Create BlobUploader.cs

**What to do:**
Create `src/Services/Reader/BlobUploader.cs`:

```csharp
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Core;

namespace Services.Reader;

public sealed class BlobUploader
{
    private readonly BlobContainerClient _container;

    public BlobUploader(BlobContainerClient container) => _container = container;

    public async Task<ErrorOr<string>> UploadAsync(byte[] content, CancellationToken ct)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(content));
            var blobName = $"{hash}.pdf";
            var blobClient = _container.GetBlobClient(blobName);
            await blobClient.UploadAsync(new BinaryData(content), ct);
            return blobClient.Uri.ToString();
        }
        catch (RequestFailedException ex)
        {
            return Errors.Reader.BlobUploadFailed(ex.Message);
        }
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

**Commit:** `feat(reader): add BlobUploader with SHA-256 dedup`

---

### Task 20: Create ReaderService.cs

**What to do:**
Create `src/Services/Reader/ReaderService.cs` — the orchestrator (~20 LOC method):

```csharp
using ErrorOr;

namespace Services.Reader;

public sealed class ReaderService
{
    private readonly BlobUploader _blobUploader;
    private readonly OpenAccessResolver _oalResolver;
    private readonly SiteResolver _siteResolver;
    private readonly PdfFetcher _pdfFetcher;
    private readonly ReaderState _state;

    public ReaderService(
        BlobUploader blobUploader,
        OpenAccessResolver oalResolver,
        SiteResolver siteResolver,
        PdfFetcher pdfFetcher,
        ReaderState state)
    {
        _blobUploader = blobUploader;
        _oalResolver = oalResolver;
        _siteResolver = siteResolver;
        _pdfFetcher = pdfFetcher;
        _state = state;
    }

    public async Task<ErrorOr<DownloadResult>> DownloadAsync(DownloadConfig config, CancellationToken ct)
    {
        return await ResolveDoiIfNeeded(config, ct)
            .ThenAsync(cfg => _oalResolver.TryResolveAsync(cfg, ct))
            .ElseAsync(async _ => await FallbackToFetch(config, ct))
            .ThenAsync(bytes => _blobUploader.UploadAsync(bytes, ct))
            .ThenAsync(async blobUrl => await RecordResult(blobUrl, config))
            .Match(
                blobUrl => DownloadResult.Success(config.Uri, blobUrl, 0),
                errors => DownloadResult.Failure(config.Uri, errors.First().Description));
    }

    private async Task<ErrorOr<DownloadConfig>> ResolveDoiIfNeeded(DownloadConfig config, CancellationToken ct)
    {
        if (config.Doi is not null) return config;
        if (config.DoiUrl is null) return config;
        var resolved = await _siteResolver.ExtractDoiAsync(config.DoiUrl);
        return resolved.IsSuccess
            ? config with { Doi = resolved.Value }
            : config;
    }

    private async Task<ErrorOr<byte[]>> FallbackToFetch(DownloadConfig config, CancellationToken ct)
    {
        return await _siteResolver.ResolveAsync(config.Uri)
            .ThenAsync(url => _pdfFetcher.FetchAsync(new Uri(url), ct));
    }

    private async Task<ErrorOr<string>> RecordResult(string blobUrl, DownloadConfig config)
    {
        await _state.RecordAsync(DownloadResult.Success(config.Uri, blobUrl, 0));
        return blobUrl;
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

**Commit:** `feat(reader): add ReaderService orchestrator`

---

## Verify Phase 3

```bash
dotnet build src/Services/Reader/Reader.csproj
```

Clean build. All services in place.

**Dependencies:** Phase 2
**Blocks:** Phase 4
