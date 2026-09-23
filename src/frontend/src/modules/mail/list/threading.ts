import type { MailFolderPage, MailMessageSummary, MailThread } from '../api/mailTypes'

/** A list with at least one element, typed rather than merely asserted: `messages[0]` reads as
    `MailMessageSummary`, never `| undefined`, everywhere a `ThreadGroup` is read. */
export type NonEmpty<T> = [T, ...T[]]

/** One list row: a conversation, or a single message wrapped as one. */
export interface ThreadGroup {
  /** The oldest member's uid — the newest changes on every arrival, so it cannot be the key. */
  key: number
  /** Newest first, as the backend sends them. */
  messages: NonEmpty<MailMessageSummary>
}

export function threadKeyOf(messages: NonEmpty<MailMessageSummary>): number {
  // The last message, read by walking rather than by a computed index: the tuple's rest part
  // has no fixed length, so TS cannot type an index into it as anything but T | undefined.
  const [first, ...rest] = messages
  let oldest = first
  for (const message of rest) oldest = message
  return oldest.uid
}

const toGroup = (thread: MailThread & { messages: NonEmpty<MailMessageSummary> }): ThreadGroup =>
  ({ key: threadKeyOf(thread.messages), messages: thread.messages })

/** Narrows a thread's `messages` from the wire's plain array to `NonEmpty` — the one place that
    invariant is actually established, by checking it. */
function hasMessages(t: MailThread): t is MailThread & { messages: NonEmpty<MailMessageSummary> } {
  return t.messages.length > 0
}

/** A grouped page speaks through `threads`; a flat one is its messages, one group each. */
export function groupsOf(page: MailFolderPage): ThreadGroup[] {
  if (page.threads) return page.threads.filter(hasMessages).map(toGroup)
  return page.messages.map(message => ({ key: message.uid, messages: [message] }))
}

// The dedupeByUid rules for threads: the first version wins, a member already shown is dropped, and a
// thread emptied by that disappears, or an offset shift would leave two rows for one message.
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
      // filter() loses the tuple type; the length check here is what the cast restores it on.
      if (fresh.length > 0) groups.push({ key: group.key, messages: fresh as NonEmpty<MailMessageSummary> })
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
