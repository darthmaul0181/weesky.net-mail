import {
  useInfiniteQuery, useIsFetching, useMutation, useQuery, useQueryClient,
  type InfiniteData, type Query, type QueryClient, type QueryKey,
} from '@tanstack/react-query'
import { useCallback } from 'react'
import { ApiError, api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import { notifiesOf, usePreferences } from '../../hooks/usePreferences'
import type {
  MailFolderNode, MailFolderPage, MailMessageDetail, MailMessageSource, MailMessageSummary,
  MailSearchPage, FolderRoleEntry, AliasInfo, IdentityListResponse, SendingIdentity, PreparedQuote,
  QuotePurpose, SavedDraft, OpenedDraft,
} from './api/mailTypes'
import { flatten } from './folders/folderNodes'
import type { SearchCriteria } from './list/searchCriteria'
import {
  patchFolderCounts, patchFolderUnread, patchSearchResults, patchSummaries,
  removeSearchResults, removeSummaries,
  type FolderCountDeltas, type MailFlagName,
} from './list/listPatch'
import { nextBlockIndex } from './list/messageStream'


// The prefixes are the keys minus their trailing size arguments: what a mutation or a refresh
// matches on, since it patches a folder's every cached page whatever size it was fetched at.
const messagesIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messages', folder] as const
const messageStreamIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messageStream', folder] as const
const searchIn = (accountId: string) => ['mail', accountId, 'search'] as const

/**
 * Every key is scoped by the active account, so linking a second account later isolates its
 * cache instead of mixing two mailboxes together.
 */
export const mailKeys = {
  all: (accountId: string) => ['mail', accountId] as const,
  folders: (accountId: string) => ['mail', accountId, 'folders'] as const,
  messagesIn,
  // pageSize is part of the key: a cached page was computed under one size and means something
  // else under another.
  messages: (accountId: string, folder: string, page: number, pageSize: number) =>
    [...messagesIn(accountId, folder), page, pageSize] as const,
  message: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'message', folder, uid] as const,
  messageSource: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'source', folder, uid] as const,
  // Its own key: what it caches is not a page but a sequence of pages, and mixing the two
  // shapes under one key is a type error that only shows at runtime.
  messageStreamIn,
  messageStream: (accountId: string, folder: string, requestSize: number) =>
    [...messageStreamIn(accountId, folder), requestSize] as const,
  folderRoles: (accountId: string) => ['mail', accountId, 'folderRoles'] as const,
  identities: (accountId: string) => ['mail', accountId, 'identities'] as const,
  trustedSenders: (accountId: string) => ['mail', accountId, 'trustedSenders'] as const,
  aliases: (accountId: string) => ['mail', accountId, 'aliases'] as const,
  /** Prefix for every cached search — what the idle key falls back to when no search is active. */
  searchIn,
  // Criteria in the key: two different searches are two caches, never one overwriting the other.
  search: (accountId: string, criteria: SearchCriteria, page: number, pageSize: number) =>
    [...searchIn(accountId), criteria, page, pageSize] as const,
  /** Mutation key, not a query key: it is what lets the poll tell our own writes from a change.
      Carried by every write — flags, move, copy, delete — since each patches the same counters. */
  writes: (accountId: string) => ['mail', accountId, 'writes'] as const,
}

// Moved to src/hooks: contacts scope their keys on it too. Re-exported so the mail module's own
// importers keep working from here.
export { useAccountId }

// Every call below carries `{ accountId }`, read at render rather than at fire time: a write
// started under one mailbox lands there even if the user switches while it is in flight.

/** One cheap LIST+STATUS across all folders. Internal, like BLOCK_SIZE: not a setting. */
export const POLL_INTERVAL = 60_000

/** `enabled` is what keeps a user who asked for no notification off the poll: the shell passes
    `sound || desktop`, /mail passes nothing, and one enabled observer runs the query. */
export function useFolders(enabled = true) {
  const accountId = useAccountId()
  const { data: preferences } = usePreferences()
  // Background polling is the cost of a notification, so only those who asked for one pay it:
  // an untouched tab keeps costing nothing.
  const notifies = preferences ? notifiesOf(preferences) : false

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal, accountId }),
    enabled,
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: notifies,
  })
}

