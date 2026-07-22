import {
  useInfiniteQuery, useMutation, useQuery, useQueryClient,
  type InfiniteData, type QueryClient,
} from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAuth } from '../../contexts/AuthContext'
import { notifiesOf, usePreferences } from '../../hooks/usePreferences'
import type {
  MailFolderNode, MailFolderPage, MailMessageDetail, MailMessageSummary, FolderRoleEntry,
} from './api/mailTypes'
import {
  patchFolderCounts, patchFolderUnread, patchSummaries, removeSummaries,
  type FolderCountDeltas, type MailFlagName,
} from './list/listPatch'
import { nextBlockIndex } from './list/messageStream'


// The prefixes are the keys minus their trailing size arguments: what a mutation or a refresh
// matches on, since it patches a folder's every cached page whatever size it was fetched at.
const messagesIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messages', folder] as const
const messageStreamIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messageStream', folder] as const

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
  // Its own key: what it caches is not a page but a sequence of pages, and mixing the two
  // shapes under one key is a type error that only shows at runtime.
  messageStreamIn,
  messageStream: (accountId: string, folder: string, requestSize: number) =>
    [...messageStreamIn(accountId, folder), requestSize] as const,
  folderRoles: (accountId: string) => ['mail', accountId, 'folderRoles'] as const,
  /** Mutation key, not a query key: it is what lets the poll tell our own writes from a change.
      Carried by every write — flags, move, copy, delete — since each patches the same counters. */
  writes: (accountId: string) => ['mail', accountId, 'writes'] as const,
}

export function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}

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
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
    enabled,
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: notifies,
  })
}

export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, pageSize, { signal }),
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
      api.getMailMessages(folderPath, pageParam, requestSize, { signal }) as Promise<MailFolderPage>,
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
    queryFn: ({ signal }) => api.getMailMessage(folderPath, uid, { signal }),
    enabled: folderPath !== null && uid !== null,
  })
}

export function useFolderRoles() {
  const accountId = useAccountId()

  return useQuery<FolderRoleEntry[]>({
    queryKey: mailKeys.folderRoles(accountId),
    queryFn: ({ signal }) => api.getFolderRoles({ signal }),
  })
}

/**
 * Role mutations invalidate the roles AND the folder tree: the tree's labels are the chain's
 * output, so changing a role changes what the tree displays.
 */
function useRoleMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folderRoles(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

export const useSetFolderRole = () =>
  useRoleMutation<{ role: string; folderPath: string }>(
    ({ role, folderPath }) => api.setFolderRole(role, folderPath))

export const useClearFolderRole = () =>
  useRoleMutation<{ role: string }>(({ role }) => api.clearFolderRole(role))

/**
 * Folder mutations all invalidate the tree: creating, renaming, deleting and subscribing each
 * change the hierarchy or the counts the tree displays.
 */
function useFolderMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) }),
  })
}

export const useCreateFolder = () =>
  useFolderMutation<{ parentPath: string; name: string }>(
    ({ parentPath, name }) => api.createMailFolder(parentPath, name))

export const useRenameFolder = () =>
  useFolderMutation<{ path: string; newParentPath: string; newName: string }>(
    ({ path, newParentPath, newName }) => api.renameMailFolder(path, newParentPath, newName))

export const useDeleteFolder = () =>
  useFolderMutation<{ path: string }>(({ path }) => api.deleteMailFolder(path))

export const useSetFolderSubscription = () =>
  useFolderMutation<{ path: string; subscribed: boolean }>(
    ({ path, subscribed }) => api.setMailFolderSubscription(path, subscribed))

export interface SetFlagsArgs {
  folderPath: string
  uids: number[]
  flag: MailFlagName
  value: boolean
}

type Snapshot = [readonly unknown[], unknown]

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
      api.setMessageFlags(folderPath, uids, flag, value),

    onMutate: async ({ folderPath, uids, flag, value }: SetFlagsArgs) => {
      const pagesKey = mailKeys.messagesIn(accountId, folderPath)
      const streamKey = mailKeys.messageStreamIn(accountId, folderPath)
      await queryClient.cancelQueries({ queryKey: pagesKey })
      await queryClient.cancelQueries({ queryKey: streamKey })

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
    await queryClient.cancelQueries({ queryKey })
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

  return { snapshots, removed: tally.removed, removedUnread: tally.removedUnread }
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
        ? api.copyMessages(folderPath, uids, targetFolderPath)
        : api.moveMessages(folderPath, uids, targetFolderPath),

    onMutate: async ({ folderPath, uids, targetFolderPath, copy }: MoveMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)

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

      // Mirror the source: cancel any in-flight target fetch before dropping its caches, so a
      // late resolve can't repopulate what we just removed and race the rollback.
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
  })
}

export interface DeleteMessagesArgs {
  folderPath: string
  uids: number[]
}

/** The source half of a move: the rows are gone and no folder receives them. */
export function useDeleteMessages(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids }: DeleteMessagesArgs) =>
      api.deleteMessages(folderPath, uids),

    onMutate: async ({ folderPath, uids }: DeleteMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)

      const source = removeFromFolderCaches(queryClient, accountId, folderPath, uids)
      const tree = patchTreeCounts(queryClient, accountId,
        [[folderPath, { total: -source.removed, unread: -source.removedUnread }]])

      return { snapshots: [...source.snapshots, ...tree] }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not delete the message')
    },
  })
}
