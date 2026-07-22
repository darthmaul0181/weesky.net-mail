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

export interface RemovedSummaries {
  messages: MailMessageSummary[]
  removed: number
  removedUnread: number
}

export function removeSummaries(messages: MailMessageSummary[], uids: number[]): RemovedSummaries {
  const targets = new Set(uids)
  let removed = 0
  let removedUnread = 0

  const kept = messages.filter(message => {
    if (!targets.has(message.uid)) return true
    removed += 1
    if (!message.seen) removedUnread += 1
    return false
  })

  return { messages: removed === 0 ? messages : kept, removed, removedUnread }
}

export interface FolderCountDeltas {
  total: number
  unread: number
}

export function patchFolderCounts(
  tree: MailFolderNode[], folderPath: string, deltas: FolderCountDeltas,
): MailFolderNode[] {
  if (deltas.total === 0 && deltas.unread === 0) return tree

  return tree.map(node => {
    if (node.path === folderPath) {
      const unread = node.unread === null ? null : Math.max(0, node.unread + deltas.unread)
      const total = node.total === null ? null : Math.max(0, node.total + deltas.total)
      return { ...node, unread, total }
    }
    return node.children.length
      ? { ...node, children: patchFolderCounts(node.children, folderPath, deltas) }
      : node
  })
}

export function patchFolderUnread(tree: MailFolderNode[], folderPath: string, delta: number): MailFolderNode[] {
  return patchFolderCounts(tree, folderPath, { total: 0, unread: delta })
}
