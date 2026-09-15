# Reverse proxy prerequisite — `ForwardedHeaders__KnownProxies`

**Apply this before the next deployment.** Without it the service **refuses to start** outside
Development, exactly as it does without `StateDirectory=` — see `mail-2a-server-prerequisite.md`,
which this note follows in both form and reasoning.

The systemd units are not versioned in this repository, so this change is manual and this note is
the only record of it.

## What and why

The login rate limiter partitions on the caller's address:

```
POST /api/login                              5 requests per minute, per address
POST /api/ConnectedAccounts                  (attaching a mailbox verifies a password)
PUT  /api/ConnectedAccounts/{id}/Password    (so does re-entering one)
PATCH /api/Account/ChangeSecret              (and so does changing your own)
POST /api/Delivery/CalendarReplies           concurrency, not per address
```

Behind the reverse proxy, every request reaches Kestrel **from the proxy**, so before this change
the first four endpoints above — the ones that verify a password — shared **one global bucket**.
Two things followed from that, and only the second one is obvious:

- Five attempts from anybody answered `429` to **every user of the service** — a denial of
  service that costs an attacker five requests a minute.
- Nothing about the limiter discriminated the source of a password-guessing run, because every
  source had the same partition key.

The fifth entry, the delivery door, is not part of that story at all: it carries a **concurrency**
limiter on purpose, one partition shared by every caller, because every legitimate caller is the
one mail server — an address-partitioned bucket would protect nobody there and a shared one is
exactly the point (spec 5e3, décision 13). It needs the forwarded-address fix below for its own
HTTP log line to read true, not for its rate limit.

`UseForwardedHeaders` puts the client's own address back on the request. It is not enabled by
default, and it must not be enabled blindly: `X-Forwarded-For` is a caller-supplied header, so a
middleware that honours it from any peer hands anybody the ability to choose their own partition
key — and to write whatever address they like into the audit log. It is therefore honoured only
from the proxies named here, and the framework's default known-proxy entries are cleared so that
trust is only ever what this file spelled out.

## Apply

```bash
systemctl edit --full snoopy.microservice
```

In the `EnvironmentFile` the unit already uses for `Cors__AllowedOrigins__0`, add the address the
proxy connects **from**:

```ini
ForwardedHeaders__KnownProxies__0=127.0.0.1
```

Add `ForwardedHeaders__KnownProxies__1=::1` as well if Kestrel listens on the IPv6 loopback (an
`ASPNETCORE_URLS` naming `localhost` rather than `127.0.0.1` usually does). If the proxy runs on
another host, name that host's address instead — the loopback entries are then wrong, not merely
redundant.

Repeat for the development unit, then:

```bash
systemctl daemon-reload
systemctl restart snoopy.microservice snoopy.microservice-dev
```

## Verify

1. The service starts at all. If the variable is missing it throws at startup with a message
   naming this fix, and crash-loops once a minute under `Restart=always` — visible and
   intentional, rather than a working-looking service whose limiter protects nobody.

2. The **backend's own** HTTP log carries client addresses rather than the proxy's. Serilog writes
   it to a file of its own — this is not the web server's access log, and not `journalctl`:

   ```bash
   tail -f /var/log/snoopy.microservice/log*http*.log
   ```

   Each line reads `HTTP GET /api/... from <address> responded 200 in … ms`. Browse from another
   machine: the address must be that machine's. Every line reading `127.0.0.1` (or whatever the
   proxy connects from) means the header is not being honoured — the address named above does not
   match the one the proxy really uses.

   The two files are `log-http-*.log` for requests and `log-*.log` for everything else, prefixed
   with the environment name outside production (`log-development-http-*.log` on the dev unit).

3. **The test that proves the partition works:** fail a login five times from one machine, then
   sign in normally from another. The second machine must not see `429`.

## What this does not do

It bounds guessing per address, not per account. A distributed run against one mailbox still gets
five attempts per minute from each address it controls. Adding a per-account counter was
considered and left out on purpose: it lets anyone lock a mailbox they do not own out of its own
webmail, which is a denial of service against a named person rather than against a botnet. If it
is ever wanted, it belongs behind a delay that grows rather than a hard refusal.

## Delivery door

The `POST /api/Delivery/CalendarReplies` route is meant to be reachable from the mail server only —
everything that actually keeps a stranger out (the key, the 404 that does not say whether the door
is open) lives in the application, but the proxy is the cheap extra layer named by spec 5e3,
décision 5. Apache, inside the `<VirtualHost>` before its `ProxyPass` lines:

```apache
<Location "/api/Delivery/">
    Require ip <ip of the mail server>
    LimitRequestBody 6291456
</Location>
```

nginx:

```
location /api/Delivery/ {
    allow <ip of the mail server>;
    deny all;
    client_max_body_size 6m;
    # … the proxy_pass and headers of the existing /api/ block
}
```

The address to allow is the one the proxy sees: with Dovecot and the service on one machine and
the script calling the public name, that is the server's own public IP, or `127.0.0.1` when the
name resolves locally — read it off the proxy's access log after one test call. nginx's default
`client_max_body_size` is 1 MB and would refuse a reply larger than that before the service's own
5 MB limit ever runs; Apache's default is far higher, so its line only bounds. See § 5 of
`docs/superpowers/webmail-delivery-replies-prerequisite.md` for the rest of the mail-server-side
setup.

## CardDAV

Before plan c opens the `/dav` routes, verify the reverse proxy in front of them:

- It passes through `PROPFIND`, `PROPPATCH`, `REPORT`, `OPTIONS`, `HEAD`, `PUT` and `DELETE` —
  many configurations default to allowing only `GET`/`POST`/`HEAD`.
- It does not strip `Depth`, `If-Match`, `If-None-Match`, or `Authorization` — some configurations
  swallow the `Authorization` header on routes they believe are public.
- It does not impose a body-size ceiling lower than ours (1 MB).
- **It does not answer `/.well-known/` itself.** This is the most common failure mode of a CDN or
  a web application firewall in front of a DAV server: the path is intercepted at the edge, the
  `301` never reaches the client, and pairing fails on a `404` before the first authenticated
  request. The check is a single `curl -X PROPFIND` from outside.

The symptom is what makes this expensive: `limit_except` or a WAF rejects silently, and **what the
client sees is an empty address book, with no error.**
