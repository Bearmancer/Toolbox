# Plan: ServiceName Rename

**Task:** T-9028552d-90b3-46b9-8fc9-8b8c6f60ff1a

## Goal
Rename ServiceName.Google to ServiceName.YouTube. Merge two extension blocks into one.

## Phases
- **0-rename-and-merge.md** — Rename enum value, merge extensions, update all references

## Verify
```powershell
Select-String -Path "src/**/*.cs" -Pattern "ServiceName.Google"
dotnet build
dotnet run --project src/App -- sync yt
```
