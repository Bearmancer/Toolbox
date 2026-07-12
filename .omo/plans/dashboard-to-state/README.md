# Plan: Dashboard to State

**Task:** T-7a5a0b00-dcf1-4131-884b-3187f0bfe203

## Goal
Move dashboard output from repo root to state/dashboard/.

## Phases
- **0-move-output-path.md** — Change DashboardGenerateCommand output to state/dashboard/

## Verify
```powershell
dotnet run --project src/App -- dashboard generate
Test-Path state/dashboard/dashboard.html
Test-Path dashboard.html
```
