// Shared by the reader's iframe and the composer's editable div so paste and render cannot drift.
// The composer is the more dangerous one: a plain div in the SPA document, not a sandboxed iframe,
// which is why it alone forbids <style> — in the reader a message's stylesheet stays inside the frame.
const FORBID_ANYWHERE = ['script', 'iframe', 'object', 'embed', 'form', 'base', 'link'] as const
export const READER_FORBID_TAGS: readonly string[] = FORBID_ANYWHERE
export const COMPOSER_FORBID_TAGS: readonly string[] = ['style', ...FORBID_ANYWHERE]
export const FORBID_ATTR: readonly string[] = ['srcset', 'formaction', 'ping']
