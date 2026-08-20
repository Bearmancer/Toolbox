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
