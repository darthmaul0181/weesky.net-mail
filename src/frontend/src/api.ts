import type { Account } from './lib/accountIdentity'
import type { LoginResponse, Quota } from './types/account'
import type { Capabilities } from './types/capabilities'
import type { DavCredentials } from './types/dav'
import type { AppSettings } from './hooks/useAppSettings'
import type { Preferences } from './hooks/usePreferences'
import type {
  AliasInfo, ApplyReplyArgs, ApplyReplyResponse, FolderRoleEntry, IdentityListResponse,
  IdentityWrite, InvitationResponse, MailFlagName, MailFolderNode, MailFolderPage,
  MailMessageDetail, MailMessageSource, MailSearchPage, OpenedDraft, PreparedQuote, QuotePurpose,
  RespondInvitationArgs, SaveDraftArgs, SavedDraft, SendMessageArgs, SendMessageResult,
  StagedAttachmentInfo,
} from './modules/mail/api/mailTypes'
import type { SearchCriteria } from './modules/mail/list/searchCriteria'
import type {
  Contact, ContactDetail, ContactDraft, ContactImportReport, ContactListResponse,
} from './modules/contacts/contactTypes'
import type { ContactGroup, ContactGroupsResponse } from './modules/contacts/contactGroupTypes'
import type {
  CalendarImportOutcome, CalendarImportReport, CalendarListResponse, CalendarWrite, CreatedId,
  EditScope, EventDetail, EventUpdateBody, EventUpdated, EventWrite, OccurrenceListResponse,
} from './modules/calendar/calendarTypes'
import type {
  AdminDomain, AdminDomainPayload, AdminUser, AdminUserPayload, VirtualDomain,
} from './modules/settings/admin/adminTypes'
import type { ExternalDomain, ExternalDomainPayload } from './modules/settings/admin/useExternalDomains'
import type {
  SchedulingAccount, SchedulingAccountPayload, SchedulingAccountTestResult,
} from './modules/settings/admin/useSchedulingAccount'
import type {
  DeliveryReplyKey, DeliveryReplyKeyGenerated,
} from './modules/settings/admin/useDeliveryReplyKey'
import type { ConnectableDomain, OAuthStart } from './modules/settings/accounts/useConnectedAccounts'
import type { ConnectedAccount } from './types/connectedAccount'
import type {
  CompatibilityCheckResult, RuleProvider, SieveRawScript, SieveRuleSet, SieveRuleWrite,
} from './modules/settings/rules/rulesTypes'
import type { ServerVersion } from './modules/settings/about/aboutTypes'
import { readStored, removeStored, writeStored } from './lib/safeStorage'

const BASE: string = import.meta.env.VITE_API_BASE
const SESSION_KEY = 'sessionActive'

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface RequestOptions {
  /** A connected account's id; 'primary', null or absent all mean the session's own mailbox. */
  accountId?: string | null
  signal?: AbortSignal
}

export interface UploadOptions extends RequestOptions {
  onProgress?: (fraction: number) => void
  inline?: boolean
}

let unauthorizedHandler: (() => void) | null = null

export function markLoggedIn(): void {
  writeStored(SESSION_KEY, '1')
}

export function clearSession(): void {
  removeStored(SESSION_KEY)
}

export function hasSession(): boolean {
  return readStored(SESSION_KEY) === '1'
}

export function setUnauthorizedHandler(fn: (() => void) | null): void {
  unauthorizedHandler = fn
}

/** An HTTP failure that keeps its status. The backend's stable ResultEnveloppe message
 * ("credentials_unavailable", "Message not found") is `code`, so callers branch without matching
 * prose. Extends Error, so `rejects.toThrow(message)` still holds. */
