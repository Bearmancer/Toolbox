# Phase 0: Foundation — Packages + Project + Core Wiring

## Tasks

### Task 1: Add FFMpegCore + ATL.NET to Directory.Packages.props

**What to do:**
Add 2 new package versions inside the existing `<ItemGroup>` in `Directory.Packages.props`, after the `Hqub.Last.fm` entry:

```xml
<PackageVersion Include="FFMpegCore" Version="5.4.0" />
<PackageVersion Include="z440.atl.core" Version="7.15.3" />
```

**Must NOT:**
- Add any other packages
- Modify existing package versions

**References:**
- `Directory.Packages.props:6-38`
- FFMpegCore: https://github.com/rosenbjerg/FFMpegCore (MIT, 2.08k stars)
- ATL.NET: https://github.com/Zeugma440/atldotnet (MIT, v7.15.3 May 2026)

**Acceptance criteria:**
- `dotnet restore` succeeds

**QA:**
```bash
dotnet restore
```
Expected: No errors

**Commit:** `chore(packages): add FFMpegCore + ATL.NET for audio pipeline`

---

### Task 2: Create Audio.csproj

**What to do:**
Create `src/Services/Audio/Audio.csproj` mirroring `src/Services/LastFm/LastFm.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<RootNamespace>Services.Audio</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="FFMpegCore" />
		<PackageReference Include="z440.atl.core" />
		<PackageReference Include="ErrorOr" />
		<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\Core\Core.csproj" />
	</ItemGroup>
</Project>
```

**Must NOT:**
- Reference Azure.csproj or Google.csproj
- Use block-scoped namespaces
- Add `<TargetFramework>` (inherited from Directory.Build.props)
- Add `<Nullable>` or `<ImplicitUsings>` (inherited)

**References:**
- `src/Services/LastFm/LastFm.csproj:1-13`
- `src/Services/Azure/Azure.csproj:1-19`

**Acceptance criteria:**
- `dotnet build src/Services/Audio/Audio.csproj` succeeds (even with no .cs files yet)

**QA:**
```bash
dotnet build src/Services/Audio/Audio.csproj
```
Expected: Clean build

**Commit:** `feat(audio): add Services.Audio project skeleton`

---

### Task 3: Add Audio to Toolbox.slnx

**What to do:**
Add 1 new project entry inside the existing `<Solution>` in `Toolbox.slnx`:

```xml
<Project Path="src/Services/Audio/Audio.csproj" />
```

Insert after `LastFm.csproj` line (line 7), before closing `</Solution>`.

**References:**
- `Toolbox.slnx:1-8`

**Acceptance criteria:**
- Solution file is valid

**QA:**
```bash
dotnet sln list
```
Expected: Audio.csproj appears in list

**Commit:** `chore(solution): add Audio project`

---

### Task 4: Add Audio to ServiceName enum + ToFileSlug

**What to do:**
Add `Audio` to the enum in `src/Core/ServiceName.cs`:

```csharp
public enum ServiceName
{
    LastFm,
    YouTube,
    OpenAi,
    Vision,
    Translate,
    TextAnalytics,
    Speech,
    DocIntel,
    Audio
}
```

Add switch arm in `ToFileSlug()`:

```csharp
ServiceName.Audio => "audio",
```

**References:**
- `src/Core/ServiceName.cs:1-33`
- `src/Core/Telemetry.cs:25-26` (auto-generates `logs/audio.jsonl` from enum)

**Acceptance criteria:**
- `dotnet build` succeeds
- Switch remains exhaustive (no `_` arm)

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(core): add Audio to ServiceName enum`

---

### Task 5: Add Errors.Audio class

**What to do:**
Add `Errors.Audio` nested class in `src/Core/Errors.cs`, after the `TextAnalytics` class:

```csharp
public static class Audio
{
    public static Error BinaryNotFound(string name) =>
        Error.Failure(
            "Audio.BinaryNotFound",
            $"{name} binary not found. Set {name.ToUpper()}_PATH in .env."
        );

    public static Error ExtractionFailed(string iso, string reason) =>
        Error.Failure("Audio.ExtractionFailed", $"SACD extraction failed for {iso}: {reason}");

    public static Error NoDffFound(string directory) =>
        Error.NotFound("Audio.NoDff", $"No .dff file found in {directory}");

    public static Error NoCueFound(string directory) =>
        Error.NotFound("Audio.NoCue", $"No .cue file found in {directory}");

    public static Error GainDetectionFailed(string file) =>
        Error.Failure("Audio.GainFailed", $"Could not detect peak levels in {file}");

    public static Error ConversionFailed(string file, string reason) =>
        Error.Failure("Audio.ConvertFailed", $"Conversion failed for {file}: {reason}");

    public static Error NoIsoFound(string directory) =>
        Error.NotFound("Audio.NoIso", $"No .iso files found in {directory}");

    public static Error InvalidCueFormat(string file, string reason) =>
        Error.Validation("Audio.InvalidCue", $"Malformed CUE file {file}: {reason}");
}
```

**References:**
- `src/Core/Errors.cs:1-99`
- Existing pattern: `Errors.YouTube`, `Errors.Azure`, `Errors.LastFm`

**Acceptance criteria:**
- `dotnet build` succeeds

**QA:**
```bash
dotnet build
```
Expected: Clean build

**Commit:** `feat(core): add Errors.Audio taxonomy`

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
