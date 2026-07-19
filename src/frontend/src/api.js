const BASE = import.meta.env.VITE_API_BASE || 'https://api.mail.weesky.net'
const SESSION_KEY = 'sessionActive'

let unauthorizedHandler = null
// Write-only mirror: AuthContext writes via setIsAdmin, no public getter reads it back (see CLAUDE.md Auth section).
// eslint-disable-next-line no-unused-vars
let isAdmin = false

export function markLoggedIn() {
  localStorage.setItem(SESSION_KEY, '1')
}

export function clearSession() {
  isAdmin = false
  localStorage.removeItem(SESSION_KEY)
}

export function setIsAdmin(value) { isAdmin = value }

export function hasSession() {
  return localStorage.getItem(SESSION_KEY) === '1'
}

export function setUnauthorizedHandler(fn) {
  unauthorizedHandler = fn
}

/**
 * An HTTP failure that keeps its status. The backend puts a stable string in the
 * ResultEnveloppe message — "credentials_unavailable", "Message not found" — which is
 * surfaced as `code` so callers can branch on it without matching prose.
 *
 * Extends Error, so existing `rejects.toThrow(message)` expectations still hold.
 */
export class ApiError extends Error {
  constructor(message, status, code) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

async function readError(res) {
  // Defensive: some callers stub a response with no body reader at all.
  if (typeof res.text !== 'function') return { message: res.statusText ?? '', code: null }

  const text = await res.text().catch(() => '')
  if (!text) return { message: res.statusText ?? '', code: null }

  try {
    const parsed = JSON.parse(text)
    const message = parsed?.message ?? parsed?.Message ?? text
    return { message, code: typeof message === 'string' ? message : null }
  } catch {
    return { message: text, code: null }
  }
}

async function request(method, path, body, options = {}) {
  const headers = {}
  if (body) headers['Content-Type'] = 'application/json'

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    credentials: 'include',
    body: body ? JSON.stringify(body) : undefined,
    signal: options.signal,
  })

  if (res.status === 401) {
    const { code } = await readError(res)
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, code)
  }

  if (res.status === 204) return null

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  return res.json()
}

/**
 * Fetches a binary response — attachments. Separate from request() because that helper always
 * parses JSON.
 */
export async function requestBlob(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    method: 'GET',
    credentials: 'include',
    signal: options.signal,
  })

  if (res.status === 401) {
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, null)
  }

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  const disposition = res.headers?.get?.('content-disposition') ?? ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)

  return { blob: await res.blob(), fileName: match ? decodeURIComponent(match[1]) : 'attachment' }
}

export const api = {
  login: (email, password) =>
    request('POST', '/api/Login', { email, password }),

  logout: () =>
    request('DELETE', '/api/Login'),

  getAccount: () =>
    request('GET', '/api/Account'),

  getQuota: () =>
    request('GET', '/api/Account/Quota'),

  getAliases: () =>
    request('GET', '/api/Aliases'),

  createAlias: (name, domain) =>
    request('POST', '/api/Aliases', { name, domain }),

  deleteAlias: (name, domain) =>
    request('DELETE', '/api/Aliases', { name, domain }),

  changePassword: (oldPassword, newPassword) =>
    request('PATCH', '/api/Account/ChangeSecret', { oldPassword, newPassword }),

  changeFullName: (fullName) =>
    request('POST', '/api/Account/FullName', { fullName }),

  adminGetUsers: () =>
    request('GET', '/api/Admin/users'),

  adminCreateUser: (payload) =>
    request('POST', '/api/Admin/users', payload),

  adminUpdateUser: (id, payload) =>
    request('PUT', `/api/Admin/users/${id}`, payload),

  adminDeleteUser: (id) =>
    request('DELETE', `/api/Admin/users/${id}`),

  adminGetDomains: () =>
    request('GET', '/api/Admin/domains'),

  adminCreateDomain: (payload) =>
    request('POST', '/api/Admin/domains', payload),

  adminUpdateDomain: (id, payload) =>
    request('PUT', `/api/Admin/domains/${id}`, payload),

  adminDeleteDomain: (id) =>
    request('DELETE', `/api/Admin/domains/${id}`),

  adminGetUserQuota: (id) =>
    request('GET', `/api/Admin/users/${id}/quota`),

  adminGetVirtualDomains: () =>
    request('GET', '/api/Admin/domains/virtuals'),

  adminAddVirtualDomainOwner: (domainId, userId) =>
    request('PUT', `/api/Admin/domains/virtuals/${domainId}`, { userId }),

  adminRemoveVirtualDomainOwner: (domainId, userId) =>
    request('DELETE', `/api/Admin/domains/virtuals/${domainId}/${userId}`),

  getFolders: () =>
    request('GET', '/api/Account/Folders'),

  getRuleProviders: () =>
    request('GET', '/api/Rules/Providers'),

  getRules: () =>
    request('GET', '/api/Rules'),

  saveRules: (rules, providerId, scriptName) =>
    request('PUT', '/api/Rules', { rules, providerId, scriptName }),

  deleteRules: () =>
    request('DELETE', '/api/Rules'),

  checkCompatibility: (providerId, rules) =>
    request('POST', '/api/Rules/CompatibilityCheck', { providerId, rules }),

  getRawScript: () =>
    request('GET', '/api/Rules/Raw'),

  saveRawScript: (content, scriptName) =>
    request('PUT', '/api/Rules/Raw', { content, scriptName }),

  // ── Mail ──────────────────────────────────────────────────────────────────
  // Folder paths are encoded: they may contain '/', '&' or '#'.

  getMailFolders: (options) =>
    request('GET', '/api/Mail/Folders', undefined, options),

  createMailFolder: (parentPath, name) =>
    request('POST', '/api/Mail/Folders', { parentPath, name }),

  renameMailFolder: (path, newParentPath, newName) =>
    request('PUT', '/api/Mail/Folders', { path, newParentPath, newName }),

  deleteMailFolder: (path) =>
    request('DELETE', '/api/Mail/Folders', { path }),

  setMailFolderSubscription: (path, subscribed) =>
    request('PUT', '/api/Mail/Folders/Subscription', { path, subscribed }),

  getMailMessages: (folder, page, pageSize, options) =>
    request('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`, undefined, options),

  getMailMessage: (folder, uid, options) =>
    request('GET', `/api/Mail/Messages/Detail?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),
}

/** Builds the attachment download URL. Kept beside the api object so encoding stays in one place. */
export function mailAttachmentUrl(folder, uid, part) {
  return `/api/Mail/Messages/Attachment?folder=${encodeURIComponent(folder)}&uid=${uid}&part=${encodeURIComponent(part)}`
}