/**
 * The manual counterpart of the poll: force the folders refetch and let useListRefresh's
 * watcher cascade onto the open folder's list by its own rules — paged invalidate, streaming
 * block-0 merge, uidValidity reset. No list machinery of its own, so the "never invalidate
 * the stream" rule holds by construction. `fetching` covers the poll tick too: it is the
 * signal the refresh button spins on, manual and automatic alike.
 */
export function useMailRefresh() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const fetching = useIsFetching({ queryKey: mailKeys.folders(accountId) }) > 0
  const refresh = useCallback(() => {
    void queryClient.refetchQueries({ queryKey: mailKeys.folders(accountId), type: 'active' })
  }, [queryClient, accountId])
  return { refresh, fetching }
}

export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, pageSize, { signal, accountId }),
    enabled: enabled && folderPath !== null,
    // Keeps the current page on screen while the next one loads, instead of flashing empty.
    placeholderData: (previous) => previous,
  })
}

export function useMessageStream(folderPath: string | null, requestSize: number, enabled: boolean) {
  const accountId = useAccountId()

  return useInfiniteQuery({
    queryKey: mailKeys.messageStream(accountId, folderPath ?? '', requestSize),
    queryFn: ({ pageParam, signal }) =>
      api.getMailMessages(folderPath, pageParam, requestSize,
        { signal, accountId }) as Promise<MailFolderPage>,
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) =>
      nextBlockIndex(lastPage, allPages.length, requestSize),
    enabled: enabled && folderPath !== null && requestSize > 0,
    // TanStack refetches *every* loaded block on focus. Forty blocks is forty IMAP
    // connections and forty full folder sorts, so this stays off.
    refetchOnWindowFocus: false,
  })
}

export function useMessage(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageDetail>({
    queryKey: mailKeys.message(accountId, folderPath ?? '', uid ?? 0),
    queryFn: ({ signal }) => api.getMailMessage(folderPath, uid, { signal, accountId }),
    enabled: folderPath !== null && uid !== null,
  })
}

/** The message as it arrived. Its own key rather than a slice of `message`: what it caches is
    the RFC822 bytes, and a reader that has the detail has not paid for those. */
export function useMessageSource(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageSource>({
    queryKey: mailKeys.messageSource(accountId, folderPath ?? '', uid ?? 0),
    queryFn: ({ signal }) => api.getMessageSource(folderPath, uid, { signal, accountId }),
    enabled: folderPath !== null && uid !== null,
    // The bytes of a given (folder, uid) never change; a stale reload is the user's own call.
    staleTime: Infinity,
  })
}

/**
 * A search is a snapshot: no window-focus replay (an all-folders sweep is N IMAP SEARCHes),
 * no poll, no writes key. placeholderData keeps the previous page while the next loads.
 */
export function useSearchMessages(criteria: SearchCriteria | null, page: number, pageSize: number) {
  const accountId = useAccountId()

  return useQuery<MailSearchPage>({
    queryKey: criteria
      ? mailKeys.search(accountId, criteria, page, pageSize)
      : [...mailKeys.searchIn(accountId), 'idle'],
    queryFn: ({ signal }) => api.searchMessages(criteria, page, pageSize, { signal, accountId }),
    enabled: criteria !== null && pageSize > 0,
    refetchOnWindowFocus: false,
    placeholderData: (previous) => previous,
  })
}

export function useFolderRoles() {
  const accountId = useAccountId()

  return useQuery<FolderRoleEntry[]>({
    queryKey: mailKeys.folderRoles(accountId),
    queryFn: ({ signal }) => api.getFolderRoles({ signal, accountId }),
  })
}

/** The curated From list. Long staleTime: it changes only from Settings, which invalidates it.
    Exported as options so an imperative `ensureQueryData` shares the hook's one definition; the
    `select` stays on the hook, since `ensureQueryData` answers the raw response either way. */
export const identitiesQueryOptions = (accountId: string) => ({
  queryKey: mailKeys.identities(accountId),
  queryFn: () => api.getIdentities({ accountId }) as Promise<IdentityListResponse>,
  staleTime: 5 * 60_000,
})

/** `pinnedAccountId` is the composer's — the From list has to be the bound account's, or the
    picker offers an address the send's account does not own and every attempt is refused. */
