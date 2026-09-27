# Known issues — CSS backgrounds and dark mode

Findings the reviews of the restorable-CSS-backgrounds slice raised and deliberately did not fix
(2026-07-26). Each was measured, not deduced: where a probe was run, its result is quoted. They
are recorded here because none of them lives in a commit — a deferred finding that only exists in
a review transcript is a finding nobody will ever act on.

Design intent for the slice: `docs/history/specs/2026-07-26-webmail-css-background-images-design.md`.

The message-stylesheets slice (2026-09-27, `docs/history/specs/2026-09-27-webmail-mail-stylesheets-design.md`)
added the entries on quoted classes, escaped font names, stylesheet backgrounds, remote fonts and
media, and the CSSOM round trip.

## Worth fixing

### Forwarding a message whose background is a `cid:` image sends a dead background

`QuotePreparer.cs` says in its own comment that it leaves no `cid:` in the quotable body, and its
loop walks `img` elements only. A `background-image: url(cid:logo@mail)` — which the reader now
displays, since the slice resolves those into data URIs — is carried through untouched and its
part is never staged, so the recipient gets a background pointing at nothing.

Pre-existing (that path runs the outgoing sanitiser, never the display one). The slice is what
makes it reachable, by making such backgrounds render in the first place.

### `darkenColours` can rewrite a URL that spells a colour

`COLOUR_PROPERTIES` matches `([^;]+)` after a colour property name, and a `;` is legal in a URL
path. Measured: `data-blocked-bg="https://x.example/a;color:#fff;b.png"` comes out of the dark
pass as `url("https://x.example/a;color: #212121;b.png")` — the URL is rewritten and the image
404s, **in dark mode only**. No injection: the replacement writes a bare colour.

The gradient work added later dodges this for `background-image` values by consuming `url(...)`
before any colour token can match. The colour-property path still has it.

### The CSS allowlist is one entry away from breaking dark backgrounds

`background-position`/`-repeat` are allowed as their `-x`/`-y` longhands. Adding
`background-attachment`, `-origin` or `-clip` would complete the set the engine needs to
re-collapse the longhands into a `background:` shorthand — which `darkenColours` deliberately
skips, since a shorthand is not a colour. Measured in Blink: with all eight present the
serialisation collapses and the background colour stops being darkened.

Today the collapse cannot happen because those three are absent. Anyone widening the allowlist —
a change that looks routine, and which rule 6 of `src/scotty.microservice/CLAUDE.md` calls
routine — must re-measure dark mode.

### A quoted message's classes reach the composer

