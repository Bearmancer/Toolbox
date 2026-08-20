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

