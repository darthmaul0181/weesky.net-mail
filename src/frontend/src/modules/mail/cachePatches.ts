import {
  useMutation, useQueryClient,
  type InfiniteData, type Query, type QueryClient, type QueryKey,
} from '@tanstack/react-query'
import i18next from 'i18next'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchPage,
} from './api/mailTypes'
import {
  blankPage, pageSummaries, patchFolderCounts, patchFolderUnread, patchPage, patchSearchResults,
  patchSummaries, removeFromPage, removeSearchResults,
  type FolderCountDeltas, type MailFlagName,
} from './list/listPatch'
import { mailKeys } from './mailKeys'

export interface SetFlagsArgs {
  folderPath: string
  uids: number[]
  flag: MailFlagName
  value: boolean
}

export type Snapshot = [readonly unknown[], unknown]

// Only caches holding data: cancelling a first load reverts it to pending with no data and no
// observer retries, so a deep link whose detail beat the folder listing read "No messages" for good.
export function cancelLoaded(queryClient: QueryClient, queryKey: QueryKey) {
  return queryClient.cancelQueries({
    queryKey, predicate: (query: Query) => query.state.data !== undefined,
  })
}

// A uid in two caches (a page and a stream block) is one badge move, not two; `patchSummaries` stays
// the single definition of a transition.
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

// Optimistic over pages, stream blocks and folder unread, with snapshot rollback. Never invalidates the
// stream (N blocks = N IMAP connections): the 60s poll and highestModSeq are the truth, so no onSettled.
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
        const patch = patchPage(page, uids, flag, value)
        if (patch.found === 0) continue
        snapshots.push([key, page])
        queryClient.setQueryData(key, patch.page)
        tally.count(pageSummaries(page))
      }

      for (const [key, stream] of
        queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
        if (!stream) continue
        let found = 0
        // Every block holding the uid is patched; the tally counts it once, whichever block or
        // cache it turned up in first.
        const pages = stream.pages.map(page => {
          const patch = patchPage(page, uids, flag, value)
          found += patch.found
          tally.count(pageSummaries(page))
          return patch.found ? patch.page : page
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
      onError?.(i18next.t('mail:mutations.updateFailed'))
    },
  })
}

/** The two list caches of one folder: pages and stream blocks, as prefixes. */
export const listKeysOf = (accountId: string, folderPath: string) =>
  [mailKeys.messagesIn(accountId, folderPath), mailKeys.messageStreamIn(accountId, folderPath)]

export async function cancelListQueries(
  queryClient: QueryClient, accountId: string, folderPath: string,
) {
  for (const queryKey of listKeysOf(accountId, folderPath)) {
    await cancelLoaded(queryClient, queryKey)
  }
}

// A uid in two caches is one row leaving the folder, not two.
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
export function removeFromFolderCaches(
  queryClient: QueryClient, accountId: string, folderPath: string, uids: number[],
) {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  const snapshots: Snapshot[] = []
  const tally = removalTally(uids)

  for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
    if (!page) continue
    const patch = removeFromPage(page, uids)
    if (patch.removed === 0) continue
    snapshots.push([key, page])
    tally.count(pageSummaries(page))
    queryClient.setQueryData(key, patch.page)
  }

  for (const [key, stream] of
    queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
    if (!stream) continue
    let removed = 0
    const pages = stream.pages.map(page => {
      const patch = removeFromPage(page, uids)
      removed += patch.removed
      tally.count(pageSummaries(page))
      return patch.removed ? patch.page : page
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

// Empties pages and blocks in place, snapshotting each. Unlike dropFolderCaches it keeps the queries:
// removing them would refetch rows the server has not expunged yet.
export function blankFolderCaches(
  queryClient: QueryClient, accountId: string, folderPath: string,
): Snapshot[] {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  const snapshots: Snapshot[] = []

  for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
    if (!page) continue
    snapshots.push([key, page])
    queryClient.setQueryData(key, blankPage(page))
  }

  for (const [key, stream] of
    queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
    if (!stream) continue
    snapshots.push([key, stream])
    queryClient.setQueryData(key, { ...stream, pages: stream.pages.map(blankPage) })
  }

  return snapshots
}

// Removal refetches nothing until the folder is shown; an invalidate would replay every stream block.
export function dropFolderCaches(queryClient: QueryClient, accountId: string, folderPath: string) {
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
export function patchTreeCounts(
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