export function useIdentities(pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId

  return useQuery({
    ...identitiesQueryOptions(accountId),
    select: (data): SendingIdentity[] => data.identities,
  })
}

export function useReplaceIdentities() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (identities: { address: string; displayName: string; isDefault: boolean }[]) =>
      api.putIdentities(identities, { accountId }) as Promise<void>,
    // Settled, not success: after a refused PUT the page must fall back to the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.identities(accountId) }),
  })
}

/** The senders whose remote images load without asking. Long staleTime: it changes only from
    the reader, which invalidates it. The Set is built by `select` so the reader can test one
    address per render without rebuilding it. */
export function useTrustedSenders() {
  const accountId = useAccountId()

  return useQuery({
    queryKey: mailKeys.trustedSenders(accountId),
    queryFn: () => api.getTrustedSenders() as Promise<string[]>,
    staleTime: 5 * 60_000,
    select: (addresses): Set<string> => new Set(addresses),
  })
}

/** One mutation for both directions — the two differ by a verb, not by a workflow. */
export function useTrustSender(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ address, trusted }: { address: string; trusted: boolean }) =>
      (trusted ? api.trustSender(address) : api.untrustSender(address)) as Promise<void>,
    // A 400 is the server refusing on purpose, in words meant to be read — the account is at its
    // ceiling, or the header carried no readable address. Anything else is infrastructure, whose
    // statusText ("Internal Server Error") tells the reader nothing, so it gets the generic.
    // A 401 says nothing at all: api.js has already cleared the session and sent them to /login,
    // and a toast on top of a redirect is noise.
    onError: (error, { trusted }) => {
      if (error instanceof ApiError && error.status === 401) return
      const generic = trusted
        ? "Could not allow this sender's images"
        : "Could not block this sender's images"
      onError?.(error instanceof ApiError && error.status === 400 ? error.message : generic)
    },
    // Settled, not success: a refused call must leave the reader showing the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.trustedSenders(accountId) }),
  })
}

export function useAliases(enabled = true) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: mailKeys.aliases(accountId),
    queryFn: () => api.getAliases() as Promise<AliasInfo[]>,
    enabled,
    staleTime: 5 * 60_000,
  })
}

/**
 * Role mutations invalidate the roles AND the folder tree: the tree's labels are the chain's
 * output, so changing a role changes what the tree displays.
 */
function useRoleMutation<TArgs>(
  mutationFn: (args: TArgs, options: { accountId: string }) => Promise<unknown>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (args: TArgs) => mutationFn(args, { accountId }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folderRoles(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

export const useSetFolderRole = () =>
  useRoleMutation<{ role: string; folderPath: string }>(
    ({ role, folderPath }, options) => api.setFolderRole(role, folderPath, options))

export const useClearFolderRole = () =>
  useRoleMutation<{ role: string }>(({ role }, options) => api.clearFolderRole(role, options))

/**
 * Folder mutations all invalidate the tree: creating, renaming, deleting and subscribing each
 * change the hierarchy or the counts the tree displays.
 */
function useFolderMutation<TArgs>(
  mutationFn: (args: TArgs, options: { accountId: string }) => Promise<unknown>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (args: TArgs) => mutationFn(args, { accountId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) }),
  })
}

export const useCreateFolder = () =>
  useFolderMutation<{ parentPath: string; name: string }>(
    ({ parentPath, name }, options) => api.createMailFolder(parentPath, name, options))

export const useRenameFolder = () =>
  useFolderMutation<{ path: string; newParentPath: string; newName: string }>(
    ({ path, newParentPath, newName }, options) =>
      api.renameMailFolder(path, newParentPath, newName, options))

export const useDeleteFolder = () =>
  useFolderMutation<{ path: string }>(({ path }, options) => api.deleteMailFolder(path, options))

export const useSetFolderSubscription = () =>
  useFolderMutation<{ path: string; subscribed: boolean }>(
    ({ path, subscribed }, options) =>
      api.setMailFolderSubscription(path, subscribed, options))

export interface SetFlagsArgs {
  folderPath: string
  uids: number[]
  flag: MailFlagName
  value: boolean
}

type Snapshot = [readonly unknown[], unknown]

/**
 * Cancels only the caches an optimistic patch can actually rewrite. Cancelling a *first* load is
 * destructive: TanStack reverts it to pending with no data, no observer ever retries, and the
 * list reads "No messages" for good — which is what a deep link hit whenever the message detail
 * came back before the folder listing, marking it seen while that listing was still in flight.
 */
