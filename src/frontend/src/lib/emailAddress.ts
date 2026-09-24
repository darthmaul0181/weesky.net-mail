/** Paint-and-gate check only; the backend's MimeKit parse is the authority. */
export function isValidAddress(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}
