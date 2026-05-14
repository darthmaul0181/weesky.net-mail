const BASE = import.meta.env.VITE_API_BASE || 'https://api.mail.weesky.net'
const TOKEN_KEY = 'authToken'
const EXPIRY_KEY = 'authExpiry'

let authToken = null
let unauthorizedHandler = null
let isAdmin = false

// Restore persisted token only if not expired
const savedToken = localStorage.getItem(TOKEN_KEY)
const savedExpiry = localStorage.getItem(EXPIRY_KEY)
if (savedToken && savedExpiry && Date.now() < Number(savedExpiry)) {
  authToken = savedToken
} else {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(EXPIRY_KEY)
}

export function setToken(token, expiresIn, persist = false) {
  authToken = token
  if (persist) {
    localStorage.setItem(TOKEN_KEY, token)
    localStorage.setItem(EXPIRY_KEY, String(Date.now() + expiresIn * 60 * 1000))
  } else {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(EXPIRY_KEY)
  }
}

export function clearToken() {
  authToken = null
  isAdmin = false
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(EXPIRY_KEY)
}

export function setIsAdmin(value) { isAdmin = value }
export function getIsAdmin() { return isAdmin }

export function hasToken() {
  return !!authToken
}

export function setUnauthorizedHandler(fn) {
  unauthorizedHandler = fn
}

async function request(method, path, body) {
  const headers = {}
  if (body) headers['Content-Type'] = 'application/json'
  if (authToken) headers['Authorization'] = `Bearer ${authToken}`

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined,
  })

  if (res.status === 401) {
    clearToken()
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
    request('POST', '/api/BearerAuthenticator', { email, password }),

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

  adminGetOwnerships: () =>
    request('GET', '/api/Admin/ownerships'),

  adminSetOwnership: (domainId, userId) =>
    request('PUT', `/api/Admin/ownerships/${domainId}`, { userId }),

  adminDeleteOwnership: (domainId) =>
    request('DELETE', `/api/Admin/ownerships/${domainId}`),
}