function cancelLoaded(queryClient: QueryClient, queryKey: QueryKey) {
  return queryClient.cancelQueries({
    queryKey, predicate: (query: Query) => query.state.data !== undefined,
  })
}

/**
 * Counts each uid's seen transition once across every cache, pages and stream blocks alike:
 * a uid sitting in two of them is one badge move, not two. `patchSummaries` stays the single
 * definition of what a transition is.
 */
function unreadTally(uids: number[], flag: MailFlagName, value: boolean) {
  const uncounted = flag === 'seen' ? new Set(uids) : null
  let delta = 0

  return {
    get delta() { return delta },
    count(messages: MailMessageSummary[]) {
      if (!uncounted?.size) return
      delta += patchSummaries(messages, [...uncounted], flag, value).unreadDelta
      for (const message of messages) uncounted.delete(message.uid)
    },
  }
}

/**
 * Optimistic across three caches — pages, stream blocks, folder unread — with snapshot
 * rollback. Never invalidates the stream (N blocks = N IMAP connections); the 60s poll
 * and highestModSeq are the truth mechanism, so there is no onSettled either.
 */
export function useSetFlags(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids, flag, value }: SetFlagsArgs) =>
      api.setMessageFlags(folderPath, uids, flag, value, { accountId }),

    onMutate: async ({ folderPath, uids, flag, value }: SetFlagsArgs) => {
      const pagesKey = mailKeys.messagesIn(accountId, folderPath)
      const streamKey = mailKeys.messageStreamIn(accountId, folderPath)
      await cancelLoaded(queryClient, pagesKey)
      await cancelLoaded(queryClient, streamKey)

      const snapshots: Snapshot[] = []
      // A cache holding no target retires nothing and counts nothing, so its silence stays
      // silence: only a cache that actually held the uid can move the badge.
      const tally = unreadTally(uids, flag, value)

      for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
        if (!page) continue
        const patch = patchSummaries(page.messages, uids, flag, value)
        if (patch.found === 0) continue
        snapshots.push([key, page])
        queryClient.setQueryData(key, { ...page, messages: patch.messages })
        tally.count(page.messages)
      }

      for (const [key, stream] of
        queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
        if (!stream) continue
        let found = 0
        // Every block holding the uid is patched; the tally counts it once, whichever block or
        // cache it turned up in first.
        const pages = stream.pages.map(page => {
          const patch = patchSummaries(page.messages, uids, flag, value)
          found += patch.found
          tally.count(page.messages)
          return patch.found ? { ...page, messages: patch.messages } : page
        })
        if (found === 0) continue
        snapshots.push([key, stream])
        queryClient.setQueryData(key, { ...stream, pages })
      }

      // Search caches are summaries too: the same patch, scoped to the mutated folder, with the
      // same snapshot rollback. They stay a snapshot otherwise — the poll never touches them.
      const searchKey = mailKeys.searchIn(accountId)
      await cancelLoaded(queryClient, searchKey)
      for (const [key, page] of queryClient.getQueriesData<MailSearchPage>({ queryKey: searchKey })) {
        if (!page) continue
        const patch = patchSearchResults(page.results, folderPath, uids, flag, value)
        if (patch.found === 0) continue
        snapshots.push([key, page])
        queryClient.setQueryData(key, { ...page, results: patch.results })
        tally.count(page.results.filter(row => row.folderPath === folderPath))
      }

      if (tally.delta !== 0) {
        const foldersKey = mailKeys.folders(accountId)
        const tree = queryClient.getQueryData<MailFolderNode[]>(foldersKey)
        if (tree) {
          snapshots.push([foldersKey, tree])
          queryClient.setQueryData(foldersKey, patchFolderUnread(tree, folderPath, tally.delta))
        }
      }

      return { snapshots }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not update the message')
    },
  })
}

/** The two list caches of one folder: pages and stream blocks, as prefixes. */
const listKeysOf = (accountId: string, folderPath: string) =>
  [mailKeys.messagesIn(accountId, folderPath), mailKeys.messageStreamIn(accountId, folderPath)]

