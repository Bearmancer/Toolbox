# Phase 0: Foundation — Packages + Core

## Tasks

### Task 1: Add Microsoft.Playwright to Directory.Packages.props

Add inside `<ItemGroup>` after `SSH.NET`:

```xml
<PackageVersion Include="Microsoft.Playwright" Version="1.52.0" />
```

**Skipped:** Patchright (anti-bot overkill for streaming site, add when blocked). Picked official Playwright — `msedge` channel native, single dep.

**Acceptance:** `dotnet restore` succeeds.

**QA:**

```bash
dotnet restore
```

**Commit:** `chore(packages): add Playwright for Pristine`

---

### Task 2: Add Pristine to solution

Add in `Toolbox.slnx` after LastFm:

```xml
<Project Path="src/Services/Pristine/Pristine.csproj" />
```

**QA:** `dotnet sln list` shows Pristine.csproj

**Commit:** `chore(solution): add Pristine project`

---

### Task 3: Add Pristine to ServiceName enum

In `src/Core/ServiceName.cs`:

```csharp
Pristine
```

**QA:** `dotnet build` clean (Telemetry iterates Enum.GetValues, throws if missing slug).

**Commit:** `feat(core): add Pristine to ServiceName`

---

### Task 4: Add Pristine slug to ServiceNameExtensions

In `src/Core/ServiceNameExtensions.cs`:

```csharp
ServiceName.Pristine => "pristine",
```

**QA:** `dotnet build`

**Commit:** `feat(core): add Pristine slug`

---

### Task 5: Add Errors.Pristine

In `src/Core/Errors.cs` after TextAnalytics class:

```csharp
public static class Pristine
{
    public static Error MissingBaseOutDir => Error.Validation(code: "Pristine.MissingBaseOutDir", description: "PRISTINE_BASE_OUT_DIR not set");
    public static Error AuthMissing => Error.NotFound(code: "Pristine.AuthMissing", description: "No session at state/pristine/auth.json — run pristine login first");
    public static Error BrowserFailed(string reason) => Error.Failure(code: "Pristine.BrowserFailed", description: $"Browser failed: {reason}");
    public static Error LoginTimeout => Error.Failure(code: "Pristine.LoginTimeout", description: "Login not completed within timeout");
    public static Error ResolveFailed(string code) => Error.NotFound(code: "Pristine.ResolveFailed", description: $"Could not resolve {code}");
    public static Error DownloadFailed(string file, string reason) => Error.Failure(code: "Pristine.DownloadFailed", description: $"Download failed {file}: {reason}");
    public static Error AlbumNotFound(string code) => Error.NotFound(code: "Pristine.AlbumNotFound", description: $"Album not found: {code}");
}
```

**QA:** `dotnet build`

**Commit:** `feat(core): add Errors.Pristine`

## Verify Phase 0

```bash
dotnet restore
dotnet build
```

**Dependencies:** None
**Blocks:** Phase 1
# Phase 1: Types + Pure Functions (no browser)

## Tasks

### Task 6: Create Pristine.csproj

`src/Services/Pristine/Pristine.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Playwright" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

**Skipped:** separate Shared package, FallbackChain abstraction — single service only.

**QA:** `dotnet build src/Services/Pristine/Pristine.csproj`

**Commit:** `feat(pristine): add Services.Pristine skeleton`

---

### Task 7: Create PristineCredentials.cs

`src/Services/Pristine/PristineCredentials.cs`:

```csharp
namespace Services.Pristine;

public sealed class PristineCredentials
{
    public required string BaseOutDir { get; init; }

    public static PristineCredentials Read() => new()
    {
        BaseOutDir = Environment.GetEnvironmentVariable("PRISTINE_BASE_OUT_DIR")
            ?? throw new InvalidOperationException("Missing: PRISTINE_BASE_OUT_DIR"),
    };
}
```

Mirrors `AzureCredentials.Read()` throwing on missing. Caught in `Program.cs` startup guard (exit 2).

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineCredentials`

---

### Task 8: Create PristineText.cs (Sanitize + Normalize + Roman)

`src/Services/Pristine/PristineText.cs`:

```csharp
namespace Services.Pristine;

public static class PristineText
{
    private static readonly Regex WinIllegalChars = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.Compiled);
    private static readonly Regex TrailingDotsSpaces = new(@"[\s.]+$", RegexOptions.Compiled);
    private static readonly Regex AudioUrlRe = new(@"\.(flac|mp3|wav|aac|ogg)(\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TimestampPrefixRe = new(@"^\s*\d{1,2}:\d{2}(?::\d{2})?\s*[-\u2013\u2014:.)]*\s*", RegexOptions.Compiled);
    private static readonly Regex MovementPrefixRe = new(@"^\s*(?:(?<ord>\d{1,2})(?:st|nd|rd|th)?\s*(?:m(?:ovement|ov\.?|vt|vmt)\.?)?|(?<roman>[ivxlcdm]{1,6})\s*(?:m(?:ovement|ov\.?|vt|vmt)\.?)?|(?<word>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\s+movement)\s*[-\u2013\u2014:.)]*\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizePathComponent(string name)
    {
        name = WinIllegalChars.Replace(name, "-");
        name = TrailingDotsSpaces.Replace(name, "");
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name.Trim();
    }

    public static string NormalizeTrackTitle(string title)
    {
        // port pristine.py:146 — timestamp strip + roman canonical
    }

    public static bool IsAudioUrl(string url) => AudioUrlRe.IsMatch(url);
}
```

