import { useEffect } from 'react'
import { setFaviconBadge } from '../lib/favicon'
import { flatten } from '../modules/mail/folders/folderNodes'
import { useFolders } from '../modules/mail/queries'
import type { MailFolderNode } from '../modules/mail/api/mailTypes'

/** Unread in the inbox alone: junk and trash carry counts nobody is behind on, the rule the
 *  folder tree already applies to its own badges. */
export function inboxIsUnread(folders: MailFolderNode[] | undefined): boolean {
  if (!folders) return false
  const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')?.node
  return (inbox?.unread ?? 0) > 0
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