async function cancelListQueries(
  queryClient: QueryClient, accountId: string, folderPath: string,
) {
  for (const queryKey of listKeysOf(accountId, folderPath)) {
    await cancelLoaded(queryClient, queryKey)
  }
}

/**
 * Counts each uid once across every cache, pages and stream blocks alike: a uid sitting in two
 * of them is one row leaving the folder, not two.
 */
function removalTally(uids: number[]) {
  const uncounted = new Set(uids)
  let removed = 0
  let removedUnread = 0

  return {
    get removed() { return removed },
    get removedUnread() { return removedUnread },
    count(messages: MailMessageSummary[]) {
      if (!uncounted.size) return
      for (const message of messages) {
        if (!uncounted.delete(message.uid)) continue
        removed += 1
        if (!message.seen) removedUnread += 1
      }
    },
  }
}

/** Drops the rows from every cached page and stream block of a folder, snapshotting each. */
function removeFromFolderCaches(
  queryClient: QueryClient, accountId: string, folderPath: string, uids: number[],
) {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  const snapshots: Snapshot[] = []
  const tally = removalTally(uids)

  for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
    if (!page) continue
    const patch = removeSummaries(page.messages, uids)
    if (patch.removed === 0) continue
    snapshots.push([key, page])
    tally.count(page.messages)
    queryClient.setQueryData(key, { ...page, messages: patch.messages })
  }

  for (const [key, stream] of
    queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
    if (!stream) continue
    let removed = 0
    const pages = stream.pages.map(page => {
      const patch = removeSummaries(page.messages, uids)
      removed += patch.removed
      tally.count(page.messages)
      return patch.removed ? { ...page, messages: patch.messages } : page
    })
    if (removed === 0) continue
    snapshots.push([key, stream])
    queryClient.setQueryData(key, { ...stream, pages })
  }

  // Search caches are summaries too: drop the mutated folder's rows and decrement that page's
  // total, snapshotting each. Same-folder rows only — a shared uid elsewhere is another message.
  for (const [key, page] of
    queryClient.getQueriesData<MailSearchPage>({ queryKey: mailKeys.searchIn(accountId) })) {
    if (!page) continue
    const removal = removeSearchResults(page.results, folderPath, uids)
    if (removal.removed === 0) continue
    snapshots.push([key, page])
    tally.count(page.results.filter(row => row.folderPath === folderPath))
    queryClient.setQueryData(key, {
      ...page, results: removal.results, total: Math.max(0, page.total - removal.removed),
    })
  }

  return { snapshots, removed: tally.removed, removedUnread: tally.removedUnread }
}

/**
 * Empties a folder's cached pages and blocks in place — messages cleared, total zeroed —
 * snapshotting each. Unlike dropFolderCaches this keeps the queries, so the open source folder
 * shows empty at once; removing them would refetch the rows the server has not expunged yet.
 */
function blankFolderCaches(
  queryClient: QueryClient, accountId: string, folderPath: string,
): Snapshot[] {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  const snapshots: Snapshot[] = []

  for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
    if (!page) continue
    snapshots.push([key, page])
    queryClient.setQueryData(key, { ...page, messages: [], total: 0 })
  }

  for (const [key, stream] of
    queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
    if (!stream) continue
    snapshots.push([key, stream])
    queryClient.setQueryData(key, {
      ...stream, pages: stream.pages.map(page => ({ ...page, messages: [], total: 0 })),
    })
  }

  return snapshots
}

/**
 * Snapshots then *removes* the target folder's caches. Removal refetches nothing until the
 * folder is shown; an invalidate would replay every loaded stream block.
 */
function dropFolderCaches(queryClient: QueryClient, accountId: string, folderPath: string) {
  const snapshots: Snapshot[] = []

  for (const queryKey of listKeysOf(accountId, folderPath)) {
    for (const [key, data] of queryClient.getQueriesData({ queryKey })) {
      if (data !== undefined) snapshots.push([key, data])
    }
    queryClient.removeQueries({ queryKey })
  }

  return snapshots
}

/** One read, one write: two patches of the same tree would snapshot an already-patched one. */
function patchTreeCounts(
  queryClient: QueryClient, accountId: string,
  patches: [folderPath: string, deltas: FolderCountDeltas][],
): Snapshot[] {
  const foldersKey = mailKeys.folders(accountId)
  const tree = queryClient.getQueryData<MailFolderNode[]>(foldersKey)
  if (!tree) return []

  const patched = patches.reduce(
    (current, [folderPath, deltas]) => patchFolderCounts(current, folderPath, deltas), tree)
  if (patched === tree) return []

  queryClient.setQueryData(foldersKey, patched)
  return [[foldersKey, tree]]
}

