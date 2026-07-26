# Known issues — CSS backgrounds and dark mode

Findings the reviews of the restorable-CSS-backgrounds slice raised and deliberately did not fix
(2026-07-26). Each was measured, not deduced: where a probe was run, its result is quoted. They
are recorded here because none of them lives in a commit — a deferred finding that only exists in
a review transcript is a finding nobody will ever act on.

Design intent for the slice: `docs/superpowers/specs/2026-07-26-webmail-css-background-images-design.md`.

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
a change that looks routine, and which rule 6 of `src/snoopy.microservice/CLAUDE.md` calls
routine — must re-measure dark mode.

## Known and accepted

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
