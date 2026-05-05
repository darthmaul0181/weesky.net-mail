---
name: ship-microservice
description: Build, upload and restart the snoopy.microservice (weesky.net mail backend / REST API) on curiosity.weesky.net. Use when the user asks to ship, deploy, or release the microservice, the backend, or the API — for example "ship the microservice", "déploie le backend", "ship l'api". Do NOT use this skill to ship the frontend.
---

# Ship — snoopy.microservice release

Run the steps in order. Each step depends on the previous one. If a step fails, stop and report — don't proceed to the next.

## 1. Publish

The publish profile `src/snoopy.microservice/Properties/PublishProfiles/default.pubxml` targets `linux-x64`, self-contained, Release, and writes to `D:\development\builds\api`. The profile sets both `PublishUrl` (VS) and `PublishDir` (CLI) so `dotnet publish` honors the destination.

Empty the target directory (the profile does not delete existing files), then publish:

```powershell
Get-ChildItem D:\development\builds\api -Recurse -Force | Remove-Item -Recurse -Force -Confirm:$false
```

```bash
cd src/snoopy.microservice
dotnet publish -p:PublishProfile=default
```

Git Bash note: use `-p:Foo=Bar` (dash), not `/p:Foo=Bar` — the forward slash gets interpreted as a path.

## 2. Upload + restart

Run this step **from PowerShell** (not Git Bash): the SSH key is held by the Windows `ssh-agent` service, which the native OpenSSH client (`C:\Windows\System32\OpenSSH\ssh.exe`) uses via named pipe. Git Bash ships its own `ssh` binary and cannot reach that agent, so auth fails there.

Stream the published contents to the remote via tar-over-ssh. The archive contains the files directly (no wrapping `api/` folder), so they land straight into `/var/www/admin/mail/snoopy.microservice/` on the target. The one-liner chains extract → `chmod +x` on the binary → `chmod 770` on the directory → `systemctl restart` in a single SSH session.

```powershell
tar -cf - -C D:\development\builds\api . | ssh root@curiosity.weesky.net "tar -xf - -C /var/www/admin/mail/snoopy.microservice && chmod +x /var/www/admin/mail/snoopy.microservice/snoopy.microservice && chmod 770 /var/www/admin/mail/snoopy.microservice/ && systemctl restart snoopy.microservice"
```

- `chmod +x` is needed because tar archives built on Windows don't carry Unix execute bits — without it, the Linux binary can't be launched.
- `chmod 770` on the deployment directory locks out world access (rwx for owner+group only).
- `systemctl restart snoopy.microservice` picks up the freshly uploaded bits.