export class ApiError extends Error {
  status: number
  code: string | null

  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

interface ErrorEnvelope { message: string; code: string | null }

/** Our envelope carries `message` (`Message` from an older writer); ASP.NET's ProblemDetails, `title`. */
interface ErrorBody { message?: unknown; Message?: unknown; title?: unknown }

// Shared by readError (fetch) and uploadAttachment (XHR) — same envelope shape, different
// transport for the raw text.
function parseErrorEnvelope(text: string, fallbackMessage: string): ErrorEnvelope {
  if (!text) return { message: fallbackMessage, code: null }
  try {
    const parsed: unknown = JSON.parse(text)
    const body: ErrorBody = typeof parsed === 'object' && parsed !== null ? parsed : {}
    const message = body.message ?? body.Message
    if (typeof message === 'string') return { message, code: message }

    // A model-validation refusal answers ProblemDetails, not our envelope. Its title is the
    // only readable line in it, and it carries no stable code — falling through to the raw
    // text printed the whole JSON blob on screen.
    return { message: typeof body.title === 'string' ? body.title : fallbackMessage, code: null }
  } catch {
    return { message: text, code: null }
  }
}

async function readError(res: Response): Promise<ErrorEnvelope> {
  // Defensive: some callers stub a response with no body reader at all.
  if (typeof res.text !== 'function') return { message: res.statusText ?? '', code: null }

  const text = await res.text().catch(() => '')
  return parseErrorEnvelope(text, res.statusText ?? '')
}

// 'primary' means the same as no id at all — the backend defaults there — so only a genuinely
// connected account changes what travels over the wire. The one place this is decided.
function carriesAccount(accountId: string | null | undefined): accountId is string {
  return Boolean(accountId) && accountId !== 'primary'
}

/** `T` is the DTO the route answers; a route that can answer 204 names `| null` in it. */
export async function request<T>(
  method: HttpMethod, path: string, body?: unknown, options: RequestOptions = {},
): Promise<T> {
  // FormData carries its own multipart boundary; naming a content type here breaks the parse on
  // the server side.
  const isForm = typeof FormData !== 'undefined' && body instanceof FormData
  const headers: Record<string, string> = {}
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

  // A 204 has no body: the routes that answer one declare `| null` in T, which this stands for.
  if (res.status === 204) return null as T

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  // The wire contract: the body is the DTO the route declares, and nothing checks it here.
  const payload: unknown = await res.json()
  return payload as T
}

/** Fetches a binary response (attachments), since request() always parses JSON. */
export async function requestBlob(
  path: string, options: RequestOptions = {},
): Promise<{ blob: Blob; fileName: string }> {
  const headers: Record<string, string> = {}
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

  return { blob: await res.blob(), fileName: match?.[1] ? decodeURIComponent(match[1]) : 'attachment' }
}

export const api = {
  login: (email: string, password: string) =>
    request<LoginResponse>('POST', '/api/Login', { email, password }),

  logout: () =>
    request<null>('DELETE', '/api/Login'),

  getAccount: () =>
    request<Account>('GET', '/api/Account'),

  // Every field is optional and an older backend answers 404 — callers treat both as "no
  // capabilities", not as an error.
  getCapabilities: () =>
    request<Capabilities>('GET', '/api/Capabilities'),

  // 204 when the IMAP server advertises no QUOTA capability — request() already resolves that to
  // null before attempting to parse a body.
  getQuota: () =>
    request<Quota | null>('GET', '/api/Account/Quota'),

  getAliases: () =>
    request<AliasInfo[]>('GET', '/api/Aliases'),

  getIdentities: (options?: RequestOptions) =>
    request<IdentityListResponse>('GET', '/api/Identities', undefined, options),

  // Replaces the whole set: the payload is the list, not a delta.
  putIdentities: (identities: IdentityWrite[], options?: RequestOptions) =>
    request<null>('PUT', '/api/Identities', { identities }, options),

  getContacts: () =>
    request<ContactListResponse>('GET', '/api/Contacts'),

  // The whole card, which the list does not carry: only the open contact pays for it.
  getContact: (id: string) =>
    request<ContactDetail>('GET', `/api/Contacts/${id}`),

  // A blob, not a URL handed to <img>: the picture sits behind the session cookie on another
  // origin, which an image element cannot send.
  getContactPhoto: (id: string) =>
    requestBlob(`/api/Contacts/${id}/Photo`).then(({ blob }) => blob),

  createContact: (contact: ContactDraft) =>
    request<Contact>('POST', '/api/Contacts', contact),

  // Replaces the contact whole — names, favourite flag and the entire address list.
  updateContact: (id: string, contact: ContactDraft) =>
    request<null>('PUT', `/api/Contacts/${id}`, contact),

  deleteContact: (id: string) =>
    request<null>('DELETE', `/api/Contacts/${id}`),

  // Its own route: the star is toggled from a tile holding a possibly stale copy, so a whole
  // contact PUT from there would clobber a concurrent edit.
  setContactFavorite: (id: string, isFavorite: boolean) =>
    request<null>('PUT', `/api/Contacts/${id}/Favorite`, { isFavorite }),

  // The batch travels in the body rather than the URL: a list of ids in a query string breaks past
  // a few dozen and has no agreed shape. Capped at 200 server-side, the mail's own batch size.
  deleteContacts: (ids: string[]) =>
    request<null>('DELETE', '/api/Contacts', { ids }),

  setContactsFavorite: (ids: string[], isFavorite: boolean) =>
    request<null>('PUT', '/api/Contacts/Favorite', { ids, isFavorite }),

  importContacts: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<ContactImportReport>('POST', '/api/Contacts/Import', form)
  },

