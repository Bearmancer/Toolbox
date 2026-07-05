# Phase 0: Package Pruning (Dead Weight Removal)

## Task 1: Remove dead packages from Directory.Packages.props

Remove these 7 lines from the `<ItemGroup>` in `Directory.Packages.props`:

```xml
<PackageVersion Include="Hqub.Last.fm" Version="2.0.0" />
<PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.9" />
<PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.3" />
<PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.9" />
<PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.9" />
<PackageVersion Include="Serilog.Enrichers.Environment" Version="3.0.1" />
<PackageVersion Include="Serilog.Enrichers.Thread" Version="4.0.0" />
```

**Must NOT:**
- Touch any other package line
- Add any new packages

**QA:**
```bash
dotnet restore
dotnet build
```
Expected: Clean build. 34 → 27 packages.

**Commit:** `chore: remove 7 unused NuGet packages`
