/** Plain text into element content. The ampersand goes first, or the later entities are escaped
 * twice (`&amp;lt;`). One copy app-wide, so no escaper quietly misses a character. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
