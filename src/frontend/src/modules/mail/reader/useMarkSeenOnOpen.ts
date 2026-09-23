import { useCallback, useEffect, useRef, useSyncExternalStore } from 'react'
import {
  matchQuery, useQueryClient, type InfiniteData, type QueryClient,
} from '@tanstack/react-query'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { pageSummaries } from '../list/listPatch'
import { listKeysOf, useAccountId, useSetFlags, type SetFlagsArgs } from '../queries'

/** A cached view of one message — the first match, pages then stream blocks. */
export function findCachedSummary(
  queryClient: QueryClient, accountId: string, folderPath: string, uid: number,
): MailMessageSummary | undefined {
  const [pagesKey, streamKey] = listKeysOf(accountId, folderPath)
  // pageSummaries, not `page.messages`: a grouped page holds its rows in `threads` alone.
  for (const [, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
    const hit = page && pageSummaries(page).find(message => message.uid === uid)
    if (hit) return hit
  }
  for (const [, stream] of queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
    for (const page of stream?.pages ?? []) {
      const hit = pageSummaries(page).find(message => message.uid === uid)
      if (hit) return hit
    }
  }
  return undefined
}

/** The cached summary's flags, followed live: a star set from the list row patches the list
    caches alone. No summary (deep link): read and unstarred, since opening just marked it read. */
export function useCachedSummaryFlags(
  folderPath: string | null, uid: number | null,
): { seen: boolean; flagged: boolean } {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  // Only data events on this folder's lists: the lookup below scans every cached row, and the
  // cache reports every observer tick of every module.
  const subscribe = useCallback((onChange: () => void) => {
    if (folderPath === null) return () => {}
    const lists = listKeysOf(accountId, folderPath)
    return queryClient.getQueryCache().subscribe(event => {
      if (event.type !== 'added' && event.type !== 'removed' && event.type !== 'updated') return
      if (lists.some(queryKey => matchQuery({ queryKey }, event.query))) onChange()
    })
  }, [queryClient, accountId, folderPath])
  // A string, not an object: useSyncExternalStore compares snapshots with Object.is.
  const snapshot = useSyncExternalStore(subscribe, () => {
    const summary = folderPath !== null && uid !== null
      ? findCachedSummary(queryClient, accountId, folderPath, uid) : undefined
    return `${summary?.seen ?? true}|${summary?.flagged ?? false}`
  })
  const [seen, flagged] = snapshot.split('|')
  return { seen: seen === 'true', flagged: flagged === 'true' }
}

/** Whether either list cache of the folder holds a page yet. */
function listingLanded(queryClient: QueryClient, accountId: string, folderPath: string): boolean {
  return listKeysOf(accountId, folderPath)
    .some(queryKey => queryClient.getQueriesData({ queryKey }).some(([, data]) => data !== undefined))
}

/** A Mark as unread on the same message, issued after `armedAt` — the only write that outranks
    our reconcile. The cache keeps mutations for gcTime, so one from an earlier opening is still
    sitting there and says nothing about this one. */
function markedUnreadSince(
  queryClient: QueryClient, folderPath: string, uid: number, armedAt: number,
): boolean {
  return queryClient.getMutationCache().getAll().some(mutation => {
    const args = mutation.state.variables as SetFlagsArgs | undefined
    return args?.flag === 'seen' && args.value === false
      && args.folderPath === folderPath && args.uids.includes(uid)
      && mutation.state.submittedAt >= armedAt
  })
}

/** Marks a message read once per opening, when its detail arrives. Failure is silent: the next poll
 * corrects it. */
export function useMarkSeenOnOpen(
  folderPath: string | null, uid: number | null, detailLoaded: boolean,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const { mutate } = useSetFlags()
  const firedFor = useRef<string | null>(null)
  // The opening whose listing has still to arrive, kept apart from the arming so StrictMode's
  // second invoke — which the arming absorbs — resubscribes rather than losing the watch.
  const awaitingListing = useRef<string | null>(null)

  useEffect(() => {
    // The arming dies with the opening, so uid → null → the same uid opens it afresh. A detail
    // that is merely not loaded yet is still the same opening.
    if (!folderPath || uid === null) { firedFor.current = null; awaitingListing.current = null; return }
    if (!detailLoaded) return

    const opening = `${folderPath} ${uid}`
    const mark = () => mutate({ folderPath, uids: [uid], flag: 'seen', value: true })

    // Armed by uid, not by the cached flag: marking it unread again must not flip it back.
    if (firedFor.current !== opening) {
      firedFor.current = opening
      // No cached summary (deep link) fires too: an idempotent STORE \Seen costs nothing.
      const summary = findCachedSummary(queryClient, accountId, folderPath, uid)
      if (summary?.seen) return
      mark()
      // A patched summary needs no reconcile; nothing cached means the listing is still coming.
      awaitingListing.current = summary ? null : opening
    }
    if (awaitingListing.current !== opening) return

    // The listing carries the server's pre-STORE flags and would put the row back to unread, so
    // mark once more when it lands — the same idempotent STORE, leaving the patch one definition
    // rather than growing a second one here.
    let stopped = false
    const stop = () => { stopped = true; unsubscribe() }
    const armedAt = Date.now()

    const unsubscribe = queryClient.getQueryCache().subscribe(() => {
      if (stopped || !listingLanded(queryClient, accountId, folderPath)) return
      stop()
      awaitingListing.current = null
      const listed = findCachedSummary(queryClient, accountId, folderPath, uid)
      if (listed && !listed.seen && !markedUnreadSince(queryClient, folderPath, uid, armedAt)) mark()
    })
    return stop
  }, [folderPath, uid, detailLoaded, accountId, queryClient, mutate])
}
