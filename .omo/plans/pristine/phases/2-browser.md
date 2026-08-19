# Phase 2: Browser + I/O

## Tasks

### Task 11: Create PristinePaths.cs

`src/Services/Pristine/PristinePaths.cs`:

```csharp
namespace Services.Pristine;

public static class PristinePaths
{
    public static string UserDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pristine-playwright-profile");
    public static string AuthPath => Path.Combine(PathResolver.RepoRoot, "state", "pristine", "auth.json");
    public static string BaseOutDir => Environment.GetEnvironmentVariable("PRISTINE_BASE_OUT_DIR") ?? throw new InvalidOperationException("Missing: PRISTINE_BASE_OUT_DIR");
}
```

Mirrors `pristine.py:177-180`. Reuse `PathResolver.RepoRoot` (existing in Core).

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristinePaths`

---

### Task 12: Create PristineBrowser.cs

`src/Services/Pristine/PristineBrowser.cs`:

Launch persistent msedge + inject `auth.json` cookies/localStorage exactly as `pristine.py:302-347`:

```csharp
using Microsoft.Playwright;

public sealed class PristineBrowser
{
    public async Task<IBrowserContext> CreateAsync(bool headless)
    {
        var pw = await Playwright.CreateAsync();
        var ctx = await pw.Chromium.LaunchPersistentContextAsync(PristinePaths.UserDataDir, new BrowserTypeLaunchPersistentContextOptions
        {
            Channel = "msedge",
            Headless = headless,
            AcceptDownloads = true,
            Args = ["--autoplay-policy=no-user-gesture-required"],
        });
        if (File.Exists(PristinePaths.AuthPath))
        {
            var json = await File.ReadAllTextAsync(PristinePaths.AuthPath);
            // storage_state shape {cookies, origins:[{origin,localStorage}]}
            // AddCookiesAsync + AddInitScriptAsync with origin guard
        }
        return ctx;
    }
}
```

**ponytail: persistent context only, ephemeral IPage per album — parallel albums would need per-page contexts if throughput matters**

**QA:** `dotnet build`; manual `playwright install msedge` once.

**Commit:** `feat(pristine): add PristineBrowser`

---

### Task 13: Create PristineDownloader.cs (HttpClient .part + atomic move)

Port `pristine.py:203-283`:

```csharp
public sealed class PristineDownloader
{
    private const int MaxAttempts = 3;
    private const int RetryBaseS = 2;

    public async Task<bool> DownloadAsync(string url, string dest, HttpClient http, CancellationToken ct)
    {
        // ponytail: substring check ".flac" in url decides ext elsewhere — keep literal
        var part = dest + ".part";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try {
                using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!r.IsSuccessStatusCode) return false;
                await using var fs = File.Create(part);
                await (await r.Content.ReadAsStreamAsync(ct)).CopyToAsync(fs, ct);
                File.Move(part, dest, overwrite: true);
                return true;
            } catch { if (File.Exists(part)) File.Delete(part); if (attempt < MaxAttempts) await Task.Delay(RetryBaseS * (1 << (attempt-1)) * 1000, ct); }
        }
        return false;
    }
}
```

Include `AUTO_OVERWRITE` guard (dead when true but kept). Chunk implicit via `CopyToAsync`.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineDownloader`

---

### Task 14: Create PristineLoginService.cs

Port `pristine.py:1075-1124`:

- `LaunchPersistentContextAsync(headless:false, acceptDownloads:false)`
- goto `https://pristinestreaming.com/app/browse`, check already-in: `"login" not in url && "browse" in url && !Browsing as guest`
- else goto `https://pristineclassical.com/pages/player-subscribe`, `WaitForURLAsync("**pristinestreaming.com/app/browse**", 300000)`
- `Context.StorageStateAsync(path: PristinePaths.AuthPath)` after `Directory.CreateDirectory`

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineLoginService`

## Verify Phase 2

```bash
dotnet build src/Services/Pristine/Pristine.csproj
```

**Dependencies:** Phase 1
**Blocks:** Phase 3