Port `pristine.py:183-196` and `146-174` byte-identical. `CultureInfo.InvariantCulture.TextInfo.ToTitleCase` for `.title()` parity — verify against known titles.

**Skipped:** per-method files — one file for three pure funcs, YAGNI split.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineText pure helpers`

---

### Task 9: Create PristineDownloadConfig.cs + PristineResult.cs

```csharp
namespace Services.Pristine;

public sealed record PristineDownloadConfig
{
    public required string Code { get; init; } // PASC552
    public string OutDir { get; init; } = "";
}

public sealed record PristineAlbumResult
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required string OutPath { get; init; }
    public int Expected { get; init; }
    public int Downloaded { get; init; }
}
```

**QA:** `dotnet build`

**Commit:** `feat(pristine): add Pristine config/result records`

---

### Task 10: Live-check selectors via Firefox DevTools MCP (before browser code)

Run live validation on `https://pristinestreaming.com/app/browse` (requires valid auth cookie or login flow):

```bash
firefox-devtools_take_snapshot # find .pp-navbar__search__input, .pp-tracklist__item__playnow, .pp-album-view__title, .pp-playbar__now-playing__track UIDs
firefox-devtools_evaluate_script "() => [...document.querySelectorAll('[class]')].flatMap(el=>[...el.classList]).filter(c=>c.startsWith('pp-')).sort()"
firefox-devtools_list_network_requests urlContains=.flac
```

Confirm selectors from `pristine.py:396-529` still present. If drifted, patch `PristineBrowser.cs` before coding downstream.

**QA:** snapshot returns UIDs for all `pp-` classes; network list shows `.flac` URLs on playback.

**Commit:** (no commit — verification step, note in plan journal)

## Verify Phase 1

```bash
dotnet build src/Services/Pristine/Pristine.csproj
```

**Dependencies:** Phase 0
**Blocks:** Phase 2
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
# Phase 3: Services (resolve → playback → poll loop)

## Tasks

### Task 15: Create PristineAlbumService.cs — ResolveAlbumId + StartPlayback + ParseTracklist + Artwork/Pdf

Single file with private helpers — mirrors `pristine.py:393-657`:

- `ResolveAlbumIdAsync(IPage page, string code)` — 3 attempts, `.pp-navbar__search__input`, fill+Enter, `WaitForLoadStateAsync(NetworkIdle,5000)`, URL contains `code` or `code[4:]`, click `[href*='/albums/']` → `.pp-browse-grid__item` → `.pp-search-results__item`, parse `int(url.Split('/').Last())`, verify `.pp-album-view__title` case-insensitive contains code. Same swallow+retry via `GotoAsync(PRISTINE_APP)`.

- `StartPlaybackAsync(IPage page)` — seekbar toggle if `value!='1'`, `WaitForSelectorAsync(".pp-tracklist__item",15000)`, `HoverAsync`+`ClickAsync(".pp-tracklist__item__playnow")` fallback JS `MouseEvent('click')`/`dblclick`, `WaitForFunctionAsync("!!document.querySelector('body > audio[src]')",5000)`.

- `ParseTracklistAsync(IPage)` → `EvaluateAsync<string[]>("Array.from(...pp-tracklist__item__title...).map(el=>el.textContent.trim())")`

- `DownloadArtworkAndPdfAsync(IPage, string albumOut, string albumTitle, HttpClient)` — `.pp-album-view__artwork > img` src → `{albumTitle}{ext}` + `S3_COVERS+{nameNoExt}.pdf` → `{nameNoExt}.pdf` via `PristineDownloader`. Missing artwork → early return; PDF fail → log non-fatal.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineAlbumService resolve/playback/artwork`

---

### Task 16: Create PristinePollService.cs — Poll loop state machine

Port `pristine.py:781-1003` core loop into `DownloadSingleAlbumAsync(IBrowserContext ctx, string code, string outDir, HttpClient http, CancellationToken ct)`:

State: `seenUrls HashSet<string>`, `seenTitles HashSet<string>`, `stallCount int`, `trackNum int`, `capturedUrls List<string>` via `Page.Request` event filtered by `PristineText.IsAudioUrl`.

Loop `while (stallCount < 60)`:

- newSrc = first `capturedUrls` not in `seenUrls` else `EvaluateAsync<string?>("...!el.paused&&el.hasAttribute('src')")`
- if found: `seenUrls.Add`, `trackNum++`, title = `EvaluateAsync("...pp-playbar__now-playing__track...")` ?? `f"Track {trackNum:02d}"`, duplicate-title break, `NormalizeTrackTitle().TitleCase` → `SanitizePathComponent` → `f"{trackNum:02d}. {safe}{ext}"` where `ext=".flac" if ".flac" in src else ".mp3"`, `EvaluateAsync("pause all")` then `PristineDownloader.DownloadAsync`, expected-count break, `Task.Delay(2000)` → `ClickForwardAsync` → `WaitForFunctionAsync("...readyState>=2",4000)` swallow → `ClickPlayAsync` → `WaitForFunctionAsync("...!paused",3000)` swallow.
- else: `stallCount++`, if `HasReadyPausedAudio` → `ClickPlay`, else if `stallCount==5` → JS re-dispatch playnow.

Tail: verification (missingOnDisk quirk — uses final `trackNum` prefix, keep bug-for-bug then fix in follow-up), `Task.Delay(10000)`, finally `Page.CloseAsync()`.

**ponytail: stall 60*1s global, sequential poll — per-track timeout if streaming stalls longer**

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristinePollService`

