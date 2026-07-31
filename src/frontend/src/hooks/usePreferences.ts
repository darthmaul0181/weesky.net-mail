import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'

/**
 * The account's webmail preferences.
 *
 * Shared rather than owned by the settings module, because the mail list reads them too. The
 * backend answers every known key with its default filled in, so there is no copy of the
 * defaults here — a consumer waits for the answer rather than guessing, which is why the
 * accessors below take the map and not an optional one.
 */
export const PREFERENCE_KEYS = {
  pageSize: 'mail.pageSize',
  showPreview: 'mail.showPreview',
  notifySound: 'mail.notifySound',
  notifyDesktop: 'mail.notifyDesktop',
  alwaysShowImages: 'mail.alwaysShowImages',
  showSpamScore: 'mail.showSpamScore',
  readingPane: 'mail.readingPane',
  rowActions: 'mail.rowActions',
  composeFormat: 'mail.composeFormat',
  captureRecipients: 'contacts.captureRecipients',
  trustContacts: 'mail.trustContacts',
} as const

export type Preferences = Record<string, string>

const queryKey = ['preferences'] as const

export function usePreferences() {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getPreferences({ signal }) as Promise<Preferences>,
    staleTime: 5 * 60 * 1000,
  })
}

export function useSetPreference() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.setPreference(key, value),
    onSuccess: () => {
      client.invalidateQueries({ queryKey })
      // The page size is part of what a message page *is*, so every cached page was computed
      // under the old value and has to go.
      client.invalidateQueries({ queryKey: ['mail'] })
    },
  })
}

/** How many messages one request asks for. Blocks in streaming mode: the wire has no notion
    of "all" — it is paging, just without a pager. */
export const BLOCK_SIZE = 100

export const ALL = 'all'

export function requestSizeOf(preferences: Preferences): number {
  const stored = preferences[PREFERENCE_KEYS.pageSize]
  return stored === ALL ? BLOCK_SIZE : Number(stored)
}

/** The only reader of the raw "all", so a NaN cannot be born anywhere else. */
export function isStreaming(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.pageSize] === ALL
}

export function showPreviewOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.showPreview] !== 'false'
}

/** On unless explicitly off — the gauge ships enabled, like the list preview. */
export function showSpamScoreOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.showSpamScore] !== 'false'
}

export type ReadingPane = 'right' | 'bottom' | 'none'

/** Falls back to 'right' — today's layout — for an absent key or a value this build ignores. */
export function readingPaneOf(preferences: Preferences): ReadingPane {
  const stored = preferences[PREFERENCE_KEYS.readingPane]
  return stored === 'bottom' || stored === 'none' ? stored : 'right'
}

export type ComposeFormat = 'html' | 'text'

/** Which editor a composer opens in. `html` unless the account explicitly chose otherwise, so an
    unrecognised value from a newer build never leaves the user in an editor they did not pick. */
export function composeFormatOf(preferences: Preferences): ComposeFormat {
  return preferences[PREFERENCE_KEYS.composeFormat] === 'text' ? 'text' : 'html'
}

export type RowAction = 'seen' | 'archive' | 'junk' | 'delete'

/** The order the icons are drawn in, whatever order they were stored in — the setting chooses
    which, never where. Delete stays last: it is the one a misplaced click cannot take back. */
export const ROW_ACTIONS: readonly RowAction[] = ['seen', 'archive', 'junk', 'delete']

/** What the row carried before the setting existed. */
export const DEFAULT_ROW_ACTIONS: readonly RowAction[] = ['seen', 'archive', 'delete']

/**
 * An absent key is an older backend; the empty string is an account that switched every icon
 * off. Collapsing the two would strip the row bare on the first render against a build that
 * does not know the key yet, so only `undefined` falls back.
 */
export function rowActionsOf(preferences: Preferences): RowAction[] {
  const stored = preferences[PREFERENCE_KEYS.rowActions]
  if (stored === undefined) return [...DEFAULT_ROW_ACTIONS]

  const chosen = new Set(stored.split(','))
  return ROW_ACTIONS.filter(action => chosen.has(action))
}

/** Off unless explicitly on: a key the backend has not sent yet must keep images blocked. */
export function alwaysShowImagesOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.alwaysShowImages] === 'true'
}

/** Off unless explicitly on — the mirror of showPreviewOf, because silence is the safe default. */
export function notifySoundOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.notifySound] === 'true'
}

export function notifyDesktopOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.notifyDesktop] === 'true'
}

/** "Anything to announce at all" — what puts a tab on the background poll and what arms the
    notification hook. One predicate, so the two can never disagree about who pays. */
export function notifiesOf(preferences: Preferences): boolean {
  return notifySoundOf(preferences) || notifyDesktopOf(preferences)
}

/** On unless explicitly off — an account whose row has never been written must capture. */
export function captureRecipientsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.captureRecipients] !== 'false'
}

/** Off unless explicitly on — a key the backend has not sent yet must not load remote images. */
export function trustContactsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.trustContacts] === 'true'
}
