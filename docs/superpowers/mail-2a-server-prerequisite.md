# Mail 2a — server prerequisite

**Apply this before deploying the mail slice.** Without it the service **refuses to start**
outside Development. That is deliberate, not a bug: the alternative is a silent fallback that
would drop every mail session on the next restart.

The systemd units are not versioned in this repository, so this change is manual and this note
is the only record of it.

## What and why

The mail endpoints open IMAP with the user's own password, which cannot be read back from the
database — MariaDB stores SHA-512 crypt. The password is therefore captured at login and kept
in a cookie encrypted with ASP.NET Core Data Protection.

That encryption depends on a **key ring**: a directory of key files, one of which is active for
encrypting while the rest are retained for decrypting. Every encrypted payload names the key
that produced it. Lose the directory and every live credentials cookie becomes undecryptable,
so every user is signed out of mail at once.

The framework's default location is `$HOME/.aspnet/DataProtection-Keys`, which happens to work
today only because the unit runs as `root` and systemd populates `$HOME`. Moving the service to
a dedicated user — a good change in itself — would silently relocate the keys. `StateDirectory=`
makes the location explicit, keeps it outside the deployment path (where the release `chmod`
and `chown` run recursively), and has systemd own its permissions.

## Apply

Edit **both** units — production and development have separate key rings on purpose, so that a
compromise of the development environment cannot decrypt production cookies.

```bash
systemctl edit --full snoopy.microservice
```

Add to the `[Service]` section:

```ini
StateDirectory=snoopy.microservice
StateDirectoryMode=0700
```

Then the development unit, with its own directory name:

```bash
systemctl edit --full snoopy.microservice-dev
```

```ini
StateDirectory=snoopy.microservice-dev
StateDirectoryMode=0700
```

Reload and restart:

```bash
systemctl daemon-reload
systemctl restart snoopy.microservice snoopy.microservice-dev
```

## Verify

1. The directories exist with the right ownership and mode:

   ```bash
   ls -ld /var/lib/snoopy.microservice /var/lib/snoopy.microservice-dev
   ```

   Expect `drwx------` owned by the unit's `User=` (currently `root`).

2. The service logs the resolved path at startup:

   ```bash
   journalctl -u snoopy.microservice-dev --since "5 min ago" | grep "key ring"
   ```

   Expect `Data Protection key ring: /var/lib/snoopy.microservice-dev/keys`. A path under
   `/var/www/admin/mail/...` means the change did not take.

3. **The test that actually proves the design works:** sign in to the webmail, then

   ```bash
   systemctl restart snoopy.microservice-dev
   ```

   and use the mail view again without signing in. If mail now fails to authenticate while the
   session still appears valid, the key ring is not persisting.

## If it was not applied

The service throws at startup with a message naming this file's fix. With `Restart=always` and
`RestartSec=60` in the unit, it will crash-loop once a minute and fill the journal — visible
and intentional, rather than a working-looking service that logs everyone out on the next
deployment.

## Not backed up, on purpose

Losing the key ring costs one re-login for everyone; the graceful path already exists
(decryption failure returns `401 credentials_unavailable`, and the client signs in again). A
backup copy would be a second set of keys capable of decrypting live credentials, for a benefit
worth one re-login. The trade is not worth it.
