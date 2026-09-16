# CSS background images — withheld like an `<img>`, not thrown away

A remote image declared in CSS becomes restorable on the same consent that restores an `<img>`,
instead of being deleted before the reader ever gets the chance to ask for it.

## The problem

The display sanitiser culls every CSS declaration carrying `url(`, because `background-image` is
allowed for gradients and a `url()` there would fetch a remote asset without consent — the whole
image-blocking model, bypassed through a property name. The cull is right; its bluntness is not.

An Anthropic receipt renders in this webmail with a hole where the sender's logo should be. The
markup we deliver explains why:

```
DIV — border-radius: 100% | width: 32px | height: 32px | background-size: contain | background-color: initial
```

A 32px circle that declares how to frame an image and carries no image. The declaration naming it
was deleted, so nothing — not "Show images", not `mail.alwaysShowImages`, not a trusted sender —
can bring it back: the URL is not in the document any more. Rainloop shows the logo; we cannot.

The same rule culls by substring, so a declaration merely containing the letters `url` falls with
it: `font-family: Curly` is dropped, measured against the shipped sanitiser.

## What this is not

**Not a loosening of the consent model.** Nothing loads before the reader agrees. The URL simply
survives, inert, in the place `data-blocked-src` already occupies for `<img>`.

**Not an image proxy.** Fetching the image server-side would answer the question the consent
exists to ask, on the reader's behalf, before they answered it.

**Not a widening to every property that can carry a `url()`.** `list-style-image`, `border-image`,
`mask-image` and `cursor` stay culled. Each one is a distinct value grammar to parse and rebuild,
and mail does not use them; `background` is what carries logos and hero images.

## Where the withholding lives

**Server-side, like the `<img>` blocking it mirrors.** The guarantee "a body served without
consent carries no remote URL" must not become a client policy — and `blockedImageCount`, which
the banner counts with, is computed there.

## The server rule

**Only `background-image` ever reaches this pass, and that is measured, not assumed.** Ganss runs
first and re-serialises what it keeps: `background: url(x) center / contain no-repeat #fff` comes
out of it as separate longhands, the image among them. The rule therefore never has to take a
shorthand apart — there is no shorthand left to take apart.

Over `[style]` attributes, on `background-image` declarations:

| `url()` target | What happens |
|---|---|
| `cid:…` | Left in place. It cannot reach the network; the client resolves it. |
| `http(s):…` | The `url(...)` token is removed from the value; the URL moves to `data-blocked-bg`; `blockedImageCount` grows by one per URL. |
| anything else | Culled as today, no attribute. Nothing to restore is nothing to validate later. |

**`data:` is not in this table, because it never gets here.** Ganss's scheme allowlist is `http`,
`https`, `mailto`, `cid`, and it applies to CSS `url()` as it does to `src`: measured, both
`background-image: url(data:…)` and `<img src="data:…">` lose their URL before our pass runs. A
data URI carries no fetch and no privacy question, so nothing is lost by leaving it there — but
the client must not be written as though one could arrive.

Removing the token rather than the declaration is what saves a value that mixes layers:
`background-image: linear-gradient(…), url(x)` keeps its gradient and withholds its image, where
today both die together. Several withheld layers are kept in order and restored together. A
declaration left with nothing but its property name is dropped rather than emitted valueless.

A `background-image` declaration carrying a backslash is culled outright, attribute and all,
exactly like any other property: an escape can spell a second function the withholding never sees,
and this row is not worth an exception.

Every other property keeps the current rule, narrowed to the **function**: a declaration is culled
when it contains `url(`, no longer when it merely contains the letters `url`. The backslash test
stays exactly as it is — it is the escape route back to the same function (`= rl(`).

## The client restore

`revealBlockedImages` gains a second gesture, guarded by a substring test so a body without
withheld backgrounds costs nothing: when `data-blocked-bg` is present, a DOM pass **appends**
`background-image: url("…")` to the element's `style`.

Appending rather than rebuilding the shorthand is the load-bearing choice: the last declaration
wins, so the image is restored without ever re-assembling a value we did not parse.

The URL is validated again here — `new URL`, protocol in `http:`/`https:` — and its quotes and
backslashes are percent-encoded before entering the `url()`, so a forged URL cannot close the
function and append declarations of its own.

`sanitizeBody` must list `data-blocked-bg` in `ADD_ATTR`, or DOMPurify strips it before the
restore can read it — the same reason `data-blocked-src` is listed.

**DOMPurify protects nothing here, and the design does not pretend otherwise.** Measured against
the version this project ships: `<div style="background-image: url(javascript:alert(1))">` comes
back through the sanitiser unchanged. Both validations are explicit, and neither delegates.

## `cid:` backgrounds

A background pointing at the message's own attachment needs no consent — the bytes never leave the
mailbox, exactly as for `<img src="cid:">`, which the reader already inlines as a data URI.

Two pure helpers grow to see them: `referencedCids` must scan `style` attributes for `url(cid:…)`
alongside `img[src]`, and `substituteInlineImages` must rewrite those `url()` values to the data
URI it already builds.

`bodyInlineParts` therefore reports a part used only as a background, so the attachment row
withholds its chip. That is the rule stated when the row learned to hide body-displayed parts, and
it keeps holding.

## Testing

Server: `font-family: Curly` survives the narrowed rule; a remote background is withheld and
counted; a gradient sharing the declaration survives beside the withheld layer; a `cid:`
background is left intact; an unknown scheme is culled and leaves no attribute; multiple layers
round-trip in order; a message arriving with its own `data-blocked-bg` cannot keep it, since the
attribute is absent from Ganss's allowlist and only our own pass may create one.

Client: `ADD_ATTR` keeps the attribute; consent produces a valid `background-image`; a URL forged
with a quote and a parenthesis cannot escape the `url()`; a `cid:` background resolves to a data
URI; a part used only as a background carries no attachment chip.

## Out of scope

The other `url()`-bearing properties, `<style>` elements (still dropped with their content), and
any form of server-side image proxying.
