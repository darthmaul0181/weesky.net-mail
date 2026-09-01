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

// Shared by readError (fetch) and uploadAttachment (XHR) — same envelope shape, different
// transport for the raw text.
function parseErrorEnvelope(text, fallbackMessage) {
  if (!text) return { message: fallbackMessage ?? '', code: null }
  try {
    const parsed = JSON.parse(text)
    const message = parsed?.message ?? parsed?.Message
    if (typeof message === 'string') return { message, code: message }

    // A model-validation refusal answers ProblemDetails, not our envelope. Its title is the
    // only readable line in it, and it carries no stable code — falling through to the raw
    // text printed the whole JSON blob on screen.
    return { message: parsed?.title ?? fallbackMessage ?? text, code: null }
  } catch {
    return { message: text, code: null }
  }
}

async function readError(res) {
  // Defensive: some callers stub a response with no body reader at all.
  if (typeof res.text !== 'function') return { message: res.statusText ?? '', code: null }

  const text = await res.text().catch(() => '')
  return parseErrorEnvelope(text, res.statusText ?? '')
}

// 'primary' means the same as no id at all — the backend defaults there — so only a genuinely
// connected account changes what travels over the wire. The one place this is decided.
function carriesAccount(accountId) {
  return Boolean(accountId) && accountId !== 'primary'
}

