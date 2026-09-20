/** A CardDAV `URL` value comes from whatever client last touched the card, and a
    `javascript:`/`data:` value would run as-is, or a scheme-less `example.com` would
    resolve as a relative link — only a normalised http(s) URL ever reaches an `href`. */
export function websiteHref(value: string): string | null {
  const trimmed = value.trim()
  if (!trimmed) return null
  // `(?!\d)` excludes a port (`example.com:8080`) from being read as a scheme: a digit right
  // after the colon is never a valid scheme's first character.
  const candidate = /^[a-z][a-z\d+.-]*:(?!\d)/i.test(trimmed) ? trimmed : `https://${trimmed}`
  try {
    const url = new URL(candidate)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}
