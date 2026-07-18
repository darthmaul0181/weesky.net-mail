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

async function request(method, path, body) {
  const headers = {}
  if (body) headers['Content-Type'] = 'application/json'

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    credentials: 'include',
    body: body ? JSON.stringify(body) : undefined,
  })

  if (res.status === 401) {
    clearSession()
    unauthorizedHandler?.()
    throw new Error('Unauthorized')
  }

  if (res.status === 204) return null
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new Error(text || res.statusText)
  }
  return res.json()
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
}
