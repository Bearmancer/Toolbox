# Phase 2: Cloudflare Tunnel (Optional)

## Task
Setup Cloudflare Tunnel on OCI for public HTTPS access without opening ports.

## Steps
1. SSH to OCI: `ssh oci`
2. Install cloudflared: `sudo apt install -y cloudflared`
3. Authenticate: `cloudflared tunnel login`
4. Create tunnel: `cloudflared tunnel create dashboard`
5. Configure: `~/.cloudflared/config.yml`
   ```yaml
   tunnel: dashboard
   credentials-file: /root/.cloudflared/<id>.json
   ingress:
     - hostname: dashboard.yourdomain.com
       service: http://localhost:80
     - service: http_status:404
   ```
6. Add DNS: `cloudflared tunnel route dns dashboard dashboard.yourdomain.com`
7. Run as service: `sudo cloudflared service install`

## Verify
```powershell
curl https://dashboard.yourdomain.com/
```
Expected: Returns 200 + dashboard HTML over HTTPS. Accessible from public internet without Tailscale.

## Commit
N/A (infrastructure setup)
