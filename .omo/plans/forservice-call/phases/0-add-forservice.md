# Phase 0: Add ForService Call

## Task
Add `using IDisposable _ = Telemetry.ForService(ServiceName.YouTube)` to SyncYoutubeCommand.ExecuteAsync.

## Steps
1. Read src/CLI/Sync/YouTube/SyncYoutubeCommand.cs
2. Add `using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);` at the top of ExecuteAsync method
3. Add `using Core;` import if not present
4. Build and verify

## Verify
```powershell
Select-String -Path "src/CLI/Sync/YouTube/SyncYoutubeCommand.cs" -Pattern "ForService"
dotnet build
dotnet run --project src/App -- sync yt
```
Expected: At least 1 match for ForService. Build clean. Sync runs and logs are YouTube-scoped.

## Commit
`feat(sync): add ForService scope to SyncYoutubeCommand`
