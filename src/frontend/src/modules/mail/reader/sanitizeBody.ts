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
