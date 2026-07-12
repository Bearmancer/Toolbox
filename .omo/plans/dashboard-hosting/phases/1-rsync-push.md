# Phase 1: Rsync Push from Laptop

## Task
Create rsync script on laptop to push generated dashboard files to OCI after sync.

## Steps
1. Create script: `scripts/push-dashboard.ps1`
   ```powershell
   rsync -avz --delete state/dashboard/ oci:/opt/dashboard/
   ```
2. Tie into SyncYoutubeCommand post-sync (T-b251e669) OR run manually after sync
3. Alternative: Windows Task Scheduler runs sync + rsync on schedule:
   ```powershell
   cd C:\Users\Lance\Desktop\Azure\New
   dotnet run --project src/App -- sync yt
   rsync -avz --delete state/dashboard/ oci:/opt/dashboard/
   ```
4. Run initial push: `rsync -avz state/dashboard/ oci:/opt/dashboard/`

## Pipeline
```
Laptop: sync yt → dashboard auto-generates → rsync push
    ↓ SSH (Tailscale 100.68.154.15)
OCI: /opt/dashboard/ → nginx :80 → http://100.68.154.15
```

## Verify
```powershell
ssh oci 'ls /opt/dashboard/'
curl http://100.68.154.15/
```
Expected: /opt/dashboard/ has dashboard.html + dashboard-data.js. curl returns 200 + dashboard HTML.

## Commit
`feat(dashboard): rsync push script for OCI deployment`
