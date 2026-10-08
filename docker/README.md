# Running Scotty webmail with Docker

One container serves the whole webmail: the pages and the API. When you are done, your users open
**https://mail.example.net**, sign in with their usual mail address and password, and find their
mail, contacts and calendar.

What the image does not include, on purpose:

- **the database.** Scotty keeps its settings, contacts and calendars in a MySQL or MariaDB database
  you already run, or install next to it;
- **HTTPS.** Your reverse proxy — Caddy, Traefik, nginx — holds the certificate and passes requests
  on. Sign-in only works over HTTPS: the browser keeps the session cookie for secure pages only.

There are six steps. Each one ends with a check: do not move on until it passes.

1. [Create the database](#step-1--create-the-database)
2. [Fill in the settings](#step-2--fill-in-the-settings)
3. [Put your HTTPS proxy in front](#step-3--put-your-https-proxy-in-front)
4. [Start](#step-4--start)
5. [Sign in](#step-5--sign-in)
6. [Back up](#step-6--back-up)

---

## Before you start

- **Docker** with the compose plugin (`docker compose version` answers).
- **MySQL 8.0 or 8.4, or MariaDB 10.5 to 11.x**, reachable from the container.
- **A mail service** with IMAP and authenticated SMTP, and optionally ManageSieve for mail rules —
  see [What your mail service needs](../install/README.md#what-your-mail-service-needs).
- **A domain name** pointing at this machine, and a reverse proxy that serves it over HTTPS.

## Step 1 — Create the database

Use `install/install.sql` from this repository, exactly as in
[step 2 of the classic guide](../install/README.md#step-2--create-the-database), with one
difference: `__HOST__` is the address **the container** connects from, not `127.0.0.1`. Docker
gives containers addresses in `172.16.0.0/12`, so write `172.%` — or `%` if the database only
accepts connections from your own network.

✅ **Check:** the script's last lines list the rights of `scotty_webmail` and `scotty_webmail_schema`.

## Step 2 — Fill in the settings

```bash
mkdir scotty && cd scotty
curl -fsSLO https://raw.githubusercontent.com/darthmaul0181/weesky.net-mail/master/docker/docker-compose.yml
curl -fsSL -o .env https://raw.githubusercontent.com/darthmaul0181/weesky.net-mail/master/docker/.env.example
chmod 600 .env
```

Open `.env` and change the lines marked **CHANGE**: the two database passwords from step 1 and the
database's address, your mail servers, and the administrators' addresses. The database address is
seen from inside the container: a database on this same machine is `host.docker.internal` on
Docker Desktop, and the machine's own address on a Linux server (not `127.0.0.1`, which is the
container itself).

The image already sets everything else: the port (8080), logs on the console, the `generic`
platform, where its keys are kept.

✅ **Check:** `docker compose config` prints the service without an error.

## Step 3 — Put your HTTPS proxy in front

Choose how your users reach Scotty:

- **one address** — `https://mail.example.net` shows the pages and answers the API under `/api/`.
  Nothing more to set. This is the simplest;
- **two addresses** — the pages on `https://mail.example.net`, the API on `https://api.example.net`.
  Add to `.env`:
  ```ini
  Frontend__ApiBase=https://api.example.net
  Cors__AllowedOrigins__0=https://mail.example.net
  ```
  Each address then serves only its own part: the pages are not found on `api.example.net`, the
  API is not found on `mail.example.net`.
  **Both addresses must be on the same domain**: the browser only sends the sign-in cookie between
  addresses of the same domain.

In both cases every request goes to the container's port 8080, and the proxy must pass on the
original `Host` header, the visitor's address (`X-Forwarded-For`) and `X-Forwarded-Proto`.

`ForwardedHeaders__KnownNetworks__0` in `.env` names where the proxy connects from. A proxy
installed on this machine reaches the container through Docker's network: keep `172.16.0.0/12`. A
proxy in a container of its own: name the range of the network it shares with Scotty
(`docker network inspect <network> -f '{{(index .IPAM.Config 0).Subnet}}'`).

#### Caddy, on this machine

```caddy
mail.example.net {
    encode zstd gzip
    reverse_proxy 127.0.0.1:8080
}
```

With two addresses, write `mail.example.net, api.example.net {` on the first line. Caddy fetches and
renews the certificates itself, and passes on the three headers without being told.

#### nginx, on this machine

`http2 on;` needs nginx 1.25.1 or later; on an older one, remove that line and write
`listen 443 ssl http2;` instead. With two addresses, the certificate must cover both.

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name mail.example.net;          # with two addresses: mail.example.net api.example.net

    ssl_certificate     /etc/letsencrypt/live/mail.example.net/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mail.example.net/privkey.pem;

    # nginx refuses anything over 1 MB by default: attachments would fail.
    client_max_body_size 30m;
    gzip on;
    gzip_types text/css text/javascript application/javascript application/json image/svg+xml;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

#### Traefik, in its own container

Remove the `ports:` lines from `docker-compose.yml`, then add to the `scotty` service, with
`traefik` being the network your Traefik container is on:

```yaml
    networks: [traefik]
    labels:
      - traefik.enable=true
      - traefik.http.routers.scotty.rule=Host(`mail.example.net`)
      - traefik.http.routers.scotty.entrypoints=websecure
      - traefik.http.routers.scotty.tls.certresolver=letsencrypt
      - traefik.http.routers.scotty.middlewares=scotty-compress
      - traefik.http.middlewares.scotty-compress.compress=true
      - traefik.http.services.scotty.loadbalancer.server.port=8080
```

and at the end of the file:

```yaml
networks:
  traefik:
    external: true
```

With two addresses: ``Host(`mail.example.net`) || Host(`api.example.net`)``. Set
`ForwardedHeaders__KnownNetworks__0` to the `traefik` network's range.

✅ **Check:** your proxy's configuration test passes (`caddy validate`, `nginx -t`).

## Step 4 — Start

```bash
docker compose up -d
docker compose ps
```

The first start creates the tables, which takes a few seconds.

✅ **Check:** after about a minute, `docker compose ps` shows `healthy`, and
**https://mail.example.net** shows the sign-in page. If it shows `unhealthy` or `restarting`, see
[When it won't start](#when-it-wont-start).

## Step 5 — Sign in

Sign in with any mailbox's full address and its password. There is no account to create: Scotty
checks them with your IMAP server. An address listed in `Generic__Administrators` also sees
Settings › Administration.

✅ **Check:** your inbox appears.

## Step 6 — Back up

Two things hold everything Scotty knows:

- **the database** — back it up as you back up any other;
- **the `scotty-state` volume** — the key that signs sessions and the keys that encrypt the secrets
  kept in the database. Without it, everyone is signed out, and an administrator must enter again
  the external mail services' secrets and the calendar service account's password.

```bash
docker run --rm -v scotty_scotty-state:/state -v "$PWD":/backup alpine \
  tar -czf /backup/scotty-state.tar.gz -C /state .
```

(`scotty_` is the compose project's name, the folder's by default: `docker volume ls` shows it.)

✅ **Check:** `scotty-state.tar.gz` is there and not empty.

---

## Updating

```bash
docker compose pull
docker compose up -d
```

That is all: the new version updates its tables itself when it starts. Settings › About shows the
running version, the image's number on its own line under the server's.

`docker-compose.yml` follows `:1`, every 1.x release. To stay on one exact version, write it
instead, such as `ghcr.io/darthmaul0181/scotty-webmail:1.0.0`.

**Database installed before version 1.3.0?** Run
[`install/adopt-existing-database.sql`](../install/adopt-existing-database.sql) once, as in
[Installed before version 1.3.0?](../install/README.md#keeping-it-running).

## Optional

**Contacts and calendars on phones.** Add `Dav__PublicUrl` to `.env`: the address that answers the
API — `https://mail.example.net` with one address, `https://api.example.net` with two — then
`docker compose up -d`. A **Sync** tab appears in each user's settings.

**Mail rules, Outlook mailboxes, calendar replies:** as in [Optional](../install/README.md#optional)
of the classic guide; the settings go in `.env` instead of the service's settings file.

## When it won't start

Read why:

```bash
docker compose logs --tail 30 scotty
```

The messages of [When the service won't start](../install/README.md#when-the-service-wont-start)
apply here too, `.env` being the settings file. And these are Docker's own:

| You see | Do this |
|---|---|
| `restarting` in `docker compose ps`, and `Unable to connect to any of the specified MySQL hosts` in the log | The container cannot reach the database. Use an address it can reach (step 2), and let the database accept connections from Docker's network (step 1's `__HOST__`) |
| With two addresses, the sign-in page loads but signing in fails, and every `/api/…` call answers 404 | The proxy does not pass on the original `Host` header (step 3): the container cannot tell the API's address from the pages' |
| Signing in seems to work, then every page asks again | The site is not on HTTPS, or the two addresses are on two different domains (step 3) |
| Everyone is signed out after each update | The `scotty-state` volume is missing from `docker-compose.yml` |
| `Access to the path '/var/lib/scotty/…' is denied` | A folder mounted there instead of the volume must belong to user 1654: `chown -R 1654:1654 <folder>` |
| `Frontend:ApiBase holds '…'` | Write the API's address alone, such as `https://api.example.net`, without a path — or remove the line for one address |
| `No CORS origin is configured. Frontend:ApiBase sends…` | With two addresses, add `Cors__AllowedOrigins__0` with the pages' address |
| `No reverse proxy is configured` | Fill in `ForwardedHeaders__KnownNetworks__0` (step 3) |
| `Mail:ImapHost and Mail:SmtpHost must name your mail servers` | Fill in `Mail__ImapHost` and `Mail__SmtpHost` in `.env` (step 2) |
| `unhealthy` forever, though the log shows no error | `.env` sets `ASPNETCORE_URLS`: remove it, the image listens on 8080 |