`PrepareQuote` keeps `class` (the outgoing sanitiser's Ganss defaults) and `SquireEditor`'s
DOMPurify keeps it too, so a quoted `class="header"` can pick up a style of the webmail's own
inside the editor, which is a plain div in the SPA document. Pre-existing and independent of the
stylesheet work; not fixed there because Squire relies on its own classes to format.

### An escaped slash beside a parenthesis in a string loses a font declaration

`.x { font-family: "a(", b\/c }` passes the backslash cull (`\/` is a safe escape), but Ganss
writes `b\/c` back as `b/c` and the whole declaration is lost in the round trip. The sheet is
kept, only that font list falls back. Rare in real mail; no safety impact.

### A stylesheet `background:` shorthand comes back as `initial` longhands

AngleSharp serialises every `background:` shorthand as its longhands, `url()` or not:
`.x { background: red }` comes back as `background-image: initial; background-position: initial;
…; background-color: …`, declarations the sender never wrote. They reset exactly what the
shorthand resets, so the render is unchanged — harmless. The `url()` cull is not the cause: it
only removes the image longhand from a sheet that already carried the others.

## Known and accepted

- **About 5 % of HTML mail still scrolls sideways on a phone, and the reader does not zoom it to
  fit.** Measured 2026-09-27 at 412px (a Galaxy S26) over two real mailboxes, 323 HTML messages:
  24 overflowed (7.4 %). Two causes remain after the stylesheet work. A template with no mobile
  rules at all (a Stripe receipt fixed at 480px, a railway ticket at ~940px) is wider than the
  screen by design; Outlook and Gmail shrink such a message to fit, but the zoom it needs was
  under 0.7 for all but 3 of the 24, where text is small enough that the reader pinches anyway —
  so zoom-to-fit (a same-origin, scriptless measuring frame beside the reader, plus an "original
  size" toggle) was judged not worth its cost and its relaxation of the frame barrier. And a long
  URL or address written as text inside a table cell (gov.uk, Hetzner, OVH, Moody's) cannot wrap:
  `overflow-wrap: break-word` on the body does not lower a cell's minimum width, and `anywhere`
  everywhere collapses real columns (see the GitHub case in `architecture-mail.md`). Only a
  message that asks for it with `word-break: break-word` gets `anywhere`, through the backend's
  `data-break-word` marker — that fixed ING and one OVH mail. Pinch-zoom on the page is the
  fallback; revisit if the proportion grows.
- **The dark-mode image dimming is uniform and covers `<img>` only.** `brightness(0.85)
  saturate(0.9)`, applied in `renderBodyDocument`. It cannot be content-aware: the reader's iframe
  is sandboxed without same-origin and the images are cross-origin, so no pixel can be read to
  tell a banner from a photograph. CSS background images are not dimmed at all, so a logo set as a
  `background-image` keeps its brightness. The reader's per-message colour toggle undoes the whole
  dark pass, which is what makes a light touch the right one.
- **`CullEscapedDeclarations` fails closed and takes neighbours with it.** A style carrying a
  backslash it cannot tokenise loses its other declarations too. Failing closed is the right side
  to fail on for a security cull; display-only.
- **The backend's `cid:` test is a plain prefix test.** `url("cid:x@y/../https://evil/z.png")` can
  sit in the CSS. No fetch — `cid:` is unresolvable in a browser — and the client resolves
  Content-IDs by exact map lookup, never by substring. **That exactness is load-bearing**: it is
  what stops the string above from ever becoming a real URL.
- **`data-blocked-bg` records no layer position.** Surviving layers are rebuilt first and withheld
  ones after, so a message whose remote layer came first comes back with its stack reordered.
  Cosmetic, and invisible on every shape the backend can currently emit.
- **`revealBlockedImages` is not idempotent** — a second reveal duplicates the restored layer.
  Unreachable: `MessageReader` memoises on the raw `htmlBody`, never on its own output.
- **The byte-identical fast path keys on a substring.** A body merely mentioning `data-blocked-bg`
  in its text pays a DOM round trip and can come back re-serialised. Performance only.
- **A `var(--x)` survivor invalidates its declaration** at computed-value time, so both layers
  vanish. An undefined custom property is not something the restore can rescue.
- **The whitespace split tears a URL containing a tab or newline** into fragments. No injection:
  every fragment still faces `new URL` and the scheme gate.
- **`background-position: right 10px bottom 20px`** (the four-value syntax) is dropped whole by
  AngleSharp — it does not survive as `-x`/`-y` longhands.
- **A bare `url(cid:a)b@x)` yields the truncated cid `a`.** The whole declaration is culled in
  practice, so nothing renders either way; noted because a truncated cid could hide the wrong
  attachment chip if the culling ever loosened.
- **Two branches of `NO_SURVIVING_LAYER` are untested**: `revert-layer` was verified by hand in
  both engines but is absent from the parametrised list, and jsdom cannot reach the `none` branch
  through the DOM.
- **A remote font does not raise the blocked-images banner.** A sheet has no consent surface of
  its own: a `@font-face` is not counted in `blockedImageCount`, only held back by the frame's
  CSP. A message with no remote image keeps its fallback font unless images are always shown or
  the sender is trusted.
- **Remote `<video>` and `<audio>` stay blocked even after consent.** The frame's CSP opens on
  `default-src 'none'` and consent widens `img-src` and `font-src` only, so `media-src` stays
  closed. Mail clients rarely play either; widening it would make consent cover more than the
  banner says.
- **A stylesheet holding an unsafe escape is removed whole.** A backslash not followed by one of
  the safe punctuation characters (CJK font names written as hex escapes are the realistic case)
  drops the entire `<style>`, and the message renders as it did before stylesheets were kept —
  desktop layout, no dark design. Three hand-written tokenisers each closed one bypass and opened
  another; failing closed is the price of not having a fourth.
- **Recolouring a light message's stylesheet round-trips it through the browser's CSSOM.** Rules
  and declarations the engine does not understand are dropped on the way back to text. They were
  inert in that engine anyway; the loss is only in what another engine might have made of them.