export interface MoveMessagesArgs {
  folderPath: string
  uids: number[]
  targetFolderPath: string
  copy: boolean
}

/**
 * Optimistic across both folders: the source loses its rows, the target loses its caches —
 * dropped rather than invalidated — and both folders' counters move. Snapshot rollback,
 * never an invalidate, no onSettled: the 60s poll is the truth mechanism.
 */
export function useMoveMessages(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids, targetFolderPath, copy }: MoveMessagesArgs) =>
      copy
        ? api.copyMessages(folderPath, uids, targetFolderPath, { accountId })
        : api.moveMessages(folderPath, uids, targetFolderPath, { accountId }),

    onMutate: async ({ folderPath, uids, targetFolderPath, copy }: MoveMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)
      // removeFromFolderCaches writes the search cache too: cancel an in-flight *refetch* of a
      // loaded search, so a late resolve can't repopulate the removed row on a success path that
      // never rolls back. A first load is left to land (cancelLoaded); onSettled reconciles it.
      await cancelLoaded(queryClient, mailKeys.searchIn(accountId))

      const snapshots: Snapshot[] = []
      const patches: [string, FolderCountDeltas][] = []
      // A copy removes nothing, so how many of the batch were unread is unknowable here without
      // scanning for it alone; the target badge waits for the poll instead.
      let added = { total: uids.length, unread: 0 }

      if (!copy) {
        const source = removeFromFolderCaches(queryClient, accountId, folderPath, uids)
        snapshots.push(...source.snapshots)
        patches.push([folderPath, { total: -source.removed, unread: -source.removedUnread }])
        // Target mirrors what actually left the source, not uids.length, so a cold source
        // cache can't inflate it past the source's own drop. Consequence: an uncached source
        // moves neither folder's counters until the next poll.
        added = { total: source.removed, unread: source.removedUnread }
      }

      // Mirror the source: cancel an in-flight refetch of a loaded target cache before dropping
      // it, so a late resolve can't repopulate what we just removed and race the rollback. A
      // first load survives cancelLoaded; removeQueries below destroys it outright anyway.
      await cancelListQueries(queryClient, accountId, targetFolderPath)
      snapshots.push(...dropFolderCaches(queryClient, accountId, targetFolderPath))
      patches.push([targetFolderPath, added])
      snapshots.push(...patchTreeCounts(queryClient, accountId, patches))

      return { snapshots, copy }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.(context?.copy ? 'Could not copy the message' : 'Could not move the message')
    },

    // A removal shrank the active search page and left sibling pages a stale total, and no poll
    // touches search — so reconcile the mounted search against the server, which re-windows it.
    // A copy leaves the source intact: nothing to reconcile.
    onSettled: (_data, _error, { copy }) => {
      if (!copy) queryClient.invalidateQueries({ queryKey: mailKeys.searchIn(accountId) })
    },
  })
}

export interface DeleteMessagesArgs {
  folderPath: string
  uids: number[]
}

/**
 * The source half of a move: the rows are gone and no folder receives them.
 * `pinnedAccountId` is the composer's: a draft belongs to the mailbox it was written in, and a
 * switch under an open composer must not send its staged ids somewhere else. Every other caller
 * omits it and follows the active account. Same for useSendMessage and useSaveDraft below.
 */
export function useDeleteMessages(onError?: (message: string) => void, pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids }: DeleteMessagesArgs) =>
      api.deleteMessages(folderPath, uids, { accountId }),

    onMutate: async ({ folderPath, uids }: DeleteMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)
      // removeFromFolderCaches writes the search cache too: cancel an in-flight *refetch* of a
      // loaded search, so a late resolve can't repopulate the removed row on a success path that
      // never rolls back. A first load is left to land (cancelLoaded); onSettled reconciles it.
      await cancelLoaded(queryClient, mailKeys.searchIn(accountId))

      const source = removeFromFolderCaches(queryClient, accountId, folderPath, uids)
      const tree = patchTreeCounts(queryClient, accountId,
        [[folderPath, { total: -source.removed, unread: -source.removedUnread }]])

      return { snapshots: [...source.snapshots, ...tree] }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not delete the message')
    },

    // The removal re-windows the active search page; reconcile it against the server (see move).
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: mailKeys.searchIn(accountId) }),
  })
}

