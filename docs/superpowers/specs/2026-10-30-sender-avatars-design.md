# Sender avatars in the message list

## Intent

Let a reader recognise a sender at a glance. Each message row can show a 40 px avatar: the sender's picture when one exists, the initials otherwise. Opt-in, from Settings.

## Goals

- Avatar in the message list, between the selection checkbox and the sender / subject / preview block (mockup B, "rounded square").
- Picture sources, in priority order: the contact's photo, then Gravatar, then initials.
- Two looks, chosen in Settings: one colour per sender, or monochrome.
- Every colour is a theme token or derived from one, in all sixteen palette/mode combinations.

## Non-goals

- Favicons, any other image source. BIMI logos came later: see [2026-10-30-bimi-logos-design.md](2026-10-30-bimi-logos-design.md).
- An avatar in the reader header, the composer or the contacts module.
- Uploading an avatar for an arbitrary address.

## Anti-goals

- The browser must never contact Gravatar or any third party.
- No visible layout shift: the avatar column is reserved before the image arrives, and a row keeps the same height whether or not it has a picture.
- No request per row for rows that are not on screen.

## Settings (Mail > Message list)

| Key | Values | Default |
|---|---|---|
| `mail.showAvatars` | `'true'` / `'false'` | off |
| `mail.avatarColor` | `'mono'` / `'sender'` | `mono` |

- `mail.showAvatars` follows `showPreviewOf`'s rule: on only when the stored value is exactly `'true'`.
- `mail.avatarColor` is `.is-child` of the first (indented, `.is-covered` while the parent is off); its value is kept while the parent is off. An unknown value reads as `mono`.
- Gravatar lookup is **not** a setting of its own: switching avatars on enables it (owner's decision, 2026-10-30). The hint under the switch says the sender's address is checked against Gravatar through the server.
- Registered in `UserPreferences.cs` and read through `usePreferences.ts`, like `mail.showPreview`.

## The avatar

- Rounded square, 40 px, `--radius-md`, a hairline `--border`. Photo covers the box; initials sit on `--folders-bg` in `--pane-item-active-fg` (mono) or on a tint of the sender's hue (sender).
- Sender look: the hue is a hash of the folded, trimmed address, so one address always gets one colour; lightness and saturation are fixed per theme (as `lib/customPalette.ts` does), and the text/background pair clears 4.5:1 in all sixteen combinations, asserted by a sweep like `customPalette.test.ts`'s.
- Initials: first letters of the first two words of the display name; with no name, the first letter of the address. Upper-cased. An address is never treated as a name (`contactNameOf` rule).
- Whose avatar: `fromName`/`fromAddress`; in a drafts-role folder the first recipient; on a collapsed conversation the head's sender; on a cross-folder result, as any row.
- The row stays four grid cells. The avatar is decorative (`aria-hidden`), inside the content cell, so the composed `list.rowLabel` name and the cell count do not change.
- Layout: the checkbox keeps its reserved gutter (revealed on hover, pinned in selection). The avatar sits after it; the row's left padding grows by the avatar width plus gap. Wide skin (`is-line`): same slot, 28 px centred on the line, since a single-line row cannot hold 40 px. This is the builder's measured call (see below).

## Where the picture comes from

1. **Contact photo.** `useContacts()` already holds every contact with `hasPhoto`, `cardHash` and addresses. A map from canonical address (`canonicalAddress`, never `fold`) to contact is built once per list. A hit with `hasPhoto` loads `GET /api/Contacts/{id}/Photo` through `requestBlob`, cached under `contactKeys.photo(accountId, id, cardHash)`.
2. **Gravatar.** A new authenticated route, `GET /api/Avatars?address=…`, computes the Gravatar hash server-side, fetches `https://gravatar.com/avatar/<hash>?s=80&d=404` from a fixed host, and returns the image bytes, or 404. It follows no redirect, caps the response size, validates the content type (JPEG/PNG/GIF/WebP), times out, and caches both hits and misses (a miss for 24 h, a hit for 7 days), so a sender is resolved once for all users. Only the lookup string is ever sent to Gravatar, never the user, cookie or IP of the reader.
3. **Initials**, when 1 and 2 fail or while they load.

The browser reaches the route through `requestBlob` (the session cookie does not follow a cross-origin `<img>`), and revokes the object URL with the blob.

## Performance

- Only rows near the viewport request an image (`IntersectionObserver`, margin counted in rows, like `PREFETCH_ROWS`).
- One in-flight request per address; TanStack Query dedupes and caches under an account-scoped key with a long `staleTime`.
- A 404 is cached client-side too, so scrolling back does not re-ask.
- `MessageRow` stays memoised: the avatar's data arrives through its own component and query, not through new props that change per render.
- The server route is rate-limited per user, and refuses addresses that are not syntactically valid.

## Security

- Fixed upstream host: no user-controlled URL, so no SSRF.
- Hash is MD5/SHA-256 of the lower-cased trimmed address, as Gravatar defines; nothing else leaves the server.
- Returned bytes are served with `X-Content-Type-Options: nosniff` and never SVG.
- Disclosure: the user's switch discloses each displayed sender address (hashed) to Gravatar, via the server. The setting's hint says so.

## What's new

An entry in `modules/whatsnew/catalog.ts` (version = `src/frontend/VERSION` bumped in this branch), `items.<id>.title/body` in `locales/{en,fr}/whatsNew.json`, a `WhatsNewDot` on each anchor of the path to the setting and a `WhatsNewTarget` around the new block.

## Tests

- Preferences: defaults, exact-value rule, unknown value, child kept while parent is off.
- Initials and hue: pure helpers, including address-only senders, one-word names, non-Latin names.
- Row: avatar present only when on; cell count still four; `rowLabel` unchanged; draft folder shows the recipient.
- Photo path: contact hit, miss falls back to Gravatar route, 404 falls back to initials.
- Contrast sweep over all palettes for the sender look.
- Backend: route refuses invalid addresses, rejects non-image content types, honours caps and negative cache, never forwards credentials or the client's IP.
- Geometry (avatar column, unchanged row height, wide skin) is verified in `probes/mobile-layout.html`, not jsdom.

## Left to the builder

- Exact avatar size in the wide skin, and the left-padding arithmetic, by measuring in the probe.
- Hue function and the per-theme lightness/saturation constants, fixed by the contrast sweep.
- Cache lifetimes above are proposals; adjust if they prove wrong.
- Whether `/api/Avatars` lives in its own controller or in an existing one.
