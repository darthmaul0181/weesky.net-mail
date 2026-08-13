import type { MailFolderPage, MailMessageSummary, MailThread } from '../api/mailTypes'

/** One list row: a conversation, or a single message wrapped as one. */
export interface ThreadGroup {
  /** The oldest member's uid — the newest changes on every arrival, so it cannot be the key. */
  key: number
  /** Newest first, as the backend sends them. */
  messages: MailMessageSummary[]
}

export function threadKeyOf(messages: MailMessageSummary[]): number {
  return messages[messages.length - 1].uid
}

const toGroup = (thread: MailThread): ThreadGroup =>
  ({ key: threadKeyOf(thread.messages), messages: thread.messages })

/** A grouped page speaks through `threads`; a flat one is its messages, one group each. */
export function groupsOf(page: MailFolderPage): ThreadGroup[] {
  if (page.threads) return page.threads.filter(t => t.messages.length > 0).map(toGroup)
  return page.messages.map(message => ({ key: message.uid, messages: [message] }))
}

/**
 * Snapshot semantics, the dedupeByUid rules transposed: the first version of a thread wins,
 * a member already shown under an earlier thread is dropped, and a thread emptied by that
 * drop disappears — two rows for one message would otherwise survive an offset shift.
 */
export function dedupeThreads(pages: MailFolderPage[]): ThreadGroup[] {
  const seenThreads = new Set<number>()
  const seenUids = new Set<number>()
  const groups: ThreadGroup[] = []

  for (const page of pages) {
    for (const group of groupsOf(page)) {
      if (seenThreads.has(group.key)) continue
      seenThreads.add(group.key)
      const fresh = group.messages.filter(message => !seenUids.has(message.uid))
      fresh.forEach(message => seenUids.add(message.uid))
      if (fresh.length > 0) groups.push({ key: group.key, messages: fresh })
    }
  }

  return groups
}

/** The members as summaries, in display order — what a list that still reasons in messages
    (selection, the reader, the bulk actions) reads. */
export function flatMessages(groups: ThreadGroup[]): MailMessageSummary[] {
  return groups.flatMap(group => group.messages)
}

export function memberUids(groups: ThreadGroup[]): number[] {
  return flatMessages(groups).map(message => message.uid)
}
