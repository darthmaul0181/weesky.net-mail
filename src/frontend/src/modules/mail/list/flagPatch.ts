import type { MailFolderNode, MailMessageSummary } from '../api/mailTypes'

export type MailFlagName = 'seen' | 'flagged'

export interface SummaryPatch {
  messages: MailMessageSummary[]
  /** Net unread change actually produced — re-marking a read message read counts zero. */
  unreadDelta: number
  /** How many targets were present; zero means this cache says nothing about the batch. */
  found: number
}

export function patchSummaries(
  messages: MailMessageSummary[], uids: number[], flag: MailFlagName, value: boolean,
): SummaryPatch {
  const targets = new Set(uids)
  let unreadDelta = 0
  let found = 0

  const patched = messages.map(message => {
    if (!targets.has(message.uid)) return message
    found += 1
    if (flag === 'seen') {
      if (message.seen === value) return message
      unreadDelta += value ? -1 : 1
      return { ...message, seen: value }
    }
    if (message.flagged === value) return message
    return { ...message, flagged: value }
  })

  return { messages: patched, unreadDelta, found }
}

export function patchFolderUnread(tree: MailFolderNode[], folderPath: string, delta: number): MailFolderNode[] {
  if (delta === 0) return tree

  return tree.map(node => {
    if (node.path === folderPath) {
      const unread = node.unread === null ? null : Math.max(0, node.unread + delta)
      return { ...node, unread }
    }
    return node.children.length ? { ...node, children: patchFolderUnread(node.children, folderPath, delta) } : node
  })
}
