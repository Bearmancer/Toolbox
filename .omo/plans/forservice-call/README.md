# Plan: ForService Call

**Task:** T-893963ec-1a21-4f13-a7c4-b0f0dccf173f

## Goal
Add Telemetry.ForService(ServiceName.YouTube) call to SyncYoutubeCommand.

## Phases
- **0-add-forservice.md** — Add using IDisposable scope to SyncYoutubeCommand.ExecuteAsync

## Verify
```powershell
Select-String -Path "src/CLI/Sync/YouTube/SyncYoutubeCommand.cs" -Pattern "ForService"
dotnet build
dotnet run --project src/App -- sync yt
```
