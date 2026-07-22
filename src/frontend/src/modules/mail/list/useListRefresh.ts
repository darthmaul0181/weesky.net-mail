import { useEffect, useRef } from 'react'
import { useQueryClient, type InfiniteData, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { BLOCK_SIZE, isStreaming, usePreferences } from '../../../hooks/usePreferences'
import type { MailFolderPage } from '../api/mailTypes'
import { flatten } from '../folders/folderNodes'
import { mailKeys, useAccountId, useFolders } from '../queries'
import { folderChanged, snapshotOf, uidValidityBroke, type FolderSnapshot } from './folderDelta'
import { dedupeByUid } from './messageStream'

/** Fetches block 0 alone and merges it in. Never invalidates: that would refetch EVERY loaded
    block — forty blocks would be forty IMAP connections and forty full folder sorts. */
async function refreshFirstBlock(client: QueryClient, accountId: string, folder: string) {
  const key = mailKeys.messageStream(accountId, folder, BLOCK_SIZE)
  try {
    const fresh: MailFolderPage = await api.getMailMessages(folder, 0, BLOCK_SIZE)
    client.setQueryData<InfiniteData<MailFolderPage>>(key, old =>
      old
        ? {
            ...old,
            // Merged, not replaced: arrivals push old block-0 rows out of the fresh window,
            // and the frozen later blocks do not hold them — a replace would drop them.
            pages: [
              { ...fresh, messages: dedupeByUid([fresh, old.pages[0]]) },
              ...old.pages.slice(1),
            ],
          }
        : old)
  } catch {
    // A poll-driven refresh fails in silence; the next tick tries again.
  }
}

/**
 * Watches the polled folder listing and refreshes the displayed list when its folder moved.
 * The first observation of a folder is the baseline and never triggers.
 */
export function useListRefresh(folderPath: string | null): void {
  const accountId = useAccountId()
  const client = useQueryClient()
  const { data: folders } = useFolders()
  const { data: preferences } = usePreferences()
  const previous = useRef<{ path: string; snapshot: FolderSnapshot } | null>(null)

  useEffect(() => {
    if (!folderPath || !folders || !preferences) return
    const node = flatten(folders).find(entry => entry.node.path === folderPath)?.node
    if (!node) return

    const snapshot = snapshotOf(node)
    const last = previous.current
    previous.current = { path: folderPath, snapshot }
    if (!last || last.path !== folderPath) return

    if (uidValidityBroke(last.snapshot, snapshot)) {
      // Every cached UID is a lie. resetQueries refetches only what is on screen, from
      // scratch — an invalidate would replay every loaded stream block.
      client.resetQueries({
        predicate: query =>
          query.queryKey[0] === 'mail' && query.queryKey[1] === accountId
          && query.queryKey[3] === folderPath,
      })
      return
    }

    if (!folderChanged(last.snapshot, snapshot)) return

    if (isStreaming(preferences)) {
      refreshFirstBlock(client, accountId, folderPath)
    } else {
      client.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, folderPath) })
    }
  }, [folders, folderPath, preferences, accountId, client])
}
