/**
 * Plain text into element content: the five characters a browser would otherwise read as markup.
 *
 * The ampersand goes first, or every entity the later replacements produce is escaped a second
 * time and `<` comes out as `&amp;lt;`. One copy for the whole app: two escapers side by side is
 * how one of them quietly stops covering a character the other does.
 */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
