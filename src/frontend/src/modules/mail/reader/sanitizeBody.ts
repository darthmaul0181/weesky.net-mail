import DOMPurify from 'dompurify'
import { FORBID_TAGS, FORBID_ATTR } from '../sanitizePolicy'

/**
 * Client-side pass over a body the backend already sanitised.
 *
 * This is not redundancy for its own sake: the two passes use different parsers in different
 * engines. The class of bug that defeats an HTML sanitiser is a parse divergence — the
 * sanitiser builds one tree, the browser builds another, and script survives in the gap
 * (GHSA-pgww-w46g-26qg was exactly that, in the backend's parser). A divergence in one engine
 * does not reproduce in the other, so a body has to defeat both to reach the iframe.
 *
 * The iframe sandbox is a third, independent barrier: it cannot run scripts at all.
 */
export function sanitizeBody(html: string): string {
  if (!html) return ''

  return DOMPurify.sanitize(html, {
    // data-blocked-src carries the withheld remote image URL; DOMPurify would strip an
    // unknown data attribute otherwise, and the "show images" action would have nothing left
    // to restore.
    ADD_ATTR: ['data-blocked-src', 'data-blocked-bg', 'target'],
    FORBID_TAGS,
    FORBID_ATTR,
  })
}

const BLOCKED_BACKGROUND = 'data-blocked-bg'

/**
 * What `background-image` answers when nothing survives the cull. `initial` is the common one:
 * Blink answers it — not '' and not `none` — for a `background:` shorthand that declares no
 * image, which is how mail is routinely written and what the backend's longhand-only rule leaves
 * behind. A CSS-wide keyword cannot sit in a layer list, so listing one beside the restored URL
 * voids the whole declaration and consent restores nothing at all.
 */
const NO_SURVIVING_LAYER = /^\s*(none|initial|inherit|unset|revert|revert-layer)\s*$/i

/**
 * A withheld URL is only ever re-entered inside url("…") after its scheme is checked and its
 * quotes and backslashes are encoded: DOMPurify validates neither — measured, it passes
 * url(javascript:…) straight through — so this is the only gate on the client side.
 */
function restorable(raw: string): string | null {
  let parsed: URL
  try {
    parsed = new URL(raw)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href.replace(/["\\]/g, encodeURIComponent)
}

/**
 * Restores remote images, on explicit user consent only. Runs before sanitising, so the
 * restored URLs are subject to the same pass as everything else.
 *
 * A background comes back through CSSOM, never by concatenating onto the style text: an open
 * construct already in that attribute — `a: url(` — captures appended text, and a `)` in the
 * withheld URL closes it, after which the rest parses as declarations of the forger's choosing.
 * Neither `)` nor `;` is percent-encoded by a URL parser, so assigning the property and letting
 * the browser reserialise the attribute is what closes that door.
 *
 * The attribute records no layer position, so the layers the cull left in the CSS (a gradient, a
 * cid: image Task 4 will resolve) are carried into the new value first and the withheld ones
 * after: that is the order the backend's own cases have, and it is the only way consent does not
 * make a surviving gradient disappear.
 */
export function revealBlockedImages(html: string): string {
  const revealed = html.replace(/data-blocked-src=/g, 'src=')
  if (!revealed.includes(BLOCKED_BACKGROUND)) return revealed

  const doc = new DOMParser().parseFromString(revealed, 'text/html')
  for (const element of doc.querySelectorAll<HTMLElement>(`[${BLOCKED_BACKGROUND}]`)) {
    const layers = (element.getAttribute(BLOCKED_BACKGROUND) ?? '')
      .split(/\s+/)
      .map(restorable)
      .filter((url): url is string => url !== null)
    if (layers.length === 0) continue

    const kept = element.style.backgroundImage
    const restored = layers.map(url => `url("${url}")`).join(', ')
    // Removed first so the declaration is re-added last: a `background:` shorthand in the same
    // attribute otherwise outranks the longhand once the attribute is serialised again.
    element.style.removeProperty('background-image')
    element.style.backgroundImage =
      kept === '' || NO_SURVIVING_LAYER.test(kept) ? restored : `${kept}, ${restored}`
  }
  return doc.body.innerHTML
}

/**
 * Wraps the sanitised fragment in a document, because a bare fragment in srcDoc inherits the
 * browser's defaults: an 8px body margin that left the message text visibly out of line with
 * the header above it, and a serif face for any body that brings no styles of its own.
 *
 * Everything here is our own markup with the sanitised fragment as its only variable part, and
 * none of it grants the body a capability — the sandbox still withholds scripts and same-origin.
 *
 * The rules are deliberately few. Message HTML carries its own styling and is entitled to it;
 * these set a floor for bodies that have none, and contain the two overflows that a body can
 * inflict on the layout regardless of its own intent.
 */
export function renderBodyDocument(
  fragment: string, options: { dark?: boolean; narrow?: boolean } = {},
): string {
  // No filter here: darkenColours has already recoloured what the message declares. These are
  // the defaults for what it does not — the sheet behind a message that brings no background,
  // and the text colour of one that names no colour.
  const sheet = options.dark
    ? { scheme: 'dark', background: '#212429', text: '#e0e0e0' }
    : { scheme: 'light', background: '#ffffff', text: '#1a1a1a' }

  // An image is the one thing darkenColours cannot recolour, so a pale banner blazes on the dark
  // canvas. The dimming is uniform because it has to be: the iframe is cross-origin and sandboxed,
  // so no pixel can be read to tell a banner from a photograph. The reader's colour toggle undoes
  // it for one message, which is what makes a light touch the right one.
  const images = options.dark ? 'filter: brightness(0.85) saturate(0.9);' : ''

  // 44px of side margin out of a 360px screen is a lot to spend on nothing.
  const padding = options.narrow ? '12px 14px' : '18px 22px'
  // iOS reflows a document's font sizes on its own unless told the scale is deliberate.
  const scale = options.narrow ? '-webkit-text-size-adjust: 100%; text-size-adjust: 100%;' : ''

  return `<!doctype html>
<html>
<head><meta charset="utf-8"><style>
  :root { color-scheme: ${sheet.scheme}; }
  html { background: ${sheet.background}; }
  body {
    margin: 0;
    padding: ${padding};
    background: ${sheet.background};
    color: ${sheet.text};
    font: 14px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
    ${scale}
  }
  /* A wide image or a long unbroken URL — this mailbox is largely made of the latter — would
     otherwise scroll the body sideways. No height:auto - it recomputes every height from the
     intrinsic ratio, and a 1x1 spacer gif stretched to 154x10 by attributes became 154px tall,
     turning a newsletter button into a tower. */
  img { max-width: 100%; ${images} }
  /* break-word, not anywhere: both break a long URL, but anywhere also feeds those break
     points into min-content sizing, so a table column can collapse to a single letter. */
  body { overflow-wrap: break-word; }
  /* Tables are the one thing that must keep its width, so it scrolls in its own box. */
  table { max-width: 100%; }
  pre { overflow-x: auto; }
</style></head>
<body>${fragment}</body>
</html>`
}
