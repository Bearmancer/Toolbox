# Phase 0: Move Output Path

## Task
Change dashboard output from repo root to state/dashboard/.

## Steps
1. Read src/CLI/Dashboard/DashboardGenerateCommand.cs
2. Find the output path logic (likely uses Directory.GetCurrentDirectory() or PathResolver.RepoRoot)
3. Change output directory to Path.Combine(PathResolver.RepoRoot, "state", "dashboard")
4. Ensure directory is created if it doesn't exist
5. Update DashboardHtmlGenerator.cs script src if it references "dashboard-data.js" — should still work since both files are in the same directory
6. Build and verify

## Verify
```powershell
dotnet build
dotnet run --project src/App -- dashboard generate
Test-Path state/dashboard/dashboard.html
Test-Path state/dashboard/dashboard-data.js
Test-Path dashboard.html
```
Expected: state/dashboard/dashboard.html exists. state/dashboard/dashboard-data.js exists. Root dashboard.html does NOT exist.

## Commit
`refactor(dashboard): move output to state/dashboard/`
