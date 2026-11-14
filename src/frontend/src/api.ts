import { type RequestOptions, nullOn404, request, requestBlob } from './lib/http'
import type { Account } from './lib/accountIdentity'
import type { LoginResponse, Quota } from './types/account'
import type { Capabilities } from './types/capabilities'
import type { DavCredentials } from './types/dav'
import type { AppSettings } from './hooks/useAppSettings'
import type { DailyImageInfo } from './lib/dailyImage'
import type { Locale } from './lib/locale'
import type { Preferences } from './hooks/usePreferences'
import type { AliasInfo, IdentityListResponse, IdentityWrite } from './modules/mail/api/mailTypes'
import type { WhatsNewMark, WhatsNewState } from './modules/whatsnew/types'
import type { ConnectableDomain, OAuthStart } from './modules/settings/accounts/useConnectedAccounts'
import type { ConnectedAccount } from './types/connectedAccount'
import type { ServerVersion } from './modules/settings/about/aboutTypes'
import { LOGO_SIZES, type LogoSize } from './lib/appLogo'
import { mailApi } from './modules/mail/api/mailApi'
import { contactsApi } from './modules/contacts/contactsApi'
import { calendarApi } from './modules/calendar/calendarApi'
import { adminApi } from './modules/settings/admin/adminApi'
import { rulesApi } from './modules/settings/rules/rulesApi'

export * from './lib/http'

/** Every backend call, one method each: the transport is lib/http.ts, and each module's calls sit
    beside the module. */
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

  getAvatar: (address: string): Promise<Blob | null> =>
    requestBlob(`/api/Avatars?address=${encodeURIComponent(address)}`).then(({ blob }) => blob, nullOn404),

  getBimiLogo: (domain: string, selector?: string): Promise<Blob | null> =>
    requestBlob(`/api/Avatars/Bimi?domain=${encodeURIComponent(domain)}${selector ? `&selector=${encodeURIComponent(selector)}` : ''}`)
      .then(({ blob }) => blob, nullOn404),

  // The address comes from the backend's configuration, never composed here: the URL this app
  // calls is not necessarily the one the proxy publishes.
  getDavCredentials: () =>
    request<DavCredentials>('GET', '/api/DavCredentials'),

  // Turning it on for the first time answers the secret in this very response — the only moment
  // it exists in clear.
  setDavCardDav: (enabled: boolean) =>
    request<DavCredentials>('PUT', '/api/DavCredentials/CardDav', { enabled }),

  // The zone travels with the switch: turning it on creates the default calendar, and that
  // calendar is born in the zone of the browser that asked for it. language names the birthdays
  // calendar when this is what creates it, before the calendar module was ever opened.
  setDavCalDav: (enabled: boolean, timeZone: string, language?: 'fr' | 'en') =>
    request<DavCredentials>('PUT', '/api/DavCredentials/CalDav', { enabled, timeZone, language }),

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

  saveRawScript: (content: string, scriptName: string | null | undefined, options?: RequestOptions) =>
    request<null>('PUT', '/api/Rules/Raw', { content, scriptName }, options),

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

  getDailyImage: (lang: Locale, options?: RequestOptions) =>
    request<DailyImageInfo>('GET', `/api/AppSettings/daily-image?lang=${lang}`, undefined, options),

  // The running API's own version, for the About tab. Authenticated: an exact version is a
  // fingerprint, and only a signed-in user has a use for it.
  getVersion: (options?: RequestOptions) =>
    request<ServerVersion>('GET', '/api/Version', undefined, options),

  setAppSetting: (key: string, value: string) =>
    request<null>('PUT', '/api/AppSettings', { key, value }),

  setAppLogo: (images: Record<LogoSize, Blob>) => {
    const form = new FormData()
    for (const size of LOGO_SIZES) form.append(`logo${size}`, images[size], `logo-${size}.png`)
    return request<null>('PUT', '/api/AppSettings/logo', form)
  },

  deleteAppLogo: () => request<null>('DELETE', '/api/AppSettings/logo'),

  // ── What's new ────────────────────────────────────────────────────────────

  getWhatsNew: (options?: RequestOptions) =>
    request<WhatsNewState>('GET', '/api/WhatsNew', undefined, options),

  markWhatsNew: (ids: string[], state: WhatsNewMark) =>
    request<null>('PUT', '/api/WhatsNew/items', { ids, state }),

  setWhatsNewBaseline: (version: string) =>
    request<null>('PUT', '/api/WhatsNew/baseline', { version }),

  ...mailApi,
  ...contactsApi,
  ...calendarApi,
  ...adminApi,
  ...rulesApi,
}
