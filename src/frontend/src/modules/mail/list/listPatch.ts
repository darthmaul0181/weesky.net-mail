import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchResult,
} from '../api/mailTypes'

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

/**
 * The one place a cached page's two faces are rewritten: the flat `messages` and, on a grouped
 * page, every thread's own members — the backend fills one or the other, never both. A thread
 * the transform empties disappears with its last member.
 */
export function mapPageSummaries(
  page: MailFolderPage, map: (messages: MailMessageSummary[]) => MailMessageSummary[],
): MailFolderPage {
  const messages = map(page.messages)
  if (!page.threads) return { ...page, messages }

  const threads = page.threads
    .map(thread => ({ messages: map(thread.messages) }))
    .filter(thread => thread.messages.length > 0)
  return { ...page, messages, threads }
}

/** Every summary a page holds, thread members included, one entry per uid — what a tally counts
    and what a lookup searches. A merged block 0 carries both faces, hence the dedup. */
export function pageSummaries(page: MailFolderPage): MailMessageSummary[] {
  if (!page.threads) return page.messages

  const byUid = new Map(page.messages.map(message => [message.uid, message]))
  for (const thread of page.threads) {
    for (const message of thread.messages) {
      if (!byUid.has(message.uid)) byUid.set(message.uid, message)
    }
  }
  return [...byUid.values()]
}

export interface PagePatch {
  page: MailFolderPage
  /** Rows the patch touched; zero means this cache says nothing about the batch. */
  found: number
}

/** patchSummaries over a whole page, threads included. */
export function patchPage(
  page: MailFolderPage, uids: number[], flag: MailFlagName, value: boolean,
): PagePatch {
  let found = 0
  const patched = mapPageSummaries(page, messages => {
    const patch = patchSummaries(messages, uids, flag, value)
    found += patch.found
    return patch.messages
  })
  return { page: patched, found }
}

export interface PageRemoval {
  page: MailFolderPage
  removed: number
}

/** removeSummaries over a whole page: a thread losing every member goes with them. `total` and
    `totalThreads` are left alone, exactly as the flat patch leaves `total`. */
export function removeFromPage(page: MailFolderPage, uids: number[]): PageRemoval {
  let removed = 0
  const kept = mapPageSummaries(page, messages => {
    const removal = removeSummaries(messages, uids)
    removed += removal.removed
    return removal.messages
  })
  return { page: kept, removed }
}

/** The folder emptied in place: whatever the flat pair says, the grouped pair says too. */
export function blankPage(page: MailFolderPage): MailFolderPage {
  const emptied = { ...mapPageSummaries(page, () => []), total: 0 }
  return page.threads ? { ...emptied, totalThreads: 0 } : emptied
}

export interface SearchResultsPatch {
  results: MailSearchResult[]
  unreadDelta: number
  found: number
}

/** patchSummaries scoped to one folder: a same uid under another folder is another message. */
export function patchSearchResults(
  results: MailSearchResult[], folderPath: string, uids: number[], flag: MailFlagName, value: boolean,
): SearchResultsPatch {
  const targets = new Set(uids)
  let unreadDelta = 0
  let found = 0

  const patched = results.map(row => {
    if (row.folderPath !== folderPath || !targets.has(row.uid)) return row
    found += 1
    if (flag === 'seen') {
      if (row.seen === value) return row
      unreadDelta += value ? -1 : 1
      return { ...row, seen: value }
    }
    if (row.flagged === value) return row
    return { ...row, flagged: value }
  })

  return { results: patched, unreadDelta, found }
}

export interface RemovedSearchResults {
  results: MailSearchResult[]
  removed: number
  removedUnread: number
}

export function removeSearchResults(
  results: MailSearchResult[], folderPath: string, uids: number[],
): RemovedSearchResults {
  const targets = new Set(uids)
  let removed = 0
  let removedUnread = 0

  const kept = results.filter(row => {
    if (row.folderPath !== folderPath || !targets.has(row.uid)) return true
    removed += 1
    if (!row.seen) removedUnread += 1
    return false
  })

  return { results: removed === 0 ? results : kept, removed, removedUnread }
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
