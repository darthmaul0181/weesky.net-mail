import DOMPurify from 'dompurify'

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
    ADD_ATTR: ['data-blocked-src', 'target'],
    FORBID_TAGS: ['style', 'script', 'iframe', 'object', 'embed', 'form', 'base', 'link'],
    FORBID_ATTR: ['srcset', 'formaction', 'ping'],
  })
}

/**
 * Restores remote images, on explicit user consent only. Runs before sanitising, so the
 * restored URLs are subject to the same pass as everything else.
 */
export function revealBlockedImages(html: string): string {
  return html.replace(/data-blocked-src=/g, 'src=')
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
export function renderBodyDocument(fragment: string): string {
  return `<!doctype html>
<html>
<head><meta charset="utf-8"><style>
  /* Light, explicitly, in every theme. Mail HTML is written against a white canvas — dark
     mode would leave a body's own colours unreadable against an inverted background, so the
     reader keeps a light sheet the way every other mail client does. */
  :root { color-scheme: light; }
  body {
    margin: 0;
    padding: 18px 22px;
    background: #ffffff;
    color: #1a1a1a;
    font: 14px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  }
  /* A wide image or a long unbroken URL — this mailbox is largely made of the latter — would
     otherwise scroll the body sideways. No height:auto - it recomputes every height from the
     intrinsic ratio, and a 1x1 spacer gif stretched to 154x10 by attributes became 154px tall,
     turning a newsletter button into a tower. */
  img { max-width: 100%; }
  body { overflow-wrap: anywhere; }
  /* Tables are the one thing that must keep its width, so it scrolls in its own box. */
  table { max-width: 100%; }
  pre { overflow-x: auto; }
</style></head>
<body>${fragment}</body>
</html>`
}