  exportContacts: () => requestBlob('/api/Contacts/Export'),

  // tz is required: it is the zone the default calendar is created with the first time it is
  // asked for, and the backend needs it on every call to answer that lazily.
  getCalendars: (tz: string) =>
    request<CalendarListResponse>('GET', `/api/Calendars?tz=${encodeURIComponent(tz)}`),

  // tz here is the new calendar's own zone, asked once at creation and never again.
  createCalendar: (calendar: CalendarWrite, tz: string) =>
    request<CreatedId>('POST', `/api/Calendars?tz=${encodeURIComponent(tz)}`, calendar),

  updateCalendar: (id: string, calendar: CalendarWrite) =>
    request<null>('PUT', `/api/Calendars/${id}`, calendar),

  // Its own route: the sidebar checkbox is a display state, never projected through a whole-
  // calendar PUT.
  setCalendarVisible: (id: string, visible: boolean) =>
    request<null>('PUT', `/api/Calendars/${id}/Visible`, { visible }),

  deleteCalendar: (id: string) =>
    request<null>('DELETE', `/api/Calendars/${id}`),

  exportCalendar: (id: string) => requestBlob(`/api/Calendars/${id}/Export`),

  importCalendar: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<CalendarImportReport>('POST', `/api/Calendars/${id}/Import`, form)
  },

  // One gesture rather than two: the calendar is created and the file poured into it. An empty
  // colour is left out so the backend gives the palette's next one.
  importCalendarAsNew: (file: File, displayName: string, color: string, tz: string) => {
    const form = new FormData()
    form.append('file', file)
    form.append('displayName', displayName)
    if (color) form.append('color', color)
    return request<CalendarImportOutcome>(
      'POST', `/api/Calendars/Import?tz=${encodeURIComponent(tz)}`, form)
  },

  // from/to are ISO instants (…Z); tz only decides which day a floating instance falls on.
  getOccurrences: (from: string, to: string, tz: string) =>
    request<OccurrenceListResponse>('GET',
      `/api/Calendar/Events?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&tz=${encodeURIComponent(tz)}`),

  searchEvents: (q: string) =>
    request<OccurrenceListResponse>('GET', `/api/Calendar/Events/Search?q=${encodeURIComponent(q)}`),

  getEvent: (id: string) =>
    request<EventDetail>('GET', `/api/Calendar/Events/${id}`),

  createEvent: (event: EventWrite) =>
    request<CreatedId>('POST', '/api/Calendar/Events', event),

  updateEvent: (id: string, event: EventUpdateBody) =>
    request<EventUpdated>('PUT', `/api/Calendar/Events/${id}`, event),

  // scope/instanceId travel in the query string, the same rule the PUT of a narrow edit follows;
  // language is the one the cancellations are written in.
  deleteEvent: (id: string, scope: EditScope, instanceId?: string, language?: string) => {
    const params = new URLSearchParams({ scope })
    if (instanceId) params.set('instanceId', instanceId)
    if (language) params.set('language', language)
    return request<null>('DELETE', `/api/Calendar/Events/${id}?${params}`)
  },

  getContactGroups: () => request<ContactGroupsResponse>('GET', '/api/ContactGroups'),

  createContactGroup: (name: string) => request<ContactGroup>('POST', '/api/ContactGroups', { name }),

  renameContactGroup: (id: string, name: string) =>
    request<null>('PUT', `/api/ContactGroups/${id}`, { name }),

  deleteContactGroup: (id: string) => request<null>('DELETE', `/api/ContactGroups/${id}`),

  addContactGroupMembers: (id: string, contactIds: string[]) =>
    request<null>('POST', `/api/ContactGroups/${id}/Members`, { contactIds }),

  removeContactGroupMembers: (id: string, contactIds: string[]) =>
    request<null>('DELETE', `/api/ContactGroups/${id}/Members`, { contactIds }),

  // The address comes from the backend's configuration, never composed here: the URL this app
  // calls is not necessarily the one the proxy publishes.
  getDavCredentials: () =>
    request<DavCredentials>('GET', '/api/DavCredentials'),

  // Turning it on for the first time answers the secret in this very response — the only moment
  // it exists in clear.
  setDavCardDav: (enabled: boolean) =>
    request<DavCredentials>('PUT', '/api/DavCredentials/CardDav', { enabled }),

  // The zone travels with the switch: turning it on creates the default calendar, and that
  // calendar is born in the zone of the browser that asked for it.
  setDavCalDav: (enabled: boolean, timeZone: string) =>
    request<DavCredentials>('PUT', '/api/DavCredentials/CalDav', { enabled, timeZone }),

  regenerateDavSecret: () =>
    request<DavCredentials>('POST', '/api/DavCredentials/Regenerate'),

  getTrustedSenders: () =>
    request<string[]>('GET', '/api/TrustedSenders'),

  trustSender: (address: string) =>
    request<null>('POST', '/api/TrustedSenders', { address }),

  // The address travels in the query string, so it is encoded here rather than at call sites.
  untrustSender: (address: string) =>
    request<null>('DELETE', `/api/TrustedSenders?address=${encodeURIComponent(address)}`),

  createAlias: (name: string, domain: string) =>
    request<null>('POST', '/api/Aliases', { name, domain }),

  deleteAlias: (name: string, domain: string) =>
    request<null>('DELETE', '/api/Aliases', { name, domain }),

  changePassword: (oldPassword: string, newPassword: string) =>
    request<null>('PATCH', '/api/Account/ChangeSecret', { oldPassword, newPassword }),

  changeFullName: (fullName: string) =>
    request<null>('POST', '/api/Account/FullName', { fullName }),

  adminGetUsers: () =>
    request<AdminUser[]>('GET', '/api/Admin/users'),

  adminCreateUser: (payload: AdminUserPayload) =>
    request<AdminUser>('POST', '/api/Admin/users', payload),

  adminUpdateUser: (id: number, payload: AdminUserPayload) =>
    request<AdminUser>('PUT', `/api/Admin/users/${id}`, payload),

  adminDeleteUser: (id: number) =>
    request<null>('DELETE', `/api/Admin/users/${id}`),

  adminGetDomains: () =>
    request<AdminDomain[]>('GET', '/api/Admin/domains'),

  adminCreateDomain: (payload: AdminDomainPayload) =>
    request<AdminDomain>('POST', '/api/Admin/domains', payload),

  adminUpdateDomain: (id: string, payload: AdminDomainPayload) =>
    request<AdminDomain>('PUT', `/api/Admin/domains/${id}`, payload),

  // deleteAliases acknowledges the cascade: the aliases anchored on the domain go with it. Sent
  // only once the user has confirmed, so an unacknowledged call is refused server-side too.
  adminDeleteDomain: (id: string, deleteAliases = false) =>
    request<null>('DELETE', `/api/Admin/domains/${id}${deleteAliases ? '?deleteAliases=true' : ''}`),

  adminGetUserQuota: (id: number) =>
    request<Quota>('GET', `/api/Admin/users/${id}/quota`),

  adminGetVirtualDomains: () =>
    request<VirtualDomain[]>('GET', '/api/Admin/domains/virtuals'),

  adminAddVirtualDomainOwner: (domainId: string, userId: number) =>
    request<VirtualDomain>('PUT', `/api/Admin/domains/virtuals/${domainId}`, { userId }),

  adminRemoveVirtualDomainOwner: (domainId: string, userId: number) =>
    request<null>('DELETE', `/api/Admin/domains/virtuals/${domainId}/${userId}`),

  getRuleProviders: (options?: RequestOptions) =>
    request<RuleProvider[]>('GET', '/api/Rules/Providers', undefined, options),

  getRules: (options?: RequestOptions) =>
    request<SieveRuleSet>('GET', '/api/Rules', undefined, options),

  saveRules: (
    rules: SieveRuleWrite[], providerId: string | null | undefined, scriptName: string | null | undefined,
    options?: RequestOptions,
  ) =>
    request<null>('PUT', '/api/Rules', { rules, providerId, scriptName }, options),

  deleteRules: (options?: RequestOptions) =>
    request<null>('DELETE', '/api/Rules', undefined, options),

  checkCompatibility: (providerId: string, rules: SieveRuleWrite[], options?: RequestOptions) =>
    request<CompatibilityCheckResult>('POST', '/api/Rules/CompatibilityCheck', { providerId, rules }, options),

  getRawScript: (options?: RequestOptions) =>
    request<SieveRawScript>('GET', '/api/Rules/Raw', undefined, options),

  saveRawScript: (content: string, scriptName: string | null | undefined, options?: RequestOptions) =>
    request<null>('PUT', '/api/Rules/Raw', { content, scriptName }, options),

  // ── Mail ──────────────────────────────────────────────────────────────────
  // Folder paths are encoded: they may contain '/', '&' or '#'.

  getMailFolders: (options?: RequestOptions) =>
    request<MailFolderNode[]>('GET', '/api/Mail/Folders', undefined, options),

  /** Answers the new folder's full path. */
  createMailFolder: (parentPath: string, name: string, options?: RequestOptions) =>
    request<string>('POST', '/api/Mail/Folders', { parentPath, name }, options),

  renameMailFolder: (path: string, newParentPath: string, newName: string, options?: RequestOptions) =>
    request<string>('PUT', '/api/Mail/Folders', { path, newParentPath, newName }, options),

  deleteMailFolder: (path: string, options?: RequestOptions) =>
    request<null>('DELETE', '/api/Mail/Folders', { path }, options),

  setMailFolderSubscription: (path: string, subscribed: boolean, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/Folders/Subscription', { path, subscribed }, options),

  getMailMessages: (
    folder: string, page: number, pageSize: number, options?: RequestOptions & { grouped?: boolean },
  ) =>
    request<MailFolderPage>('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`
      + (options?.grouped ? '&grouped=true' : ''), undefined, options),

  getMailMessage: (folder: string, uid: number, options?: RequestOptions) =>
    request<MailMessageDetail>('GET', `/api/Mail/Messages/Detail?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),

  // A calendar route reached from the reader: it answers the organiser and files the event, and
  // it names the message the block was read from, so it lives beside the message calls.
  respondInvitation: (body: RespondInvitationArgs, options?: RequestOptions) =>
    request<InvitationResponse>('POST', '/api/Calendar/Invitations/Respond', body, options),
  applyInvitationReply: (body: ApplyReplyArgs, options?: RequestOptions) =>
    request<ApplyReplyResponse>('POST', '/api/Calendar/Invitations/ApplyReply', body, options),

  getMessageSource: (folder: string, uid: number, options?: RequestOptions) =>
    request<MailMessageSource>('GET', `/api/Mail/Messages/Source?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),

  setMessageFlags: (folder: string, uids: number[], flag: MailFlagName, value: boolean, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/Messages/Flags', { folderPath: folder, uids, flag, value }, options),

  moveMessages: (folder: string, uids: number[], targetFolder: string, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Messages/Move', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  copyMessages: (folder: string, uids: number[], targetFolder: string, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Messages/Copy', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  deleteMessages: (folder: string, uids: number[], options?: RequestOptions) =>
    request<null>('DELETE', '/api/Mail/Messages', { folderPath: folder, uids }, options),

  emptyFolder: (folder: string, targetFolder?: string | null, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Folders/Empty', { folderPath: folder, targetFolderPath: targetFolder ?? null }, options),

  searchMessages: (criteria: SearchCriteria, page: number, pageSize: number, options?: RequestOptions) =>
    request<MailSearchPage>('POST', '/api/Mail/Messages/Search', { ...criteria, page, pageSize }, options),

  getFolderRoles: (options?: RequestOptions) =>
    request<FolderRoleEntry[]>('GET', '/api/Mail/FolderRoles', undefined, options),

  setFolderRole: (role: string, folderPath: string, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/FolderRoles', { role, folderPath }, options),

  clearFolderRole: (role: string, options?: RequestOptions) =>
    request<null>('DELETE', `/api/Mail/FolderRoles?role=${encodeURIComponent(role)}`, undefined, options),

  sendMessage: (payload: SendMessageArgs, options?: RequestOptions) =>
    request<SendMessageResult>('POST', '/api/Mail/Send', payload, options),

  deleteAttachment: (id: string, options?: RequestOptions) =>
    request<null>('DELETE', `/api/Mail/Attachments/${id}`, undefined, options),

  prepareQuote: (folder: string, uid: number, purpose: QuotePurpose, options?: RequestOptions) =>
    request<PreparedQuote>('POST', '/api/Mail/Messages/PrepareQuote', { folder, uid, purpose }, options),

  saveDraft: (payload: SaveDraftArgs, options?: RequestOptions) =>
    request<SavedDraft>('POST', '/api/Mail/Drafts', payload, options),

  openDraft: (folder: string, uid: number, options?: RequestOptions) =>
    request<OpenedDraft>('POST', '/api/Mail/Drafts/Open', { folder, uid }, options),

  // ── Connected accounts ───────────────────────────────────────────────────

  getConnectedAccounts: () =>
    request<ConnectedAccount[]>('GET', '/api/ConnectedAccounts'),

  connectAccount: (domainId: string | null, email: string, password: string) =>
    request<ConnectedAccount>('POST', '/api/ConnectedAccounts', { domainId, email, password }),

  updateConnectedAccountPassword: (id: string, password: string) =>
    request<null>('PUT', `/api/ConnectedAccounts/${id}/Password`, { password }),

  deleteConnectedAccount: (id: string) =>
    request<null>('DELETE', `/api/ConnectedAccounts/${id}`),

  getConnectableDomains: () =>
    request<ConnectableDomain[]>('GET', '/api/ConnectedAccounts/Domains'),

  startOAuthConnect: ({ domainId = null, accountId = null }: { domainId?: string | null; accountId?: string | null }) =>
    request<OAuthStart>('POST', '/api/ConnectedAccounts/OAuth/Start', { domainId, accountId }),

  completeOAuthConnect: (state: string) =>
    request<ConnectedAccount>('POST', '/api/ConnectedAccounts/OAuth/Complete', { state }),

  adminGetExternalDomains: () =>
    request<ExternalDomain[]>('GET', '/api/Admin/domains/external'),

  adminCreateExternalDomain: (domain: ExternalDomainPayload) =>
    request<ExternalDomain>('POST', '/api/Admin/domains/external', domain),

  adminUpdateExternalDomain: (id: string, domain: ExternalDomainPayload) =>
    request<null>('PUT', `/api/Admin/domains/external/${id}`, domain),

  adminDeleteExternalDomain: (id: string) =>
    request<null>('DELETE', `/api/Admin/domains/external/${id}`),

  adminGetSchedulingAccount: () =>
    request<SchedulingAccount>('GET', '/api/SchedulingAccount'),

  adminSaveSchedulingAccount: (account: SchedulingAccountPayload, options?: RequestOptions) =>
    request<null>('PUT', '/api/SchedulingAccount', account, options),

  adminDeleteSchedulingAccount: () =>
    request<null>('DELETE', '/api/SchedulingAccount'),

  // `account` is optional: no body tests and records the stored one, a body tests those values
  // without persisting anything — the same request shape either way.
  adminTestSchedulingAccount: (account?: SchedulingAccountPayload) =>
    request<SchedulingAccountTestResult>('POST', '/api/SchedulingAccount/Test', account),

  adminGetDeliveryReplyKey: () =>
    request<DeliveryReplyKey>('GET', '/api/DeliveryReplyKey'),

  adminGenerateDeliveryReplyKey: () =>
    request<DeliveryReplyKeyGenerated>('POST', '/api/DeliveryReplyKey'),

  adminSetDeliveryReplies: (body: { enabled: boolean }) =>
    request<null>('PUT', '/api/DeliveryReplyKey', body),

  adminDeleteDeliveryReplyKey: () =>
    request<null>('DELETE', '/api/DeliveryReplyKey'),

  // ── Preferences ───────────────────────────────────────────────────────────
  // The response covers every known key: defaults live on the backend, so there is no second
  // copy here to drift from.

  getPreferences: (options?: RequestOptions) =>
    request<Preferences>('GET', '/api/Preferences', undefined, options),

  setPreference: (key: string, value: string) =>
    request<null>('PUT', '/api/Preferences', { key, value }),

  // ── App settings ──────────────────────────────────────────────────────────
  // Instance-wide, not account-scoped: readable anonymously since the login page needs them too.

  getAppSettings: (options?: RequestOptions) =>
    request<AppSettings>('GET', '/api/AppSettings', undefined, options),

  // The running API's own version, for the About tab. Authenticated: an exact version is a
  // fingerprint, and only a signed-in user has a use for it.
  getVersion: (options?: RequestOptions) =>
    request<ServerVersion>('GET', '/api/Version', undefined, options),

  setAppSetting: (key: string, value: string) =>
    request<null>('PUT', '/api/AppSettings', { key, value }),
}

/** The attachment download URL, encoded in one place. A subresource fetch cannot carry a header,
 * so a connected account rides along as `?account=`. */
export function mailAttachmentUrl(folder: string, uid: number, part: string, accountId?: string | null): string {
  const account = carriesAccount(accountId) ? `&account=${encodeURIComponent(accountId)}` : ''
  return `/api/Mail/Messages/Attachment?folder=${encodeURIComponent(folder)}&uid=${uid}&part=${encodeURIComponent(part)}${account}`
}

/** The API origin. Exported so the composer can undo an absolute staged URL before sending. */
export const API_BASE = BASE

/** A staged attachment's content URL, the src of the composer's inline images. Absolute, unlike
 * mailAttachmentUrl, or an <img> resolves it against the SPA's origin. Staged files are namespaced
 * by account, so a connected account carries `?account=` or its inline images 404. */
export function stagedAttachmentUrl(id: string, accountId?: string | null): string {
  const account = carriesAccount(accountId) ? `?account=${encodeURIComponent(accountId)}` : ''
  return `${BASE}/api/Mail/Attachments/${id}/content${account}`
}

/** Uploads one outgoing attachment. XHR, not fetch: only XHR exposes upload progress, and a
 * 25 MB file without a bar reads as a hang. */
export function uploadAttachment(
  file: File, { onProgress, signal, accountId, inline }: UploadOptions = {},
): Promise<StagedAttachmentInfo> {
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
        // The wire contract: the upload route answers a StagedAttachmentInfo, unchecked here.
        const staged: unknown = JSON.parse(xhr.responseText)
        resolve(staged as StagedAttachmentInfo)
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
