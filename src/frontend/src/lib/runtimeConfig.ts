/** What /config.js left in window.SCOTTY_CONFIG, checked once. Never throws: api.ts reads it while
    the module graph loads, and a throw there would leave a blank page instead of the error screen. */
export type RuntimeConfig = { apiBase: string } | { error: string }

export function parseRuntimeConfig(config: unknown): RuntimeConfig {
  if (typeof config !== 'object' || config === null) {
    return { error: 'Configuration missing: /config.js did not define window.SCOTTY_CONFIG.' }
  }
  const value = (config as { apiBase?: unknown }).apiBase
  if (value === undefined) return { apiBase: '' }
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (trimmed === '') return { apiBase: '' }
    if (isApiBase(trimmed)) return { apiBase: trimmed.replace(/\/+$/, '') }
  }
  return {
    error: `Invalid API address in /config.js: ${JSON.stringify(value)}. ` +
      'Expected an http:// or https:// address, or "" for this site\'s own address.',
  }
}

// Every route is `${base}/api/...`: a query, a fragment or credentials would end up mid-URL.
function isApiBase(value: string): boolean {
  if (!/^https?:\/\//i.test(value)) return false
  try {
    const url = new URL(value)
    return !url.search && !url.hash && !url.username && !url.password
  } catch {
    return false
  }
}

/** Plain text, no translations, no API: this runs exactly when neither can be relied on. */
export function showConfigError(root: HTMLElement, message: string): void {
  const text = document.createElement('p')
  text.style.cssText = 'padding:2rem;font:16px system-ui'
  text.textContent = message
  root.replaceChildren(text)
}

export const runtimeConfig = parseRuntimeConfig(window.SCOTTY_CONFIG)
export const configuredApiBase = 'apiBase' in runtimeConfig ? runtimeConfig.apiBase : ''
