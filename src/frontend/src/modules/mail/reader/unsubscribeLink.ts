/**
 * Only an http(s) unsubscribe can be followed in a new tab. A mailto: needs a composer this
 * webmail does not have yet, so it stays in the details grid rather than leaving for the OS
 * mail client. The scheme is case-insensitive on the wire, whatever the backend normalised.
 */
export function isWebUnsubscribe(url: string | null): boolean {
  return !!url && /^https?:\/\//i.test(url)
}
