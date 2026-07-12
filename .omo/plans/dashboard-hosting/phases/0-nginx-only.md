# Phase 0: Nginx Only — No .NET on OCI

## Task
Install nginx on OCI, configure to serve static dashboard files. NO .NET SDK, NO repo clone.

## Steps
1. SSH to OCI: `ssh oci`
2. Install nginx: `sudo apt update && sudo apt install -y nginx`
3. Create dashboard directory: `sudo mkdir -p /opt/dashboard`
4. Create nginx config:
   ```bash
   sudo tee /etc/nginx/sites-available/dashboard << 'EOF'
   server {
       listen 80;
       server_name _;
       root /opt/dashboard;
       index dashboard.html;
       location / {
           try_files $uri $uri/ =404;
       }
   }
   EOF
   ```
5. Enable: `sudo ln -s /etc/nginx/sites-available/dashboard /etc/nginx/sites-enabled/`
6. Remove default: `sudo rm -f /etc/nginx/sites-enabled/default`
7. Test: `sudo nginx -t`
8. Reload: `sudo systemctl reload nginx`

## Verify
```powershell
ssh oci 'sudo nginx -t'
ssh oci 'which dotnet'
ssh oci 'ls /opt/dashboard/'
```
Expected: nginx -t passes. `which dotnet` returns nothing. /opt/dashboard/ is empty (files come in Phase 1).

## Commit
N/A (infrastructure setup)
