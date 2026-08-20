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
