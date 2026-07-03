# Phase 0: Data Layer — Load All Videos

## Task 1: Update DashboardGenerateCommand.cs to load all videos for View 3

**What to do:**
Modify `LoadVideosAsync` to return all videos for View 3. Ensure the videos list is passed to `DashboardHtmlGenerator.Generate()`.

**Must NOT:**
- Change the data structure
- Add new models

**References:**
- `src/CLI/Dashboard/DashboardGenerateCommand.cs:155-202` (LoadVideosAsync method)
- `src/CLI/Dashboard/DashboardGenerateCommand.cs:37-39` (playlists loading)
- `src/CLI/Dashboard/DashboardGenerateCommand.cs:41-43` (videos loading)
- `src/CLI/Dashboard/DashboardGenerateCommand.cs:57-58` (Generate call)

**Acceptance criteria:**
- `dotnet build` succeeds
- `dotnet run --project src/App -- dashboard generate` produces HTML > 1MB

**QA:**
```bash
dotnet build
dotnet run --project src/App -- dashboard generate
```
Expected: Exit 0, HTML file created

**Commit:** `feat(dashboard): load all videos for View 3`

---

## Verify Phase 0

```bash
dotnet build
dotnet run --project src/App -- dashboard generate
```

Build succeeds. HTML generated.

**Dependencies:** None
**Blocks:** Phase 1
