# Optional — applying guests' calendar replies at delivery

When someone you invited accepts or declines, their reply arrives as a mail. Without this, the
calendar only learns about it when the organiser opens that mail. With it, Dovecot applies the
reply to the calendar as it delivers the message, and opening the mail becomes a fallback rather
than the mechanism.

Requires Dovecot 2.4.5 (Pigeonhole). The `delivery_reply_key` table `install.sql` created is all
the database side needs.

> **Do not turn the webmail switch on until the verification in [§ 6](#6-before-you-switch-it-on)
> passes.** A Sieve program that fails takes down *every user's* personal rules for that message,
> with no signal.

---

## 1. Dovecot

In the configuration — `doveconf -n` must show the block after editing:

```
sieve_plugins {
  sieve_extprograms = yes
}
sieve_global_extensions {
  vnd.dovecot.execute = yes
}
sieve_execute_bin_dir = /usr/lib/dovecot/sieve-execute
sieve_script calendar_replies {
  type = before
  driver = file
  path = /etc/dovecot/sieve/calendar-replies.sieve
  bin_path = /var/lib/dovecot/sieve   # a directory: Dovecot appends calendar-replies.svbin
}
```

`vnd.dovecot.execute` goes in **`sieve_global_extensions` only, never in `sieve_extensions`**:
users write their own rules from the webmail's Rules tab, and this extension would let them have
the server run a program.

`mime` and `foreverypart` are on by default; if `sievec` rejects the rule over one of them, add
the block below. Dovecot 2.4 separates a block's settings with a newline, never with `;`:

```
sieve_extensions {
  mime = yes
  foreverypart = yes
}
```

`bin_path` names a **directory**, never the file. `/var/lib/dovecot/sieve/` must exist and belong
to `vmail`, or Dovecot recompiles the rule on every delivery.

---

## 2. The rule — `/etc/dovecot/sieve/calendar-replies.sieve`

```
require ["mime", "foreverypart", "vnd.dovecot.execute"];
if size :under 5M {
  foreverypart {
    if allof (
      anyof (
        allof (header :mime :type "Content-Type" "text",
               header :mime :subtype "Content-Type" "calendar"),
        allof (header :mime :type "Content-Type" "application",
               header :mime :subtype "Content-Type" "ics")),
      header :mime :param "method" "Content-Type" "REPLY") {
      execute :pipe "calendar-reply";
      break;
    }
  }
}
```

Compile it, and do so again after **every** change:

```bash
sievec /etc/dovecot/sieve/calendar-replies.sieve /var/lib/dovecot/sieve/calendar-replies.svbin
chown vmail /var/lib/dovecot/sieve/calendar-replies.svbin
```

The rule stops nothing — no `stop`, no `discard` — so the user's own rules still run afterwards.

### Piloting on a single mailbox (optional)

To try the mechanism on one test account first, guard the rule by **delivery address**: the
address Dovecot received from Postfix for this mailbox (`RCPT TO`, the `envelope` extension), not
the `To:` header, which the sender writes as they please and which says nothing about the target
mailbox when the mail arrives by copy or through an alias.

Only the `require` line and the outer `if` change:

```
require ["envelope", "mime", "foreverypart", "vnd.dovecot.execute"];
if allof (envelope :is "to" "webmail-test@example.net", size :under 5M) {
  foreverypart {
    …   # unchanged
  }
}
```

Mail sent to an alias of the account arrives with the alias as delivery address, so list them:
`envelope :is "to" ["webmail-test@example.net", "other@example.net"]`. To generalise, drop the
`envelope` test and recompile — nothing else changes, on either side.

A personal rule cannot serve as a pilot: `vnd.dovecot.execute` is allowed in global rules only,
and that is deliberate.

---

## 3. The configuration file — `/etc/dovecot/calendar-reply.conf`

`root:vmail`, mode `0640`. One `curl` option per line:

```
url = "https://<ip-or-host-of-the-microservice>/api/Delivery/CalendarReplies"
header = "X-Delivery-Key: <the key generated in Administration>"
header = "Content-Type: message/rfc822"
connect-timeout = 2
max-time = 5
silent
output = "/dev/null"
```

Use a literal IP or an `/etc/hosts` entry, not a name to resolve on every delivery. Any process
running as `vmail` can read this file; what the key permits is bounded by design — it can rewrite
the reply of a guest already present on an event the webmail organises, and nothing else.

---

## 4. The script — `/usr/lib/dovecot/sieve-execute/calendar-reply`

`root:root`, mode `0755`. **The order of the statements is the safety here:** all of stdin is read
before any decision is taken, including when `mktemp` itself fails. Leaving before the pipe is
drained would show Dovecot a `SIGPIPE` on an unread input, and a failed Sieve action skips the
user's own rules for that mail.

```sh
#!/bin/sh
# Applies a guest's calendar REPLY at delivery through the webmail's internal door.
# Always exits 0: a failed Sieve action skips the user's own rules for this mail.
tmp=$(mktemp) || { cat >/dev/null; exit 0; }
trap 'rm -f "$tmp"' EXIT
cat > "$tmp"                                   # read everything first, unconditionally
[ -n "$USER" ] || exit 0                        # LDA without a user: nothing to do
[ "$(wc -c < "$tmp")" -le 5242880 ] || exit 0   # belt under the rule's size :under 5M
timeout 8 curl --config /etc/dovecot/calendar-reply.conf \
  -H "X-Delivery-Mailbox: $USER" --data-binary "@$tmp" >/dev/null 2>&1
exit 0
```

`timeout` (coreutils) bounds everything including name resolution, under the 10 s of
`sieve_execute_exec_timeout`. `--max-time` alone bounds only the transfer.

---

## 5. The reverse proxy — accept the mail server only

Apache, in the `<VirtualHost>` before its `ProxyPass` lines:

```apache
<Location "/api/Delivery/">
    Require ip <ipv4 of the mail server> <ipv6 of the mail server>
    LimitRequestBody 6291456
</Location>
```

nginx:

```
location /api/Delivery/ {
    allow <ipv4 of the mail server>;
    allow <ipv6 of the mail server>;
    deny all;
    client_max_body_size 6m;   # the 1 MB default would refuse a larger reply
    # … the proxy_pass and headers of the existing /api/ block
}
```

**The address to allow is the one the proxy sees.** With Dovecot and the microservice on one
machine and the script calling the public name, the connection arrives with the server's public
IP — or `127.0.0.1` if the name resolves locally in `/etc/hosts`. Make one test `curl` from the
mail server and read the address off the proxy's access log. Any other address gets a 403 from the
proxy, before the key is ever checked.

A machine with both IPv4 and IPv6 calls by one or the other depending on resolution at the time:
allow both, or force a family in the `curl` file (`ipv4`). The size ceiling lets through the 5 MB
the microservice accepts, which answers 413 above that on its own.

---

## 6. Before you switch it on

1. `sievec` runs without error, and `doveconf -n | grep -A4 calendar_replies` shows the block.
2. From the mail server, as `vmail`:

   ```bash
   curl -v --config /etc/dovecot/calendar-reply.conf \
     -H "X-Delivery-Mailbox: <a mailbox>" --data-binary @<a mail that is NOT a reply>
   ```

   Expect `404` while the setting is off — the key and the address are right if the service log
   carries "Delivery calls refused … door closed". Expect `200` with `NotAReply` once it is on.
   **Never send a real reply here:** against an open door, it would be applied.
3. In Administration → Application, turn the switch on.
4. Deliver a real one: reply from Gmail or Outlook.com to an invitation the webmail sent. The
   calendar is up to date **before** the mail is opened, the card shows "Last call received on …",
   and the log carries `Applied`. Then check that a personal rule of that user — a sort into a
   folder — still applied to that same message.
5. **With the microservice stopped** (`systemctl stop scotty.microservice`), deliver a second
   reply: the mail arrives, the personal rule still applies, and the Dovecot log carries no
   `execute` error. Restart the service; opening the mail then applies the reply as a fallback.

---

## 7. The mailbox identifier

`USER` is the Dovecot identifier of the mailbox owner, and it must be the address the user signs
in to the webmail with. A user who signs in through an alias has their `users` row on the alias:
delivery answers `UnknownMailbox` every time, visible in the log, and their replies apply only on
opening.

## 8. Backing out

Turning the switch off in Administration is enough: the door answers 404, the script exits 0, and
the rule can stay in place. A service version predating the feature knows neither the table nor
the route.