export interface SendMessageArgs {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  htmlBody: string
  attachmentIds: string[]
  /** Omitted picks the account's own address server-side; the display label is always server-resolved. */
  fromAddress?: string
  /** Threading of a reply/forward: the original's id and its extended references chain. */
  inReplyTo?: string
  references?: string[]
}

export interface SendMessageResult { appendedToSent: boolean }

/**
 * Sends a composed message. On success invalidates the folder tree (the Sent copy changes its
 * counts) plus the Sent folder's own message queries, found in the cached tree by specialUse.
 */
export function useSendMessage(pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SendMessageArgs) =>
      api.sendMessage(args, { accountId }) as Promise<SendMessageResult>,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      const folders = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const sent = folders ? flatten(folders).find(entry => entry.node.specialUse === 'sent') : undefined
      if (sent) {
        queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, sent.node.path) })
        queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, sent.node.path) })
      }
    },
  })
}

/**
 * Stages the quotable body server-side. A mutation, not a query: every call stages files
 * (a side effect with a TTL), so the result must never be cached or replayed.
 */
export function usePrepareQuote() {
  const accountId = useAccountId()

  return useMutation({
    mutationFn: (args: { folder: string; uid: number; purpose: QuotePurpose }) =>
      api.prepareQuote(args.folder, args.uid, args.purpose, { accountId }) as Promise<PreparedQuote>,
  })
}

export type SaveDraftArgs = SendMessageArgs & { replaceUid?: number }

/** Files the draft under the drafts role; each success replaces the version before it. */
export function useSaveDraft(pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SaveDraftArgs) => api.saveDraft(args, { accountId }) as Promise<SavedDraft>,
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, saved.folderPath) })
      queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, saved.folderPath) })
    },
  })
}

/**
 * Opens a draft for the composer. A mutation, not a query: every call re-stages the draft's
 * parts (a side effect with a TTL), so the result must never be cached or replayed.
 */
export function useOpenDraft() {
  const accountId = useAccountId()

  return useMutation({
    mutationFn: (args: { folder: string; uid: number }) =>
      api.openDraft(args.folder, args.uid, { accountId }) as Promise<OpenedDraft>,
  })
}

export interface EmptyFolderArgs {
  folderPath: string
  /** Blank/absent = purge; set = move every message into this folder. */
  targetFolderPath?: string | null
}

/**
 * Empties a whole folder. The open source's caches are emptied in place and its counts zeroed;
 * a move also adds the source's own total/unread (read from the tree node) to the target.
 * Optimistic with snapshot rollback, never an invalidate — the 60s poll reconciles.
 */
export function useEmptyFolder(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, targetFolderPath }: EmptyFolderArgs) =>
      api.emptyFolder(folderPath, targetFolderPath ?? null, { accountId }),

    onMutate: async ({ folderPath, targetFolderPath }: EmptyFolderArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)

      // Empty in place, not removeQueries: the source is the folder on screen, and removing its
      // query refetches the rows the server has not expunged yet, racing them back until the poll.
      const snapshots: Snapshot[] = blankFolderCaches(queryClient, accountId, folderPath)

      // The source folder's own counts drive both the zeroing and, on a move, the target's gain.
      const tree = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const node = tree ? flatten(tree).find(entry => entry.node.path === folderPath)?.node : undefined
      const source = { total: node?.total ?? 0, unread: node?.unread ?? 0 }

      const patches: [string, FolderCountDeltas][] = [
        [folderPath, { total: -source.total, unread: -source.unread }],
      ]

      const move = !!targetFolderPath
      if (move) {
        await cancelListQueries(queryClient, accountId, targetFolderPath!)
        snapshots.push(...dropFolderCaches(queryClient, accountId, targetFolderPath!))
        patches.push([targetFolderPath!, { total: source.total, unread: source.unread }])
      }

      snapshots.push(...patchTreeCounts(queryClient, accountId, patches))
      return { snapshots }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not empty the folder')
    },
  })
}
