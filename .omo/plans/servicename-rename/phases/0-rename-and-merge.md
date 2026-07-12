# Phase 0: Rename and Merge

## Task
Rename ServiceName.Google to ServiceName.YouTube. Merge two extension blocks into one.

## Steps
1. Read src/Core/ServiceName.cs
2. Rename Google enum value to YouTube
3. Merge the Google extension block and YouTube extension block into a single ServiceName.YouTube extension
4. Find all references: `Select-String -Path "src/**/*.cs" -Pattern "ServiceName.Google"`
5. Replace each with ServiceName.YouTube
6. Build and verify

## Verify
```powershell
Select-String -Path "src/**/*.cs" -Pattern "ServiceName.Google"
dotnet build
dotnet run --project src/App -- sync yt
```
Expected: 0 matches for ServiceName.Google. Build clean. Sync runs without error.

## Commit
`refactor: rename ServiceName.Google to ServiceName.YouTube, merge extensions`
