const LOGIN = '/login'
const SINGLE_SLASH = /^\/(?![/\\])/

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

/** Where to go after signing in: the location `RequireAuth` remembered in the router state, else
 * home. The path is resolved against this origin and the normalised result is what gets checked
 * and returned: a same-origin path starting with a single slash, never the login page. */
export function returnPathOf(state: unknown): string {
  const from = (state as { from?: unknown } | null)?.from as
    { pathname?: unknown; search?: unknown; hash?: unknown } | undefined
  const { pathname, search = '', hash = '' } = from ?? {}
  if (!isString(pathname) || !isString(search) || !isString(hash)) return '/'
  if (!pathname.startsWith('/') || (search && !search.startsWith('?')) || (hash && !hash.startsWith('#'))) return '/'

  const origin = window.location.origin
  let url: URL
  try {
    url = new URL(pathname + search + hash, origin)
  } catch {
    return '/'
  }
  const path = url.pathname + url.search + url.hash
  if (url.origin !== origin || url.pathname.replace(/\/+$/, '').toLowerCase() === LOGIN || !SINGLE_SLASH.test(path)) return '/'
  return path
}
