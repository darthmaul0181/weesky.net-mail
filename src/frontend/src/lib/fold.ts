/** Diacritics stripped, lower-cased, so an accented query matches a plain value and back. \p{M},
 * not \p{Diacritic}, which also strips ASCII '^' and '`'. */
export function fold(value: string): string {
  return value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
}
