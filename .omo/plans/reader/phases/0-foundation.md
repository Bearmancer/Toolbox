# Phase 0: Foundation — Packages + Core Modifications

## Tasks

### Task 1: Add packages to Directory.Packages.props

**What to do:**
Add 4 new package versions inside the existing `<ItemGroup>`:

```xml
<PackageVersion Include="Azure.Storage.Blobs" Version="12.21.0" />
<PackageVersion Include="Moongazing.Veil" Version="1.0.0" />
<PackageVersion Include="Patchright" Version="1.58.0" />
<PackageVersion Include="Polly" Version="8.5.0" />
```

Insert after existing `Hqub.Last.fm` entry (line 34), before closing `</ItemGroup>`.

**References:**
- `Directory.Packages.props:6-38`

**Acceptance criteria:**
- `dotnet restore` succeeds

**QA:**
```bash
dotnet restore
```
Expected: No errors

**Commit:** `chore(packages): add Reader dependencies`

---

### Task 2: Add Reader to solution

**What to do:**
Add 1 new project entry inside the existing `<Solution>` in `Toolbox.slnx`:

```xml
<Project Path="src/Services/Reader/Reader.csproj" />
```

Insert after `LastFm.csproj` line (line 7).

**References:**
- `Toolbox.slnx`

**Acceptance criteria:**
- Solution file is valid

**QA:**
```bash
dotnet sln list
```
Expected: Reader.csproj appears in list

**Commit:** `chore(solution): add Reader project`

---

### Task 3: Add Reader to ServiceName enum

**What to do:**
Add `Reader` to the existing enum in `src/Core/ServiceName.cs`:

```csharp
public enum ServiceName
{
    LastFm,
    Google,
    OpenAI,
    Vision,
    Translate,
    TextAnalytics,
    Speech,
    DocIntel,
    Reader
}
```

**References:**
- `src/Core/ServiceName.cs`

**Acceptance criteria:**
- `dotnet build` succeeds

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(core): add Reader to ServiceName enum`

---

### Task 4: Add Reader switch arm to ServiceNameExtensions

**What to do:**
Add switch arm for `Reader` in `src/Core/ServiceNameExtensions.cs`:

```csharp
ServiceName.Reader => "reader",
```

**References:**
- `src/Core/ServiceNameExtensions.cs`

**Acceptance criteria:**
- `dotnet build` succeeds
- Switch remains exhaustive (no `_` arm)

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(core): add Reader switch arm`

---

### Task 5: Add Errors.Reader class

**What to do:**
Add `Errors.Reader` nested class at the end of the `Errors` class in `src/Core/Errors.cs`:

```csharp
public static class Reader
{
    public static Error NoMatchingRoute(string url) =>
        Error.NotFound(code: "RD.NoRoute", description: $"No matching route for URL: {url}");

    public static Error FetchFailed(string url, string reason) =>
        Error.Failure(code: "RD.FetchFailed", description: $"Fetch failed: {url} - {reason}");

    public static Error AllTiersFailed(string url) =>
        Error.Failure(code: "RD.AllTiersFailed", description: $"All tiers failed for {url}");

    public static Error GeoBlocked =>
        Error.Failure(code: "RD.GeoBlocked", description: "Geo-blocked (HTTP 451)");

    public static Error InvalidDoi =>
        Error.Validation(code: "RD.InvalidDoi", description: "Invalid DOI format");

    public static Error BrowserSetupFailed =>
        Error.Failure(code: "RD.BrowserSetup", description: "Browser setup failed");

    public static Error CaptchaSolvingFailed(string reason) =>
        Error.Failure(code: "RD.CaptchaFailed", description: $"CAPTCHA solving failed: {reason}");

    public static Error PowSolvingFailed =>
        Error.Failure(code: "RD.PowFailed", description: "Proof-of-work solving failed");

    public static Error BlobUploadFailed(string reason) =>
        Error.Failure(code: "RD.BlobUpload", description: $"Blob upload failed: {reason}");

    public static Error InvalidPdfResponse =>
        Error.Failure(code: "RD.InvalidPdf", description: "Response is not a valid PDF");

    public static Error FileTooLarge(long size, long max) =>
        Error.Validation(code: "RD.FileTooLarge", description: $"File too large: {size} > {max}");

    public static Error SSRFBlocked(string host) =>
        Error.Validation(code: "RD.SSRF", description: $"SSRF blocked: {host}");

    public static Error ScrapingApiKeyMissing =>
        Error.NotFound(code: "RD.NoApiKey", description: "Scraping API key not configured");

    public static Error OpenAccessUnavailable =>
        Error.NotFound(code: "RD.NoOA", description: "No open access source found");
}
```

Insert inside the `Errors` class, after the `TextAnalytics` class (line 102), before the final closing `}`.

**References:**
- `src/Core/Errors.cs`

**Acceptance criteria:**
- `dotnet build` succeeds

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(core): add Errors.Reader taxonomy`

---

## Verify Phase 0

```bash
dotnet restore
dotnet build
dotnet sln list
```

All succeed. Foundation in place.

**Dependencies:** None
**Blocks:** Phase 1
