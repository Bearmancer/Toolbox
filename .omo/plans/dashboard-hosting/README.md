# Plan: Dashboard Hosting on OCI

**Task:** T-fbbd3a00-c04c-4af2-a306-e3a464cea03d

## Goal
Host dashboard 24/7 on OCI ARM64 via nginx — **static files only**. NO .NET, NO repo clone, NO build on OCI.

## Architecture
```
Laptop (sync yt + dashboard generate)
    ↓ rsync state/dashboard/ → oci:/opt/dashboard/
OCI (nginx :80 serves /opt/dashboard/)
    ↓
http://100.68.154.15 (Tailscale network)
```

## OCI Details
- **IP**: 100.68.154.15 (Tailscale)
- **OS**: Ubuntu 26.04 ARM64
- **Access**: `ssh oci` (SSH alias configured)
- **Already running**: Docker media stack (emby, sonarr, radarr, etc.)
- **NOT installed**: .NET SDK, repo, build tools (and won't be)

## Access Options

| Option | URL | Requirements |
|--------|-----|--------------|
| **Tailscale** | `http://100.68.154.15` | Device on Tailscale network. Zero config. |
| **Cloudflare Tunnel** | `https://dashboard.yourdomain.com` | `cloudflared` on OCI. Free, public, no port forwarding. |

## Phases
- **0-nginx-only.md** — Install nginx on OCI, configure to serve /opt/dashboard/, no .NET
- **1-rsync-push.md** — Create rsync script on laptop to push generated files to OCI after sync
- **2-cloudflare-tunnel.md** — (Optional) Setup Cloudflare Tunnel for public access

## Dependencies
- T-7a5a0b00 (dashboard-to-state) — dashboard must output to state/dashboard/
- T-b251e669 (tie dashboard to sync) — sync must trigger dashboard regeneration

## Verify
```powershell
curl http://100.68.154.15/
ssh oci 'sudo nginx -t'
ssh oci 'which dotnet'
ssh oci 'ls /opt/dashboard/'
```
Expected: curl 200 + HTML. nginx -t passes. `which dotnet` returns nothing. /opt/dashboard/ has dashboard.html + dashboard-data.js only.
