/**
 * DOMPurify additions shared by every surface that turns mail HTML into DOM: the reader's
 * iframe body and the composer's editable div. One policy, so paste and render cannot drift
 * into two dialects — the composer is the more dangerous surface, since it is a plain div in
 * the SPA document rather than a scriptless cross-origin iframe.
 */
export const FORBID_TAGS = ['style', 'script', 'iframe', 'object', 'embed', 'form', 'base', 'link']
export const FORBID_ATTR = ['srcset', 'formaction', 'ping']
