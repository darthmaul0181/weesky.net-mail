import { useEffect } from 'react'
import { setFaviconBadge } from '../../../lib/favicon'
import type { MailFolderNode } from '../api/mailTypes'
import { useFolders } from '../folders'
import { inboxOf } from '../folders/folderNodes'

/** Unread in the inbox alone: junk and trash carry counts nobody is behind on, the rule the
 *  folder tree already applies to its own badges. */
export function inboxIsUnread(folders: MailFolderNode[] | undefined): boolean {
  return (inboxOf(folders)?.unread ?? 0) > 0
}

/** Marks the tab icon while the inbox holds unread mail. The query is disabled: it reads what the
 * tree holds and never fetches, so in a background tab the badge keeps up only while a
 * notification setting keeps something polling. */
export function useFaviconBadge(): void {
  const { data: folders } = useFolders(false)
  const unread = inboxIsUnread(folders)

  useEffect(() => {
    setFaviconBadge(unread)
    return () => setFaviconBadge(false)
  }, [unread])
}
