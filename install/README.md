# Installing Scotty webmail

Everything a new deployment needs, in the order it needs it. Follow the five steps below and the
service starts; skip one and it refuses to start, on purpose, with a message naming what is
missing.

Two of these steps are server configuration that lives outside this repository — systemd units
and Apache vhosts are not versioned here, so this document is their only record. Two commented
examples come with it, to copy and edit rather than write from scratch:
[`scotty.microservice.service`](scotty.microservice.service) and
[`scotty.microservice.env`](scotty.microservice.env).

| # | Step | Without it |
|---|---|---|
| 1 | [The database](#1-the-database) | The service refuses to start |
| 2 | [The service unit and its key ring](#2-the-service-unit-and-its-key-ring) | Refuses to start outside Development |
| 3 | [The reverse proxy's address](#3-the-reverse-proxys-address) | Refuses to start outside Development |
| 4 | [SPA routing in Apache](#4-spa-routing-in-apache) | Reloading any page but `/` returns 404 |
| 5 | [Configuration and first sign-in](#5-configuration-and-first-sign-in) | — |

Two optional features have setups of their own, and most deployments need neither:
[connecting external mailboxes over OAuth](optional/oauth-providers.md) and
[applying guests' calendar replies at delivery](optional/delivery-replies.md).

---

## 1. The database

The webmail keeps its own database, separate from the mail server's `dovecot` database. That
separation is deliberate: `dovecot` belongs to Dovecot and may be rebuilt by the mail server's
provisioning, which would take the webmail's data with it, and the two have different backup and
retention policies.

**This project has no EF migrations.** The schema is created by hand, once, and the service never
alters it — it is not even granted the right to.

```bash
# Edit the two placeholders at the top of the file first: __HOST__ and __PASSWORD__
mysql -u root -p < install.sql
```

[`install.sql`](install.sql) creates the database, a MySQL account with rights on the data and
nothing else, and the 26 tables. It inserts no rows: an empty schema is the correct initial
state, and every setting is entered later in the Administration screens.

The script ends with two verification queries. Run them.

To bring up a second environment — a development database alongside production — replace
`scotty_webmail` throughout with the name you want and run it again. Keep the MySQL accounts
distinct, so that a leak on one side does not reach the other.

Then set the connection string on the service, never in a versioned file:

```
ConnectionStrings__WebmailPreferencesDatabase =
  Server=<host>;Port=3306;Database=scotty_webmail;User=scotty_webmail;Password=<...>;
```

The service refuses to start without it, in every environment including Development. A silently
inert feature is worse than a failure to start. It goes in the `EnvironmentFile` of step 2, with
the other secrets — [`scotty.microservice.env`](scotty.microservice.env) is a filled-in example.

> **Changing a database that is already in service** is a different job: `install.sql` builds a
> new one, and stops rather than touch an existing schema. Why the schema looks the way it does is
> in [`../docs/schema-notes.md`](../docs/schema-notes.md).

---

## 2. The service unit and its key ring

**Symptom if skipped:** the service throws at startup outside Development, naming this fix. Under
`Restart=always` it crash-loops once a minute and fills the journal — visible and intentional,
rather than a working-looking service that signs everyone out at the next deployment.

### Why

The mail endpoints open IMAP with the user's own password, which cannot be read back from the
database — MariaDB stores SHA-512 crypt. The password is captured at login and kept in a cookie
encrypted with ASP.NET Core Data Protection.

That encryption depends on a **key ring**: a directory of key files, one active for encrypting,
the rest retained for decrypting. Lose the directory and every live credentials cookie becomes
undecryptable, signing every user out of mail at once.

The framework's default location is `$HOME/.aspnet/DataProtection-Keys`, which works today only
because the unit runs as `root` and systemd populates `$HOME`. Moving the service to a dedicated
user — a good change in itself — would silently relocate the keys. `StateDirectory=` makes the
location explicit, keeps it out of the deployment path (where the release `chmod` and `chown` run
recursively), and hands systemd the ownership of its permissions.

### Apply

**Setting up a new deployment:** copy the two examples, edit the marked lines in each, and start
the service.

```bash
install -m 0600 -D scotty.microservice.env /etc/scotty/scotty.microservice.env
cp scotty.microservice.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now scotty.microservice
```

The unit already carries the two lines that matter here:

```ini
StateDirectory=scotty.microservice
StateDirectoryMode=0700
```

**Already running a unit of your own:** add those two lines to its `[Service]` section
(`systemctl edit --full <unit>`), then `systemctl daemon-reload && systemctl restart <unit>`.

If you run a second environment alongside production, give it a `StateDirectory` of its own —
separate key rings mean a compromise on one side cannot decrypt the other's cookies.

### Verify

```bash
ls -ld /var/lib/scotty.microservice
```

Expect `drwx------` owned by the unit's `User=`. Then check the path the service resolved:

```bash
journalctl -u scotty.microservice --since "5 min ago" | grep "key ring"
```

Expect `Data Protection key ring: /var/lib/scotty.microservice/keys`. A path under `/var/www/...`
means the change did not take.

**The test that proves the design works:** sign in to the webmail, `systemctl restart
scotty.microservice`, then use the mail view again without signing in. If mail fails to
authenticate while the session still looks valid, the key ring is not persisting.

### Not backed up, on purpose

Losing the key ring costs one re-login for everyone, and the graceful path already exists: a
decryption failure returns `401 credentials_unavailable` and the client signs in again. A backup
copy would be a second set of keys able to decrypt live credentials, for a benefit worth one
re-login. The trade is not worth it.

---

## 3. The reverse proxy's address

**Symptom if skipped:** the service throws at startup outside Development, exactly as in step 2.

### Why

The login rate limiter partitions on the caller's address — 5 requests per minute per address on
`POST /api/login` and on the three other endpoints that verify a password. Behind a reverse proxy
every request reaches Kestrel **from the proxy**, so without this setting all four share **one
global bucket**. Five attempts from anybody would answer `429` to every user of the service: a
denial of service costing an attacker five requests a minute.

`UseForwardedHeaders` puts the client's own address back on the request. It must not be enabled
blindly: `X-Forwarded-For` is caller-supplied, so honouring it from any peer hands anybody the
ability to choose their own partition key — and to write whatever address they like into the
audit log. It is honoured only from the proxies named here, and the framework's default
known-proxy entries are cleared so that trust is only ever what you spelled out.

### Apply

The example `EnvironmentFile` already carries it:

```ini
ForwardedHeaders__KnownProxies__0=127.0.0.1
```

Add `ForwardedHeaders__KnownProxies__1=::1` as well if Kestrel listens on the IPv6 loopback (an
`ASPNETCORE_URLS` naming `localhost` rather than `127.0.0.1` usually does). If the proxy runs on
another host, name that host's address instead — the loopback entries are then wrong, not merely
redundant. Then `systemctl restart scotty.microservice`.

### Verify

The backend writes its own HTTP log, which is neither the web server's access log nor
`journalctl`:

```bash
tail -f /var/log/scotty.microservice/log*http*.log
```

Each line reads `HTTP GET /api/... from <address> responded 200 in … ms`. Browse from another
machine: the address must be that machine's. Lines still reading `127.0.0.1` mean the header is
not being honoured — the address configured above is not the one the proxy really uses.

**The test that proves the partition works:** fail a login five times from one machine, then sign
in normally from another. The second machine must not see `429`.

### What it does not do

It bounds guessing per address, not per account. A distributed run against one mailbox still gets
five attempts a minute from each address it controls. A per-account counter was considered and
left out on purpose: it would let anyone lock a mailbox they do not own out of its own webmail — a
denial of service against a named person rather than against a botnet.

### If you serve CardDAV or CalDAV through this proxy

**Set `Dav__PublicUrl` first, or the feature is silently absent.** It goes in the same
`EnvironmentFile` (`Dav:PublicUrl` in `appsettings.json`, shipped empty) and holds the origin the
reverse proxy serves — a bare origin, exactly: no path, no trailing slash, no port, no
credentials, because clients append `/.well-known/carddav` themselves. The service refuses to
start on any other shape rather than let the value reach the screen.

Leaving it empty is legal and is the default: the deployment serves no `/dav`,
`GET /api/DavCredentials` answers `404`, and the Sync tab does not appear. **Nothing says so at
startup** — this is the failure to know about, a whole feature staying quiet because a variable is
missing.

Then check four things before opening the `/dav` routes, because the failure mode is expensive: a
`limit_except` rule or a web application firewall rejects silently, and **what the client sees is
an empty address book, with no error.**

- `PROPFIND`, `PROPPATCH`, `REPORT`, `OPTIONS`, `HEAD`, `PUT` and `DELETE` pass through. Many
  configurations allow only `GET`/`POST`/`HEAD`.
- `Depth`, `If-Match`, `If-None-Match` and `Authorization` are not stripped. Some configurations
  swallow `Authorization` on routes they believe are public.
- No body-size ceiling lower than ours (1 MB).
- **The proxy does not answer `/.well-known/` itself.** This is the most common failure of a CDN
  or WAF in front of a DAV server: the path is intercepted at the edge, the `301` never reaches
  the client, and pairing fails on a `404` before the first authenticated request. One
  `curl -X PROPFIND` from outside is the whole check.

---

## 4. SPA routing in Apache

**Symptom if skipped:** `/` and `/index.html` answer 200; every other path 404s. Navigating
inside the app works, because the router changes the URL client-side and never asks the server
for it. Pressing F5 does ask, and there is no file named `mail` on disk.

### Apply

The build produces exactly one HTML file. Every application route must be answered with it, and
the router then reads `window.location` and renders the right page.

```bash
grep -rl account.frontend /etc/apache2/sites-available/
```

Add **one line** to the existing `<Directory>`, and one nested block after it:

```apache
        <Directory /var/www/<deployment-path>/account.frontend>
                Options +FollowSymLinks
                AllowOverride None
                Require all granted

                FallbackResource /index.html
        </Directory>

        # Hashed assets must keep 404ing. An index.html served in place of a
        # missing .js is worse than a 404: the browser reports a syntax error
        # at "<!DOCTYPE", which says nothing about the real cause - a stale
        # index.html asking for a bundle the last deployment replaced.
        <Directory /var/www/<deployment-path>/account.frontend/assets>
                FallbackResource disabled
        </Directory>
```

`FallbackResource` fires only when the requested path does not exist on disk, so real files are
still served directly. It needs no `mod_rewrite`.

```bash
apachectl configtest && systemctl reload apache2
```

### Verify

```bash
for p in / /mail '/mail?folder=INBOX&uid=1' /settings/general /assets/nope.js; do
  echo "$(curl -s -o /dev/null -w '%{http_code}' "https://<your-host>$p")  $p"
done
```

Expect `200` for the first four and `404` for `/assets/nope.js`. Then reload the browser on a
deep URL: the same message must still be open, since the folder and uid travel in the query
string.

### Why the vhost and not a `.htaccess` in the build

A `.htaccess` shipped in `public/` would deploy itself, which is tempting. Both vhosts set
`AllowOverride None`, so it would be **ignored without a word** — the worst kind of fix. Even
enabled it would cost a directory walk with a stat per request, and would put server routing in a
file the deployment wipes and rewrites. The vhost is read once at startup.

---

## 5. Configuration and first sign-in

Nothing else is a file. There is no seeding step and no default password to change: the webmail
authenticates every user against the mail server itself, with an IMAP login.

What you can configure afterwards depends on the `Platform` you set in step 2.

**`Platform=weesky`** — a Dovecot database backs the accounts. Sign in with an account whose
`admin` flag is set there, and **Administration** opens:

- **Application** — the product name shown in the tab and top bar, the calendar service account,
  and the delivery key of the optional feature below.
- **Accounts, aliases, domains** — the mail server's own data, read from `dovecot`.

**`Platform=generic`** — any IMAP server, with nothing behind the mailbox. There is no directory
to administer, so those screens are absent, and **the administrator-only settings cannot be set
at all**: the product name keeps its default, and the calendar service account and the delivery
key stay unconfigured. Mail, contacts and calendar work; the two optional features below do not.

### One thing to check on Postfix

Sending with a `From` set to one of the user's aliases requires `smtpd_sender_login_maps` to let
the authenticated user use their aliases as envelope sender. Without it Postfix answers 553 and
the webmail shows "The mail server refused to send from *address*".

```bash
postconf smtpd_sender_login_maps
```

A query against the alias table is the usual value. Check too that
`reject_sender_login_mismatch` — or `reject_authenticated_sender_login_mismatch` — appears in
`smtpd_sender_restrictions`.

---

## Once it runs

- **Restoring a backup of the webmail database** is not just a restore: the CardDAV and CalDAV
  sync epochs must be rotated before clients reconnect, or devices silently keep a stale view.
  See [`../docs/operations/restore-sync-epoch-rotation.md`](../docs/operations/restore-sync-epoch-rotation.md).
- **Architecture** lives next to the code it describes:
  [`../src/scotty.microservice/DESIGN.md`](../src/scotty.microservice/DESIGN.md) for the backend,
  the `CLAUDE.md` of each component, and [`../docs/README.md`](../docs/README.md) for the rest of
  the documentation.
