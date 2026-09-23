import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'

/** The account's webmail preferences, shared because the mail list reads them. The backend fills
 * every default, so none is copied here: the accessors take the map, not an optional one. */
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
  showFolderIcons: 'mail.showFolderIcons',
  groupConversations: 'mail.groupConversations',
  captureRecipients: 'contacts.captureRecipients',
  trustContacts: 'mail.trustContacts',
  language: 'ui.language',
} as const

export type Preferences = Record<string, string>

const queryKey = ['preferences'] as const

/** `enabled` so a caller with no session — the locale provider on the login page — can leave the
    query off rather than firing a request that can only 401. Same shape as `useFolders`. */
export function usePreferences({ enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getPreferences({ signal }),
    staleTime: 5 * 60 * 1000,
    enabled,
  })
}

export function useSetPreference() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.setPreference(key, value),
    // Optimistic on the one shared entry, so every reader (LocaleContext too) sees the value at
    // once; onError restores it, since a refused write must look refused.
    onMutate: async ({ key, value }) => {
      await client.cancelQueries({ queryKey })
      const previous = client.getQueryData<Preferences>(queryKey)
      client.setQueryData<Preferences>(queryKey, old => old && { ...old, [key]: value })
      return { previous }
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) client.setQueryData(queryKey, context.previous)
    },
    onSuccess: () => {
      void client.invalidateQueries({ queryKey })
      // The page size is part of what a message page *is*, so every cached page was computed
      // under the old value and has to go.
      void client.invalidateQueries({ queryKey: ['mail'] })
    },
  })
}

/** How many messages one request asks for. Blocks in streaming mode: the wire has no notion
    of "all" — it is paging, just without a pager. */
export const BLOCK_SIZE = 100

export const ALL = 'all'

/** The steps the `<select>` offers, and the only strings `requestSizeOf` trusts as a row count.
    `GeneralPage` draws its options from this same list, so the picker and the bound can never
    drift apart. */
export const PAGE_SIZES = ['10', '20', '30', '50', '100'] as const

/** Bounded, not merely parsed: a newer build, a hand-edited row or a direct `PUT` can write
    anything. A value outside `PAGE_SIZES` falls back to `BLOCK_SIZE` — what the account's own
    backend default (`'all'`) already resolves to, so a garbled row reads as none stored. */
export function requestSizeOf(preferences: Preferences): number {
  const stored = preferences[PREFERENCE_KEYS.pageSize]
  if (stored === ALL) return BLOCK_SIZE
  return stored !== undefined && (PAGE_SIZES as readonly string[]).includes(stored)
    ? Number(stored)
    : BLOCK_SIZE
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

/** An absent key is an older backend; '' is every icon switched off. Only `undefined` falls back,
 * or the row is stripped bare against a build that does not know the key yet. */
export function rowActionsOf(preferences: Preferences): RowAction[] {
  const stored = preferences[PREFERENCE_KEYS.rowActions]
  if (stored === undefined) return [...DEFAULT_ROW_ACTIONS]

  const chosen = new Set(stored.split(','))
  return ROW_ACTIONS.filter(action => chosen.has(action))
}

/** Off unless explicitly on — the folder column has never carried icons, so a backend that does
    not know the key yet must leave it as it was. */
export function showFolderIconsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.showFolderIcons] === 'true'
}

/** Off unless explicitly on — the list has always been flat, so a backend that does not know
    the key yet must keep it that way. */
export function groupConversationsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.groupConversations] === 'true'
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

/** The raw stored value — `auto`, `en`, `fr`, or something a newer build wrote. `resolveLocale`
    is what turns it into a locale; this accessor deliberately does not, so the settings radio can
    show "Automatic" as the distinct choice it is. */
export function languageOf(preferences: Preferences): string {
  return preferences[PREFERENCE_KEYS.language] ?? 'auto'
}

