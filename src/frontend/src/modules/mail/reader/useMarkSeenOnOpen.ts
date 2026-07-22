import { useEffect, useRef } from 'react'
import { useQueryClient, type InfiniteData, type QueryClient } from '@tanstack/react-query'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { mailKeys, useAccountId, useSetFlags } from '../queries'

/** A cached view of one message — the first match, pages then stream blocks. */
export function findCachedSummary(
  queryClient: QueryClient, accountId: string, folderPath: string, uid: number,
): MailMessageSummary | undefined {
  for (const [, page] of queryClient.getQueriesData<MailFolderPage>(
    { queryKey: mailKeys.messagesIn(accountId, folderPath) })) {
    const hit = page?.messages.find(message => message.uid === uid)
    if (hit) return hit
  }
  for (const [, stream] of queryClient.getQueriesData<InfiniteData<MailFolderPage>>(
    { queryKey: mailKeys.messageStreamIn(accountId, folderPath) })) {
    for (const page of stream?.pages ?? []) {
      const hit = page.messages.find(message => message.uid === uid)
      if (hit) return hit
    }
  }
  return undefined
}

/**
 * Marks a message read once per opening — armed on uid change, fired when the detail arrives.
 * Failure is silent by design: the next poll corrects it.
 */
export function useMarkSeenOnOpen(
  folderPath: string | null, uid: number | null, detailLoaded: boolean,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const { mutate } = useSetFlags()
  const firedFor = useRef<string | null>(null)

  useEffect(() => {
    // The arming dies with the opening, so uid → null → the same uid opens it afresh. A detail
    // that is merely not loaded yet is still the same opening.
    if (!folderPath || uid === null) { firedFor.current = null; return }
    if (!detailLoaded) return

    const opening = `${folderPath} ${uid}`
    // Armed by uid, not by the cached flag: marking it unread again must not flip it back.
    if (firedFor.current === opening) return
    firedFor.current = opening

    // No cached summary (deep link) fires too: an idempotent STORE \Seen costs nothing.
    const summary = findCachedSummary(queryClient, accountId, folderPath, uid)
    if (summary?.seen) return

    mutate({ folderPath, uids: [uid], flag: 'seen', value: true })
  }, [folderPath, uid, detailLoaded, accountId, queryClient, mutate])
}
