import { useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { usePreferences } from '../../../hooks/usePreferences'
import { refreshFolderList } from '../cachePatches'
import { folderByPath } from '../folders/folderNodes'
import { mailKeys, useAccountId, useFolders } from '../queries'
import { folderChanged, snapshotOf, uidValidityBroke, type FolderSnapshot } from './folderDelta'

// Refreshes the list when the polled listing says its folder moved; the first observation is the
// baseline and never triggers.
export function useListRefresh(folderPath: string | null, enabled = true): void {
  const accountId = useAccountId()
  const client = useQueryClient()
  const { data: folders } = useFolders(enabled)
  const { data: preferences } = usePreferences()
  const previous = useRef<{ path: string; snapshot: FolderSnapshot } | null>(null)

  useEffect(() => {
    if (!folderPath || !folders || !preferences) return
    const node = folderByPath(folders, folderPath)
    if (!node) return

    const snapshot = snapshotOf(node)

    // A write patches these very counts optimistically, so answering it would refetch the
    // folder mid-write: the read reaches the mailbox before the STORE lands and puts the
    // message back the way it was. Whatever really moved keeps until the next poll.
    if (client.isMutating({ mutationKey: mailKeys.writes(accountId) }) > 0) return

    const last = previous.current
    previous.current = { path: folderPath, snapshot }
    if (!last || last.path !== folderPath) return

    if (uidValidityBroke(last.snapshot, snapshot)) {
      // Every cached UID is a lie. resetQueries refetches only what is on screen, from
      // scratch — an invalidate would replay every loaded stream block.
      void client.resetQueries({
        predicate: query =>
          query.queryKey[0] === 'mail' && query.queryKey[1] === accountId
          && query.queryKey[3] === folderPath,
      })
      return
    }

    if (!folderChanged(last.snapshot, snapshot)) return

    void refreshFolderList(client, accountId, folderPath, preferences)
  }, [folders, folderPath, preferences, accountId, client])
}
