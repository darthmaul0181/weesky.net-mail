// Shared by the reader's iframe and the composer's editable div so paste and render cannot drift.
// The composer is the more dangerous one: a plain div in the SPA document, not a sandboxed iframe.
export const FORBID_TAGS = ['style', 'script', 'iframe', 'object', 'embed', 'form', 'base', 'link']
export const FORBID_ATTR = ['srcset', 'formaction', 'ping']
