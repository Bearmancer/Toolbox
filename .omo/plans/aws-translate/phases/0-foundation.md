# Phase 0: Foundation — Package + Project

## Tasks

### Task 1: Add AWSSDK.Translate package to Directory.Packages.props

**What to do:**
Add `<PackageVersion Include="AWSSDK.Translate" Version="4.0.2.11" />` to the `<ItemGroup>` in `Directory.Packages.props`.

**Must NOT:**
- Add any other packages

**References:**
- `Directory.Packages.props:6-38`

**Acceptance criteria:**
- `dotnet restore` succeeds

**QA:**
```bash
dotnet restore
```
Expected: No errors

**Commit:** `chore(packages): add AWSSDK.Translate V4`

---

### Task 2: Create Amazon.csproj

**What to do:**
Create `src/Services/Amazon/Amazon.csproj` mirroring `src/Services/Azure/Azure.csproj`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="AWSSDK.Translate" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

Set `<RootNamespace>Services.Amazon</RootNamespace>`.

**Must NOT:**
- Reference Azure.csproj
- Use block-scoped namespaces

**References:**
- `src/Services/Azure/Azure.csproj:1-18`
- `src/Services/Google/Google.csproj:1-13`

**Acceptance criteria:**
- `dotnet build src/Services/Amazon/Amazon.csproj` succeeds (even with empty .cs files)

**QA:**
```bash
dotnet build src/Services/Amazon/Amazon.csproj
```
Expected: Clean build

**Commit:** `feat(amazon): add Services.Amazon project skeleton`

---

## Verify Phase 0

```bash
dotnet restore
dotnet build src/Services/Amazon/Amazon.csproj
```

Both succeed. Foundation in place.

**Dependencies:** None
**Blocks:** Phase 1
