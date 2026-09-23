import DOMPurify from 'dompurify'
import { FORBID_TAGS, FORBID_ATTR } from '../sanitizePolicy'

// Second pass over a body the backend already sanitised, in another engine: a sanitiser falls to a
// parse divergence with the browser, and one engine's divergence does not reproduce in the other.
// The sandboxed iframe is the third barrier.
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

// Blink answers `initial` for a `background:` shorthand with no image. A CSS-wide keyword cannot sit
// in a layer list, so keeping one beside a restored URL would void the whole declaration.
const NO_SURVIVING_LAYER = /^\s*(none|initial|inherit|unset|revert|revert-layer)\s*$/i

// Scheme checked, quotes and backslashes encoded: DOMPurify passes url(javascript:…) untouched,
// so this is the only gate on the client side.
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

// On consent only, before sanitising. A background returns through CSSOM, never appended to the style
// text, where an open `url(` would capture it and a `)` in the URL would inject declarations.
// Layers the cull left (a gradient, a cid: image) go first, withheld ones after: the backend's order.
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

// A document, not a bare fragment, which would take the browser's 8px margin and serif face. Nothing
// here grants the body a capability; the rules only set a floor and contain the two overflows a body
// can inflict on the layout.
export function renderBodyDocument(
  fragment: string, options: { dark?: boolean; narrow?: boolean } = {},
): string {
  // No filter here: darkenColours has already recoloured what the message declares. These are
  // the defaults for what it does not — the sheet behind a message that brings no background,
  // and the text colour of one that names no colour.
  const sheet = options.dark
    ? { scheme: 'dark', background: '#212429', text: '#e0e0e0' }
    : { scheme: 'light', background: '#ffffff', text: '#1a1a1a' }

  // An image is the one thing darkenColours cannot recolour, and the sandboxed cross-origin iframe
  // lets no pixel be read, so the dimming is uniform. The colour toggle undoes it per message.
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
  /* A wide image or a long unbroken URL would scroll the body sideways. Never height:auto: it
     recomputes heights from the intrinsic ratio, and a 1x1 spacer stretched to 154x10 by
     attributes became a 154px tower. */
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
