import {
  useMutation, useQueryClient,
  type InfiniteData, type Query, type QueryClient, type QueryKey,
} from '@tanstack/react-query'
import i18next from 'i18next'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import {
  BLOCK_SIZE, groupConversationsOf, isStreaming, type Preferences,
} from '../../hooks/usePreferences'
import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchPage, MailSearchResult, MailThread,
} from './api/mailTypes'
import {
  blankPage, pageSummaries, patchFolderCounts, patchFolderUnread, patchPage, patchSearchResults,
  patchSummaries, removeFromPage, removeSearchResults,
  type FolderCountDeltas, type MailFlagName,
} from './list/listPatch'
import { dedupeByUid } from './list/messageStream'
import { dedupeThreads } from './list/threading'
import { mailKeys } from './mailKeys'

export interface SetFlagsArgs {
  folderPath: string
  uids: number[]
  flag: MailFlagName
  value: boolean
}

export type Snapshot = [readonly unknown[], unknown]

/** Puts back every cache an optimistic write patched; a mutation that never got that far has none. */
export function restoreSnapshots(queryClient: QueryClient, context: { snapshots: Snapshot[] } | undefined) {
  for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
}

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
      restoreSnapshots(queryClient, context)
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

// Each row goes back after the nearest row that stood before it and is still there, else first.
function reinsert<T>(
  before: T[], now: T[], restored: (row: T) => T | null, same: (a: T, b: T) => boolean,
): T[] {
  const rows = [...now]
  before.forEach((row, index) => {
    const back = restored(row)
    if (!back || rows.some(kept => same(kept, back))) return
    let at = 0
    for (let earlier = index - 1; earlier >= 0 && at === 0; earlier--) {
      at = rows.findIndex(kept => same(kept, before[earlier]!)) + 1
    }
    rows.splice(at, 0, back)
  })
  return rows
}

const sameUid = (a: MailMessageSummary, b: MailMessageSummary) => a.uid === b.uid
const sameThread = (a: MailThread, b: MailThread) =>
  a.messages.some(member => b.messages.some(other => other.uid === member.uid))

function reinsertIntoPage(before: MailFolderPage, now: MailFolderPage, uids: Set<number>): MailFolderPage {
  const pick = (row: MailMessageSummary) => (uids.has(row.uid) ? row : null)
  const messages = reinsert(before.messages, now.messages, pick, sameUid)
  if (!before.threads || !now.threads) return { ...now, messages }

  // Members back into the threads still listed, then the threads the removal emptied.
  const origins = before.threads
  const grown = now.threads.map(thread => {
    const origin = origins.find(old => sameThread(old, thread))
    return origin ? { messages: reinsert(origin.messages, thread.messages, pick, sameUid) } : thread
  })
  const threads = reinsert(origins, grown, old => {
    const back = old.messages.filter(member => uids.has(member.uid))
    return back.length > 0 ? { messages: back } : null
  }, sameThread)
  return { ...now, messages, threads }
}

/** Undoes removeFromFolderCaches on the caches as they now stand: a write made since (a star,
    another removal) keeps its effect, which restoring the snapshots would revert. */
export function reinsertIntoFolderCaches(
  queryClient: QueryClient, folderPath: string, uids: number[], snapshots: Snapshot[],
) {
  const targets = new Set(uids)
  const pickResult = (row: MailSearchResult) =>
    (row.folderPath === folderPath && targets.has(row.uid) ? row : null)
  const sameResult = (a: MailSearchResult, b: MailSearchResult) =>
    a.uid === b.uid && a.folderPath === b.folderPath

  for (const [key, before] of snapshots) {
    if (key[2] === 'messages') {
      queryClient.setQueryData<MailFolderPage>(key, now =>
        now && reinsertIntoPage(before as MailFolderPage, now, targets))
    } else if (key[2] === 'messageStream') {
      const blocks = (before as InfiniteData<MailFolderPage>).pages
      queryClient.setQueryData<InfiniteData<MailFolderPage>>(key, now => now && {
        ...now,
        pages: now.pages.map((page, index) =>
          blocks[index] ? reinsertIntoPage(blocks[index], page, targets) : page),
      })
    } else if (key[2] === 'search') {
      queryClient.setQueryData<MailSearchPage>(key, now => {
        if (!now) return now
        const results = reinsert((before as MailSearchPage).results, now.results, pickResult, sameResult)
        return { ...now, results, total: now.total + results.length - now.results.length }
      })
    }
  }
}

/** Fetches block 0 alone and merges it in. Never invalidates: that would refetch EVERY loaded
    block — forty blocks would be forty IMAP connections and forty full folder sorts. */
async function refreshFirstBlock(
  client: QueryClient, accountId: string, folder: string, grouped: boolean,
) {
  const key = mailKeys.messageStream(accountId, folder, BLOCK_SIZE, grouped)
  try {
    const fresh: MailFolderPage =
      await api.getMailMessages(folder, 0, BLOCK_SIZE, { accountId, grouped })
    client.setQueryData<InfiniteData<MailFolderPage>>(key, old => {
      if (!old) return old
      // A cached InfiniteData always holds the block it was seeded with; fresh stands in for a
      // head that somehow isn't there rather than asserting one.
      const [head, ...rest] = old.pages
      const previousHead = head ?? fresh
      return {
        ...old,
        // Merged, not replaced: arrivals push old block-0 rows out of the fresh window,
        // and the frozen later blocks do not hold them — a replace would drop them.
        // Grouped, the unit is the thread: a reply joins its own rather than opening a row.
        pages: [
          grouped
            ? {
                ...fresh,
                threads: dedupeThreads([fresh, previousHead])
                  .map(group => ({ messages: group.messages })),
              }
            : { ...fresh, messages: dedupeByUid([fresh, previousHead]) },
          ...rest,
        ],
      }
    })
  } catch {
    // A poll-driven refresh fails in silence; the next tick tries again.
  }
}

/** Reloads the folder's list as the poll does when it moved: block 0 streaming, the pages otherwise. */
export function refreshFolderList(
  client: QueryClient, accountId: string, folder: string, preferences: Preferences,
) {
  if (isStreaming(preferences)) {
    return refreshFirstBlock(client, accountId, folder, groupConversationsOf(preferences))
  }
  return client.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, folder) })
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

// dropFolderCaches, unless the folder is open right now: removing an observed query blanks it for a
// render before a refetch could refill it, so an open folder is invalidated instead — pages only,
// never the stream (would replay every loaded block); the caller's own tree invalidation refreshes it.
export function dropOrRefreshFolderCaches(
  queryClient: QueryClient, accountId: string, folderPath: string,
) {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  const observed = [pagesKey, streamKey].some(
    queryKey => queryClient.getQueryCache().findAll({ queryKey, type: 'active' }).length > 0)

  if (observed) {
    void queryClient.invalidateQueries({ queryKey: pagesKey })
    return
  }
  dropFolderCaches(queryClient, accountId, folderPath)
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