async function request(method, path, body, options = {}) {
  // FormData carries its own multipart boundary; naming a content type here breaks the parse on
  // the server side.
  const isForm = typeof FormData !== 'undefined' && body instanceof FormData
  const headers = {}
  if (body && !isForm) headers['Content-Type'] = 'application/json'
  if (carriesAccount(options.accountId)) headers['X-Account-Id'] = options.accountId

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    credentials: 'include',
    body: body ? (isForm ? body : JSON.stringify(body)) : undefined,
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
  const headers = {}
  if (carriesAccount(options.accountId)) headers['X-Account-Id'] = options.accountId

  const res = await fetch(`${BASE}${path}`, {
    method: 'GET',
    headers,
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

  // Every field is optional and an older backend answers 404 — callers treat both as "no
  // capabilities", not as an error.
  getCapabilities: () =>
    request('GET', '/api/Capabilities'),

  // 204 when the IMAP server advertises no QUOTA capability — request() already resolves that to
  // null before attempting to parse a body.
  getQuota: () =>
    request('GET', '/api/Account/Quota'),

  getAliases: () =>
    request('GET', '/api/Aliases'),

  getIdentities: (options) =>
    request('GET', '/api/Identities', undefined, options),

  // Replaces the whole set: the payload is the list, not a delta.
  putIdentities: (identities, options) =>
    request('PUT', '/api/Identities', { identities }, options),

  getContacts: () =>
    request('GET', '/api/Contacts'),

  // The whole card, which the list does not carry: only the open contact pays for it.
  getContact: (id) =>
    request('GET', `/api/Contacts/${id}`),

  // A blob, not a URL handed to <img>: the picture sits behind the session cookie on another
  // origin, which an image element cannot send.
  getContactPhoto: (id) =>
    requestBlob(`/api/Contacts/${id}/Photo`).then(({ blob }) => blob),

  createContact: (contact) =>
    request('POST', '/api/Contacts', contact),

  // Replaces the contact whole — names, favourite flag and the entire address list.
  updateContact: (id, contact) =>
    request('PUT', `/api/Contacts/${id}`, contact),

  deleteContact: (id) =>
    request('DELETE', `/api/Contacts/${id}`),

  // Its own route: the star is toggled from a tile holding a possibly stale copy, so a whole
  // contact PUT from there would clobber a concurrent edit.
  setContactFavorite: (id, isFavorite) =>
    request('PUT', `/api/Contacts/${id}/Favorite`, { isFavorite }),

  // The batch travels in the body rather than the URL: a list of ids in a query string breaks past
  // a few dozen and has no agreed shape. Capped at 200 server-side, the mail's own batch size.
  deleteContacts: (ids) =>
    request('DELETE', '/api/Contacts', { ids }),

  setContactsFavorite: (ids, isFavorite) =>
    request('PUT', '/api/Contacts/Favorite', { ids, isFavorite }),

  importContacts: (file) => {
    const form = new FormData()
    form.append('file', file)
    return request('POST', '/api/Contacts/Import', form)
  },

  exportContacts: () => requestBlob('/api/Contacts/Export'),

  getContactGroups: () => request('GET', '/api/ContactGroups'),

  createContactGroup: (name) => request('POST', '/api/ContactGroups', { name }),

  renameContactGroup: (id, name) => request('PUT', `/api/ContactGroups/${id}`, { name }),

  deleteContactGroup: (id) => request('DELETE', `/api/ContactGroups/${id}`),

  addContactGroupMembers: (id, contactIds) =>
    request('POST', `/api/ContactGroups/${id}/Members`, { contactIds }),

  removeContactGroupMembers: (id, contactIds) =>
    request('DELETE', `/api/ContactGroups/${id}/Members`, { contactIds }),

  // The address comes from the backend's configuration, never composed here: the URL this app
  // calls is not necessarily the one the proxy publishes.
  getDavCredentials: () =>
    request('GET', '/api/DavCredentials'),

  // Turning it on for the first time answers the secret in this very response — the only moment
  // it exists in clear.
  setDavCardDav: (enabled) =>
    request('PUT', '/api/DavCredentials/CardDav', { enabled }),

  regenerateDavSecret: () =>
    request('POST', '/api/DavCredentials/Regenerate'),

  getTrustedSenders: () =>
    request('GET', '/api/TrustedSenders'),

  trustSender: (address) =>
    request('POST', '/api/TrustedSenders', { address }),

  // The address travels in the query string, so it is encoded here rather than at call sites.
  untrustSender: (address) =>
    request('DELETE', `/api/TrustedSenders?address=${encodeURIComponent(address)}`),

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

  // deleteAliases acknowledges the cascade: the aliases anchored on the domain go with it. Sent
  // only once the user has confirmed, so an unacknowledged call is refused server-side too.
  adminDeleteDomain: (id, deleteAliases = false) =>
    request('DELETE', `/api/Admin/domains/${id}${deleteAliases ? '?deleteAliases=true' : ''}`),

  adminGetUserQuota: (id) =>
    request('GET', `/api/Admin/users/${id}/quota`),

  adminGetVirtualDomains: () =>
    request('GET', '/api/Admin/domains/virtuals'),

  adminAddVirtualDomainOwner: (domainId, userId) =>
    request('PUT', `/api/Admin/domains/virtuals/${domainId}`, { userId }),

  adminRemoveVirtualDomainOwner: (domainId, userId) =>
    request('DELETE', `/api/Admin/domains/virtuals/${domainId}/${userId}`),

  getRuleProviders: (options) =>
    request('GET', '/api/Rules/Providers', undefined, options),

  getRules: (options) =>
    request('GET', '/api/Rules', undefined, options),

  saveRules: (rules, providerId, scriptName, options) =>
    request('PUT', '/api/Rules', { rules, providerId, scriptName }, options),

  deleteRules: (options) =>
    request('DELETE', '/api/Rules', undefined, options),

  checkCompatibility: (providerId, rules, options) =>
    request('POST', '/api/Rules/CompatibilityCheck', { providerId, rules }, options),

  getRawScript: (options) =>
    request('GET', '/api/Rules/Raw', undefined, options),

  saveRawScript: (content, scriptName, options) =>
    request('PUT', '/api/Rules/Raw', { content, scriptName }, options),

  // ── Mail ──────────────────────────────────────────────────────────────────
  // Folder paths are encoded: they may contain '/', '&' or '#'.

  getMailFolders: (options) =>
    request('GET', '/api/Mail/Folders', undefined, options),

  createMailFolder: (parentPath, name, options) =>
    request('POST', '/api/Mail/Folders', { parentPath, name }, options),

  renameMailFolder: (path, newParentPath, newName, options) =>
    request('PUT', '/api/Mail/Folders', { path, newParentPath, newName }, options),

  deleteMailFolder: (path, options) =>
    request('DELETE', '/api/Mail/Folders', { path }, options),

  setMailFolderSubscription: (path, subscribed, options) =>
    request('PUT', '/api/Mail/Folders/Subscription', { path, subscribed }, options),

  getMailMessages: (folder, page, pageSize, options) =>
    request('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`
      + (options?.grouped ? '&grouped=true' : ''), undefined, options),

  getMailMessage: (folder, uid, options) =>
    request('GET', `/api/Mail/Messages/Detail?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),

  getMessageSource: (folder, uid, options) =>
    request('GET', `/api/Mail/Messages/Source?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),

  setMessageFlags: (folder, uids, flag, value, options) =>
    request('PUT', '/api/Mail/Messages/Flags', { folderPath: folder, uids, flag, value }, options),

  moveMessages: (folder, uids, targetFolder, options) =>
    request('POST', '/api/Mail/Messages/Move', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  copyMessages: (folder, uids, targetFolder, options) =>
    request('POST', '/api/Mail/Messages/Copy', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  deleteMessages: (folder, uids, options) =>
    request('DELETE', '/api/Mail/Messages', { folderPath: folder, uids }, options),

  emptyFolder: (folder, targetFolder, options) =>
    request('POST', '/api/Mail/Folders/Empty', { folderPath: folder, targetFolderPath: targetFolder ?? null }, options),

  searchMessages: (criteria, page, pageSize, options) =>
    request('POST', '/api/Mail/Messages/Search', { ...criteria, page, pageSize }, options),

  getFolderRoles: (options) =>
    request('GET', '/api/Mail/FolderRoles', undefined, options),

  setFolderRole: (role, folderPath, options) =>
    request('PUT', '/api/Mail/FolderRoles', { role, folderPath }, options),

  clearFolderRole: (role, options) =>
    request('DELETE', `/api/Mail/FolderRoles?role=${encodeURIComponent(role)}`, undefined, options),

  sendMessage: (payload, options) =>
    request('POST', '/api/Mail/Send', payload, options),

  deleteAttachment: (id, options) =>
    request('DELETE', `/api/Mail/Attachments/${id}`, undefined, options),

  prepareQuote: (folder, uid, purpose, options) =>
    request('POST', '/api/Mail/Messages/PrepareQuote', { folder, uid, purpose }, options),

  saveDraft: (payload, options) =>
    request('POST', '/api/Mail/Drafts', payload, options),

  openDraft: (folder, uid, options) =>
    request('POST', '/api/Mail/Drafts/Open', { folder, uid }, options),

  // ── Connected accounts ───────────────────────────────────────────────────

  getConnectedAccounts: () =>
    request('GET', '/api/ConnectedAccounts'),

  connectAccount: (domainId, email, password) =>
    request('POST', '/api/ConnectedAccounts', { domainId, email, password }),

  updateConnectedAccountPassword: (id, password) =>
    request('PUT', `/api/ConnectedAccounts/${id}/Password`, { password }),

  deleteConnectedAccount: (id) =>
    request('DELETE', `/api/ConnectedAccounts/${id}`),

  getConnectableDomains: () =>
    request('GET', '/api/ConnectedAccounts/Domains'),

  // Typed here because the defaults alone would infer `null` and refuse a real id.
  /** @param {{ domainId?: string | null, accountId?: string | null }} target */
  startOAuthConnect: ({ domainId = null, accountId = null }) =>
    request('POST', '/api/ConnectedAccounts/OAuth/Start', { domainId, accountId }),

  completeOAuthConnect: (state) =>
    request('POST', '/api/ConnectedAccounts/OAuth/Complete', { state }),

  adminGetExternalDomains: () =>
    request('GET', '/api/Admin/domains/external'),

  adminCreateExternalDomain: (domain) =>
    request('POST', '/api/Admin/domains/external', domain),

  adminUpdateExternalDomain: (id, domain) =>
    request('PUT', `/api/Admin/domains/external/${id}`, domain),

  adminDeleteExternalDomain: (id) =>
    request('DELETE', `/api/Admin/domains/external/${id}`),

  // ── Preferences ───────────────────────────────────────────────────────────
  // The response covers every known key: defaults live on the backend, so there is no second
  // copy here to drift from.

  getPreferences: (options) =>
    request('GET', '/api/Preferences', undefined, options),

  setPreference: (key, value) =>
    request('PUT', '/api/Preferences', { key, value }),

  // ── App settings ──────────────────────────────────────────────────────────
  // Instance-wide, not account-scoped: readable anonymously since the login page needs them too.

  getAppSettings: (options) =>
    request('GET', '/api/AppSettings', undefined, options),

  setAppSetting: (key, value) =>
    request('PUT', '/api/AppSettings', { key, value }),
}

/**
 * Builds the attachment download URL. Kept beside the api object so encoding stays in one place.
 * A subresource fetch cannot carry a header, so a connected account rides along as `?account=`.
 */
export function mailAttachmentUrl(folder, uid, part, accountId) {
  const account = carriesAccount(accountId) ? `&account=${encodeURIComponent(accountId)}` : ''
  return `/api/Mail/Messages/Attachment?folder=${encodeURIComponent(folder)}&uid=${uid}&part=${encodeURIComponent(part)}${account}`
}

/** The API origin. Exported so the composer can undo an absolute staged URL before sending. */
export const API_BASE = BASE

/**
 * Builds a staged attachment's content URL — the src the composer shows inline images through.
 * Absolute, unlike mailAttachmentUrl: that one is a request path handed to requestBlob, which
 * prefixes BASE itself, while an <img> subresource would resolve against the SPA's own origin.
 * Staged files are namespaced by account on the backend, so a connected account must carry
 * `?account=` here too or its inline images 404.
 */
export function stagedAttachmentUrl(id, accountId) {
  const account = carriesAccount(accountId) ? `?account=${encodeURIComponent(accountId)}` : ''
  return `${BASE}/api/Mail/Attachments/${id}/content${account}`
}

/**
 * Uploads one outgoing attachment. XMLHttpRequest, not fetch: only XHR exposes upload
 * progress, and a 25 MB file without a bar reads as a hang.
 */
export function uploadAttachment(file, { onProgress, signal, accountId, inline } = {}) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${BASE}/api/Mail/Attachments`)
    xhr.withCredentials = true
    if (carriesAccount(accountId)) xhr.setRequestHeader('X-Account-Id', accountId)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress?.(event.loaded / event.total)
    }

    const onAbort = () => { detachAbort(); xhr.abort(); reject(new ApiError('Aborted', 0, null)) }
    const detachAbort = () => signal?.removeEventListener('abort', onAbort)

    xhr.onload = () => {
      detachAbort()
      if (xhr.status === 401) {
        const { code } = parseErrorEnvelope(xhr.responseText, xhr.statusText)
        clearSession()
        unauthorizedHandler?.()
        reject(new ApiError('Unauthorized', 401, code))
        return
      }
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText))
        return
      }
      const { message, code } = parseErrorEnvelope(xhr.responseText, xhr.statusText)
      reject(new ApiError(message || xhr.statusText, xhr.status, code))
    }
    xhr.onerror = () => { detachAbort(); reject(new ApiError('Network error', 0, null)) }

    // fetch rejects synchronously for a pre-aborted signal; XHR needs the same check up front.
    if (signal?.aborted) {
      reject(new ApiError('Aborted', 0, null))
      return
    }

    signal?.addEventListener('abort', onAbort)
    const form = new FormData()
    form.append('file', file)
    if (inline) form.append('inline', 'true')
    xhr.send(form)
  })
}