---

### Task 17: Create PristineOrchestrator.cs

Port `pristine.py:1006-1072`:

- mkdir `dest`, `CreateAsync` browser, seed page `GotoAsync(PRISTINE_APP)` + `_wait_for_login` (180s), require `AuthPath` exists else `Errors.Pristine.AuthMissing`, loop `RELEASES` or passed codes with `Task.Delay(3000)` inter-album, per-album try/catch, `context.CloseAsync` finally, log `ALL DOWNLOADS COMPLETE`.

Use `Telemetry.ForService(ServiceName.Pristine)` scope + `ErrorOr` return.

Keep constants: `S3_COVERS`, `PRISTINE_APP`, `POLL_SLEEP=1.0`, `POST_DL_WAIT=2.0`, `MAX_STALL=60`, `TOSCANINI_BEETHOVEN/GENERAL/STOKOWSKI/RELEASES` as `static readonly string[]` — lazy: inline at top of orchestrator, not separate constants file.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineOrchestrator`

## Verify Phase 3

```bash
dotnet build src/Services/Pristine/Pristine.csproj
```

**Dependencies:** Phase 2
**Blocks:** Phase 4
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

---

## Execution Status

Verified 2026-08-19 by Atlas (orchestrator) via 6 parallel subagents + personal build.

- [x] Task 1: Microsoft.Playwright in Directory.Packages.props
- [x] Task 2: Pristine in Toolbox.slnx
- [x] Task 3: Pristine in ServiceName enum
- [x] Task 4: Pristine slug in ServiceName (ToFileSlug)
- [x] Task 5: Errors.Pristine class
- [x] Task 6: Pristine.csproj skeleton
- [x] Task 7: PristineCredentials.cs
- [x] Task 8: PristineText.cs (Sanitize/Normalize/Roman)
- [x] Task 9: PristineDownloadConfig + PristineAlbumResult (PristineModels.cs)
- [x] Task 10: Live-check selectors (Firefox DevTools MCP) — **N/A**: no auth session + Firefox DevTools MCP unavailable in env (see Chronic Firefox MCP Failures docs). Selectors hardcoded from pristine.py port; deferred to live run with credentials.
- [x] Task 11: PristinePaths.cs
- [x] Task 12: PristineBrowser.cs
- [x] Task 13: PristineDownloader.cs
- [x] Task 14: PristineLoginService.cs
- [x] Task 15: PristineAlbumService.cs
- [x] Task 16: PristinePollService.cs
- [x] Task 17: PristineOrchestrator.cs
- [x] Task 18: PristineSetup.cs DI
- [x] Task 19: PristineCommandModule.cs
- [x] Task 20: PristineLoginCommand.cs
- [x] Task 21: PristineDownloadCommand.cs
- [x] Task 22: Wire Pristine into CLI + App

**Final verification:** `dotnet build` → 0 errors, 0 warnings, all 8 projects compile. `dotnet run --project src/App -- pristine --help` / `login --help` / `download --help` all exit 0 with correct command surface.

**Benign deviations from plan (verified correct, not defects):**
- T2: slnx uses backslash path separators (repo convention).
- T4: slug mapping in `ServiceName.cs` `ToFileSlug` extension, not `ServiceNameExtensions.cs`.
- T5: error descriptions reworded; AuthMissing path `state/auth/pristine/auth.json`.
- T7: `PristineCredentials.Read()` defaults to Desktop/Pristine instead of throwing on missing `PRISTINE_BASE_OUT_DIR`.
- T9: config + result merged into `PristineModels.cs`; `OutDir` nullable.
- T11: AuthPath `state/auth/pristine/auth.json` (auth grouped under `state/auth`).
- T18: extra `PristineAudioVerifier` singleton registered (required by PollService).
- T21: `[codes]` positional arg + `NormalizeCodes` instead of `-c|--code` option (superior multi-code UX).
- Extra file `PristineAudioVerifier.cs`: ffprobe-based 16-bit FLAC verification (enhancement beyond plan).
