---
name: ship-frontend
description: Build locally and upload the weesky.net mail frontend (account.frontend) to curiosity.weesky.net. Use when the user asks to ship, deploy, or release the frontend — for example "ship the frontend", "déploie le frontend", "ship le front". Do NOT use this skill to ship the microservice / backend / API.
---

# Ship — account.frontend release

Build happens **locally**; only the `dist/` output is uploaded. The server no longer needs Node.js or `node_modules`. The docroot `/var/www/admin/mail/account.frontend/` is what nginx serves directly, so `dist/` contents land straight at the root (no wrapping `dist/` folder on the remote).

Run the steps in order. Each step depends on the previous one. If a step fails, stop and report — don't proceed to the next.

Run these commands **from PowerShell** (not Git Bash): the SSH key is held by the Windows `ssh-agent` service, which the native OpenSSH client (`C:\Windows\System32\OpenSSH\ssh.exe`) uses via named pipe. Git Bash ships its own `ssh` binary and cannot reach that agent, so auth fails there.

## 1. Build locally

```powershell
cd D:\development\repos\weesky.net-mail\src\frontend; npm run build
```

Produces `D:\development\repos\weesky.net-mail\src\frontend\dist\`.

## 2. Clean docroot, upload, chmod, chown

Single SSH session that:
1. Wipes the docroot contents (old hashed assets would otherwise accumulate). `find -mindepth 1 -delete` removes everything inside without touching the directory itself.
2. Extracts `dist/` contents into the docroot root.
3. `chmod 750` on the docroot (rwx for owner, r-x for www-data group, world excluded).
4. `chown root:www-data -R` so nginx (running as www-data) can read everything.
5. `chmod 750` on `assets/` (same lockdown as docroot — r-x for www-data so nginx can serve the hashed static assets, world excluded).

```powershell
tar -cf - -C D:\development\repos\weesky.net-mail\src\frontend\dist . | ssh root@curiosity.weesky.net "find /var/www/admin/mail/account.frontend -mindepth 1 -delete && tar -xf - -C /var/www/admin/mail/account.frontend && chmod 750 /var/www/admin/mail/account.frontend && chown root:www-data /var/www/admin/mail/account.frontend -R && chmod 750 /var/www/admin/mail/account.frontend/assets"
```
