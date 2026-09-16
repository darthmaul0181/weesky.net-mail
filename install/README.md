# Installing Scotty webmail

This guide installs Scotty on a Linux server. When you are done,
your users open **https://mail.example.net**, sign in with their usual mail address and password,
and find their mail, contacts and calendar.

There are five steps. Each one ends with a check: do not move on until it passes.

1. [Build the application](#step-1--build-the-application)
2. [Create the database](#step-2--create-the-database)
3. [Install the API as a service](#step-3--install-the-api-as-a-service)
4. [Publish it through your web server](#step-4--publish-it-through-your-web-server)
5. [Sign in](#step-5--sign-in)

---

## Before you start

### What your mail service needs

Scotty does not host mail. Like a mail app on a phone, it connects to a mail service that already
exists — your own server or a provider — and works through it. That service can be anything that
offers:

| | |
|---|---|
| **IMAP** | To read mail. Users must be able to log in with their **full mail address** and their password. |
| **SMTP with authentication** | To send mail, usually on port 587 or 465. |
| **ManageSieve** *(optional)* | Only for mail rules. Without it, everything else works. |

One installation serves the users of **one** mail service: all of them read and send through the
same IMAP and SMTP servers. Scotty keeps no copy of anyone's mail.

### What the server needs

| | |
|---|---|
| **Linux with systemd** | To run the API in the background and restart it if it stops. |
| **MySQL or MariaDB** | Scotty keeps a small database of its own there: settings, contacts, calendars. It can run on this server or another one. See [Which database](#which-database). |
| **A web server** | Apache, nginx or any other that can serve files and relay requests, with HTTPS. It shows the web pages and passes everything else to the API. |

Commands in this guide use Debian and Ubuntu paths; where the Red Hat family differs, it says so.

#### Which database

Scotty talks to its database through the MySQL protocol and writes MySQL's dialect of SQL, so it
needs a server from that family:

| | Versions |
|---|---|
| **MySQL** | 8.0, 8.4 |
| **MariaDB** | 10.5, 10.6, 10.11, 11.x |

These are the versions tested by the database library Scotty is built on. `install.sql` and the
service itself have also been run on MySQL 8.4 and MariaDB 11.4. Other
MySQL-compatible servers — Percona Server for MySQL, or a managed MySQL such as Amazon RDS or
Aurora, Azure Database for MySQL, Google Cloud SQL — usually work too, but nobody has tested them.

**PostgreSQL, SQLite, SQL Server and Oracle are not supported.** It is not a setting: using them
would mean changing Scotty's code and rewriting its database script.

### Two addresses, on the same domain

Scotty comes in two parts:

- the **web interface** — the pages the browser shows;
- the **API** — the service behind them, which talks to your mail server.

Each part gets its own address, and each address needs an HTTPS certificate. This guide uses
`mail.example.net` for the web interface and `api.example.net` for the API: replace them with your
own wherever they appear.

> **Both addresses must be on the same domain.** `mail.example.net` and `api.example.net` work
> together; `mail.example.net` and `api.other.org` do not. The browser only sends the sign-in cookie
> between addresses of the same domain, so on two domains signing in seems to work and every page
> after it fails.

### Two platforms: generic or weesky

Scotty runs in one of two modes, chosen by a single line of its settings.

**`generic` — any mail service.** Scotty is a webmail and nothing more: it reads and sends through
whatever IMAP and SMTP service you point it at, and knows nothing about how the mailboxes behind
them are managed. You keep managing them where you already do.

**`weesky` — everything in one place.** Scotty is also the control panel of the mail server itself.
The same interface users read their mail in lets an administrator create mailboxes, domains and
aliases, and lets each user change their own mail password or manage their aliases — no separate
admin tool, no second login. And because Scotty then knows every mailbox and every alias, it can do
what a plain webmail cannot:

| | `generic` | `weesky` |
|---|:---:|:---:|
| Mail, contacts, calendar, phone sync, mail rules | ✓ | ✓ |
| Administration: mailboxes, domains, aliases, quotas | — | ✓ |
| Users change their own mail password and display name | — | ✓ |
| Users manage their own aliases | — | ✓ |
| Sending only from addresses the user really owns | left to your SMTP server | checked by Scotty |
| A mailbox disabled by the administrator is signed out on its next action | — | ✓ |
| Administrator settings: product name, calendar service account for invitations sent from phones, connecting Outlook mailboxes | — | ✓ |

The price of `weesky` is that it only works with the mail server it was built for: Dovecot and
Postfix, reading their mailboxes from a MySQL or MariaDB database that Scotty administers. It cannot be
pointed at just any mail service.

> **This guide installs the `generic` platform.** The `weesky` platform and the mail server it
> expects will be documented in a separate repository, coming soon.

### Where everything goes

| What | Where |
|---|---|
| The API | `/opt/scotty` |
| The web interface | `/var/www/scotty` |
| The settings | `/etc/scotty/scotty.microservice.env` |

---

## Step 1 — Build the application

You need the **.NET 10 SDK** and **Node.js 20** for this step only. The simplest is to build on the
server itself. If you would rather not install them there, build on another machine and copy three
folders to the server, keeping their place in the repository: `out/api`, `src/frontend/dist` and
`install`.

Every command in this guide runs **as root, from the repository folder**.

```bash
git clone https://github.com/darthmaul0181/weesky.net-mail.git
cd weesky.net-mail
```

**1.1 Build the API.** The result runs on its own: the server does not need .NET.

```bash
dotnet publish src/scotty.microservice.host -c Release -r linux-x64 --self-contained \
  -p:ReleaseBuild=true -o out/api
```

**1.2 Build the web interface.** It has to know where the API is, and it writes that address into
the pages while building them — so set it first:

```bash
cd src/frontend
echo "VITE_API_BASE=https://api.example.net" > .env.production
npm ci
RELEASE_BUILD=true npm run build
cd ../..
```

If the API's address ever changes, build the web interface again.

✅ **Check:** both `out/api/scotty.microservice` and `src/frontend/dist/index.html` exist.

---

## Step 2 — Create the database

This creates Scotty's own database. Your mail service is not involved.

**2.1 Fill in the script.** Open `install/install.sql` and replace its two placeholders:

| Placeholder | Replace with |
|---|---|
| `__HOST__` | The address the API connects **from**: `127.0.0.1` if the database runs on the same server as the API, otherwise the API server's address |
| `__PASSWORD__` | A new password, made up for this. Write it down: step 3 needs it. |

**2.2 Run it** as a database administrator:

```bash
mysql -u root -p < install/install.sql
```

On MariaDB the command may be called `mariadb` instead of `mysql`. If the database runs on another
machine, add `-h` followed by its address.

✅ **Check:** at the end, the output shows `26` twice — twenty-six tables created, all twenty-six
with the right character set — followed by the account's rights: `SELECT, INSERT, UPDATE, DELETE`
on `scotty_webmail`.

---

## Step 3 — Install the API as a service

**3.1 Create an account for it.** The API runs under its own user, which has no password and
cannot log in.

```bash
useradd --system --no-create-home --shell /usr/sbin/nologin scotty
```

**3.2 Copy its files.**

```bash
mkdir -p /opt/scotty
cp -r out/api/. /opt/scotty/
chown -R root:scotty /opt/scotty
chmod -R u=rwX,g=rX,o= /opt/scotty
chmod 750 /opt/scotty/scotty.microservice
```

**3.3 Write its settings.** Copy the example file, then open it:

```bash
install -D -m 0640 -o root -g scotty install/scotty.microservice.env /etc/scotty/scotty.microservice.env
nano /etc/scotty/scotty.microservice.env
```

Change these lines, and leave the others as they are:

| Line | What to put |
|---|---|
| `ConnectionStrings__WebmailPreferencesDatabase` | Replace `CHANGE_ME` with the password from step 2. If the database is on another machine, replace `127.0.0.1` with its address too. |
| `TokenConstants__Key` | A long random value. Generate one with `openssl rand -base64 48` |
| `Cors__AllowedOrigins__0` | The web interface's address, `https://mail.example.net` |
| `Mail__ImapHost` | Your IMAP server's name |
| `Mail__SmtpHost` | Your SMTP server's name |
| `Sieve__Host` | Your ManageSieve server's name — usually the same as IMAP. No ManageSieve? Put the IMAP name anyway. |

Scotty reaches IMAP on port **143** and SMTP on port **587**, both with STARTTLS.
If yours uses **993** and **465** instead, add these four lines:

```ini
Mail__ImapPort=993
Mail__ImapSecurity=SslOnConnect
Mail__SmtpPort=465
Mail__SmtpSecurity=SslOnConnect
```

**3.4 Start it.**

```bash
cp install/scotty.microservice.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now scotty.microservice
```

✅ **Check:** wait a few seconds, then

```bash
curl http://127.0.0.1:5000/health
```

It answers `Healthy`.

- `Unhealthy` means the database password in the settings is wrong.
- No answer at all means the service did not start: see
  [When the service won't start](#when-the-service-wont-start).

---

## Step 4 — Publish it through your web server

Your web server does two jobs:

- on `mail.example.net`, it serves the web interface's files;
- on `api.example.net`, it passes every request on to the service, at `http://127.0.0.1:5000`.

**4.1 Copy the web interface.**

```bash
mkdir -p /var/www/scotty
cp -r src/frontend/dist/. /var/www/scotty/
```

**4.2 Configure the two sites.** Below are ready-made configurations for **Apache** and **nginx**.
Any other web server that can serve files and relay requests works too, as long as it does what
these do:

| On | The web server must |
|---|---|
| Both addresses | Serve HTTPS. |
| `mail.example.net` | Answer any address that is not a file with `/index.html`. The application draws its pages itself: without this, reloading any page but the first one shows "Not Found". |
| `mail.example.net` | Keep a real "Not Found" under `/assets/`. A missing script served as a page breaks the application with an error that points nowhere. |
| `api.example.net` | Pass every request to `http://127.0.0.1:5000`, adding the `X-Forwarded-For` and `X-Forwarded-Proto` headers. They tell the service who is asking, and that the visitor came over HTTPS. |
| `api.example.net` | Accept uploads of at least 30 MB. Attachments go up to 25 MB. |

Replace the names and certificate paths with your own.

#### With Apache

Turn on the modules it needs (Debian and Ubuntu; on the Red Hat family they are already loaded):

```bash
a2enmod ssl proxy proxy_http headers
```

Create `/etc/apache2/sites-available/scotty.conf` (Red Hat family: `/etc/httpd/conf.d/scotty.conf`):

```apache
# The web interface
<VirtualHost *:443>
    ServerName mail.example.net
    DocumentRoot /var/www/scotty

    SSLEngine on
    SSLCertificateFile    /etc/letsencrypt/live/mail.example.net/fullchain.pem
    SSLCertificateKeyFile /etc/letsencrypt/live/mail.example.net/privkey.pem

    <Directory /var/www/scotty>
        Require all granted
        AllowOverride None
        FallbackResource /index.html
    </Directory>
    <Directory /var/www/scotty/assets>
        FallbackResource disabled
    </Directory>
</VirtualHost>

# The API
<VirtualHost *:443>
    ServerName api.example.net

    SSLEngine on
    SSLCertificateFile    /etc/letsencrypt/live/api.example.net/fullchain.pem
    SSLCertificateKeyFile /etc/letsencrypt/live/api.example.net/privkey.pem

    ProxyPreserveHost On
    RequestHeader set X-Forwarded-Proto "https"
    ProxyPass        / http://127.0.0.1:5000/
    ProxyPassReverse / http://127.0.0.1:5000/
</VirtualHost>
```

Apache adds `X-Forwarded-For` by itself, and accepts large uploads by default. Then:

```bash
a2ensite scotty          # Debian and Ubuntu only
apachectl configtest && systemctl reload apache2     # Red Hat family: httpd
```

#### With nginx

Create `/etc/nginx/conf.d/scotty.conf`:

```nginx
# The web interface
server {
    listen 443 ssl;
    server_name mail.example.net;

    ssl_certificate     /etc/letsencrypt/live/mail.example.net/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mail.example.net/privkey.pem;

    root /var/www/scotty;

    location / {
        try_files $uri /index.html;
    }
    location /assets/ {
        try_files $uri =404;
    }
}

# The API
server {
    listen 443 ssl;
    server_name api.example.net;

    ssl_certificate     /etc/letsencrypt/live/api.example.net/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.example.net/privkey.pem;

    # nginx refuses anything over 1 MB by default: attachments would fail.
    client_max_body_size 30m;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Then:

```bash
nginx -t && systemctl reload nginx
```

> **Web server on another machine?** Point it at the API server's address instead of `127.0.0.1`,
> make the service listen on that address (`ASPNETCORE_URLS` in the settings file), and put the web
> server's own address in `ForwardedHeaders__KnownProxies__0`. The service only believes the
> forwarded headers of the machines listed there.

✅ **Check:** `curl https://api.example.net/health` answers `Healthy`, and
**https://mail.example.net** shows the sign-in page.

---

## Step 5 — Sign in

Open **https://mail.example.net** and sign in with the address and password of any mailbox your
mail server knows.

There is no account to create and no default password. Scotty asks your mail server whether the
password is right, and never stores it.

✅ **Check:** the inbox appears. Open a message and reload the page: the same message is still open.

**Scotty is installed.**

---

## Optional

### Mail rules

Users can manage their mail filters in **Settings → Rules** if your mail service offers
**ManageSieve** (port 4190). There is nothing more to set: the `Sieve__Host` line from step 3 is
enough. Without ManageSieve, that page shows an error and nothing else is affected.

### Contacts and calendars on phones

Scotty can sync contacts and calendars with iPhones, Android phones (through DAVx⁵) and Thunderbird.
Add this line to the settings file, with your API's address:

```ini
Dav__PublicUrl=https://api.example.net
```

then restart the service:

```bash
systemctl restart scotty.microservice
```

A **Sync** tab appears in each user's settings, with the server address, user name and password to
enter on the phone. Without this line the tab is simply not there, and nothing warns you.

The configurations of step 4 already let phones through. If a CDN or a firewall sits in front of
`api.example.net`, it must let through the requests phones use — `PROPFIND`, `REPORT`, `PUT`,
`DELETE` — and anything under `/.well-known/`: when it blocks them, the phone simply shows an empty
address book, with no error.

### Features of the weesky platform

These two are configured from the administration screens, so they need the `weesky` platform (see
[Two platforms](#two-platforms-generic-or-weesky)):

- [Connecting Outlook and Office 365 mailboxes](optional/oauth-providers.md)
- [Updating calendars as guests' replies arrive](optional/delivery-replies.md) — also needs your mail
  server to be Dovecot

---

## When the service won't start

If something essential is missing, the service refuses to start and writes down why. Read the last
lines of its log:

```bash
journalctl -u scotty.microservice -n 30
```

| If the log says | Do this |
|---|---|
| `Connection string 'WebmailPreferencesDatabase' is missing` | The settings file is not being read. Check the `EnvironmentFile=` line of `/etc/systemd/system/scotty.microservice.service` |
| `TokenConstants:Key must be at least 32 bytes` | `TokenConstants__Key` is empty or too short. Generate one with `openssl rand -base64 48` |
| `No CORS origin is configured` | Fill in `Cors__AllowedOrigins__0` |
| `No reverse proxy is configured` | Put back `ForwardedHeaders__KnownProxies__0=127.0.0.1` |
| `'Platform' is missing`, or `Connection string 'Weesky:ConnectionStrings:MailUserAccountsDatabase' is missing` | Put back `Platform=generic` |
| `STATE_DIRECTORY is not set` | Start the service with `systemctl`, not by hand, and keep the `StateDirectory=` line of the service file |

After any change to the settings: `systemctl restart scotty.microservice`.

---

## Keeping it running

**Updating.** Build the new version (step 1), copy the files again (3.2 and 4.1), then
`systemctl restart scotty.microservice`. `install.sql` is only for a new installation: never run
it on a database that is already in use.

**Backups.** Back up the `scotty_webmail` database. After restoring it, follow
[`../docs/operations/restore-sync-epoch-rotation.md`](../docs/operations/restore-sync-epoch-rotation.md)
before users reconnect — otherwise their phones quietly keep an out-of-date copy of their contacts
and calendars.

**The encryption keys** in `/var/lib/scotty.microservice` need no backup. If they are lost, users
just sign in again.
