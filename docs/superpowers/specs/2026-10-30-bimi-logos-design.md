# BIMI logos in the sender avatars

Extends [sender avatars](2026-10-30-sender-avatars-design.md), whose non-goal "BIMI logos" is lifted by this document.

## Intent

A brand that publishes a BIMI logo for its domain (La Poste, PayPal…) shows that logo in the message list, instead of a Gravatar or initials. Sender recognition at a glance, for senders who are not in the address book.

## Goals

- Source order becomes: contact photo, then BIMI logo, then Gravatar, then initials. A photo the owner saved for a contact is never overruled (owner's decision, 2026-10-30).
- A logo is shown only for a message whose `Authentication-Results` (topmost header, rule 7 of the backend CLAUDE.md) says `dmarc=pass`, for the domain it names in `header.from`. Without it, the row falls through to Gravatar.
- Activated by the existing `mail.showAvatars` switch; no setting of its own. The hint under the switch also says the sender's domain is looked up.

## Non-goals

- Verifying the VMC / CMC certificate. The logo is vouched for by DMARC alone, which is looser than Gmail; accepted by the owner for now.
- Showing the logo in the reader header, the composer or the contacts module.
- The `l=` / `a=` evidence fields of the BIMI record are not read beyond what fetching the logo needs.

## Anti-goals

- The browser never contacts the sender's domain, its DNS or its logo host.
- No SVG ever reaches the browser, and none is ever rendered with scripts or external references.
- A domain cannot make the server reach an internal address.
- No layout shift, no request for rows off screen (same rules as the Gravatar path).

## How it works

1. **The list says which rows qualify.** The summary FETCH already names headers for priority (`MailPriorityReader.Fields`, one round trip). `Authentication-Results` and `BIMI-Selector` join that list. `MailMessageSummary` gains `bimiDomain` (and `bimiSelector` when the header is present): the `header.from` domain of a `dmarc=pass` in the topmost header, otherwise omitted. Parsing reuses `MailAuthenticationReader`; the backend transcribes, it does not decide to trust.
2. **A new route, `GET /api/Avatars/Bimi?domain=&selector=`**, on `AvatarsController`: authenticated, same rate-limit policy, 404 before any lookup while the caller's `mail.showAvatars` is not `true`, 400 for anything that is not a bare domain name. The selector defaults to `default` and must match `[a-z0-9._-]{1,63}`.
3. **Server-side lookup**: TXT record `<selector>._bimi.<domain>`; if none and the domain has a parent organisational domain, retry there once. Read `l=`; fetch it; turn it into a PNG; return it with `nosniff` and `private, max-age=86400`. When the lookup returns several BIMI records, the first one starting `v=BIMI1` is used, a deviation from the BIMI draft, which says none should be; accepted for now.
4. **Fetching the logo is a request to a host the sender chose**, so, unlike Gravatar, SSRF is a real risk. Required: `https` only; at most 2 redirects, each hop re-validated and re-pinned (a target that is not https on port 443 with a DNS host name and no userinfo, or a third redirect, is a miss); port 443; the connection goes to an address resolved once and checked (refuse loopback, private, link-local, multicast, unique-local, and IPv4-mapped forms of these) and the request is pinned to it; the whole fetch bounded by one budget token (as `GravatarService` does); body capped at 32 KB (the BIMI limit); `Content-Type` must be `image/svg+xml`.
5. **SVG to PNG**: rasterised server-side to a fixed square (80 px), with no script execution and no external resource loaded (no `href` leaving the document, no `@import`, no `<image>` to a URL). A file the renderer rejects counts as a miss.
6. **Cache**: per domain and selector, shared by all users; hit 7 days, miss 24 h, failure 5 min, in the same memory budget idea as `GravatarService`; single-flight per key.
7. **Browser**: in the avatar chain, a row with `bimiDomain` asks the new route (through `requestBlob`, as for Gravatar) after the contact-photo check and before Gravatar; on 404 it falls through to Gravatar, then initials. The query is cached per domain+selector, a 404 included.

## Security

- Domain and selector are syntactically validated before any DNS or network use.
- Output is a server-made PNG; the original SVG is never forwarded.
- Disclosure: the user's switch now also reveals the domain (not the address) of each displayed DMARC-passing sender to that domain's DNS and logo host, via the server only. The hint says so.

## What's new

An entry (version = `src/frontend/VERSION` bumped in the branch; fr/en strings; `WhatsNewDot` / `WhatsNewTarget` on the existing avatars setting). The owner may waive it.

## Tests

- Header reading: topmost `Authentication-Results` only; `dmarc=pass` gives the `header.from` domain; `dmarc=fail`, a lower header or a forged `dmarc=pass` below the top give none; `BIMI-Selector` read, bad value ignored.
- Route: 400 for non-domains and bad selectors; 404 while avatars are off, with no lookup made; no DNS record, parent fallback, non-SVG, oversize, a third redirect or one to an unfetchable target, non-443, internal address (each name resolution form), timeout and rasteriser failure all answer 404; hit and miss caching; single-flight.
- SVG hardening: a file with a script, an external `href`, an `@import` and an XML entity bomb yields either a harmless PNG or a miss, with nothing fetched.
- Client: order contact photo > BIMI > Gravatar > initials; no request for a row without `bimiDomain`; fall-through on 404; one request per domain.
- Row geometry (logo on a coloured background, rounded square clip) is checked in `probes/mobile-layout.html`.

## Left to the builder

- DNS client and SVG rasteriser libraries (new dependencies; name them and why in the plan).
- How a logo with transparency sits in the avatar box (background token, padding).
- Cache sizes and exact lifetimes; whether `domain` and `selector` stay two query parameters.
