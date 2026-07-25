# Frontend — SPA routing server prerequisite

**Apply this on the Apache vhosts serving `account.frontend`.** Without it, reloading the
browser on any URL other than `/` returns a 404. The Apache configuration is not versioned in
this repository, so this change is manual and this note is the only record of it.

## Symptom

```
$ curl -o /dev/null -w '%{http_code}\n' https://account-dev.mail.weesky.net/mail
302   ->  https://error.weesky.net?code=404
```

`/` and `/index.html` answer 200; every other path 404s. Navigating inside the app works
because the router changes the URL client-side and never asks the server for it. Pressing F5
does ask, and there is no file named `mail` on disk.

## Fix

The build produces exactly one HTML file. Every application route must be answered with it, and
the router then reads `window.location` and renders the right page.

Find the vhosts:

```bash
grep -rl account.frontend /etc/apache2/sites-available/
```

Both are `*:443` blocks. Development serves
`/var/www/admin/mail-dev/account.frontend`, production `/var/www/admin/mail/account.frontend`.
Add **one line** to the existing `<Directory>`, and one nested block after it — the development
vhost shown here, production identical with `mail` in place of `mail-dev`:

```apache
        <Directory /var/www/admin/mail-dev/account.frontend>
                Options +FollowSymLinks
                AllowOverride None
                Require all granted

                FallbackResource /index.html
        </Directory>

        # Hashed assets must keep 404ing. An index.html served in place of a
        # missing .js is worse than a 404: the browser reports a syntax error
        # at "<!DOCTYPE", which says nothing about the real cause — a stale
        # index.html asking for a bundle the last deployment replaced.
        <Directory /var/www/admin/mail-dev/account.frontend/assets>
                FallbackResource disabled
        </Directory>
```

`FallbackResource` only fires when the requested path does not exist on disk, so real files —
`index.html`, `/assets/*`, `favicon` — are still served directly. It needs no `mod_rewrite`.

```bash
apachectl configtest && systemctl reload apache2
```

## Why the vhost and not a `.htaccess` in the build

A `.htaccess` shipped in `public/` would deploy itself, which is tempting. Both vhosts set
`AllowOverride None`, so it would be **ignored without a word** — the worst kind of fix. Even
enabled it would cost a directory walk with a stat per request, and would put server routing in
a file the deployment wipes and rewrites. The vhost is read once at startup.

## Verify

```bash
for p in / /mail '/mail?folder=INBOX&uid=1' /settings/general /assets/nope.js; do
  echo "$(curl -s -o /dev/null -w '%{http_code}' "https://account-dev.mail.weesky.net$p")  $p"
done
```

Expect `200` for the first four and `404` for `/assets/nope.js`. Then reload the browser on a
deep URL: the same message must still be open, since the folder and uid travel in the query
string.

## Note for the API

The API is on its own host, so nothing under this document's `FallbackResource` can shadow it.
Should the API ever be reverse-proxied under this vhost, `ProxyPass` matches before the
filesystem is consulted and stays unaffected — but re-read this section before doing it.
