// Only an http(s) unsubscribe opens in a new tab; a mailto: would leave for the OS mail client, so it
// stays in the details grid. The scheme is matched case-insensitively whatever the backend sent.
export function isWebUnsubscribe(url: string | undefined): url is string {
  return !!url && /^https?:\/\//i.test(url)
}
