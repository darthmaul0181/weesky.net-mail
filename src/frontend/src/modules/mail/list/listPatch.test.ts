import { describe, expect, it } from 'vitest'
import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchResult,
} from '../api/mailTypes'
import {
  blankPage, mapPageSummaries, pageSummaries, patchFolderCounts, patchFolderUnread,
  patchPage, patchSearchResults, patchSummaries, removeFromPage, removeSearchResults,
  removeSummaries,
} from './listPatch'

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', to: [], date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  priority: 'normal',
  ...over,
})

const result = (uid: number, folderPath: string, seen = true): MailSearchResult =>
  ({ ...summary(uid, { seen }), folderPath, uidValidity: 1 })

const node = (
  path: string, unread: number | null, children: MailFolderNode[] = [], total: number | null = 10,
): MailFolderNode => ({
  path, name: path, specialUse: null, selectable: true, subscribed: true,
  total, unread, uidValidity: 1, uidNext: 100, highestModSeq: null, children,
})

const flatPage = (messages: MailMessageSummary[]): MailFolderPage => ({
  folderPath: 'INBOX', uidValidity: 1, total: 20, page: 0, pageSize: 50, messages,
})

/** A grouped page as the backend sends one: the rows live in `threads`, `messages` stays empty. */
const groupedPage = (groups: number[][]): MailFolderPage => ({
  ...flatPage([]),
  threads: groups.map(uids => ({ messages: uids.map(uid => summary(uid)) })),
  totalThreads: groups.length,
})

const threadUids = (page: MailFolderPage) => page.threads!.map(t => t.messages.map(m => m.uid))

describe('patchSummaries', () => {
  it('rewrites only the targeted uids', () => {
    const { messages } = patchSummaries([summary(1), summary(2)], [2], 'seen', true)
    expect(messages[0].seen).toBe(false)
    expect(messages[1].seen).toBe(true)
  })

  it('counts the unread delta only for real transitions', () => {
    const input = [summary(1, { seen: true }), summary(2, { seen: false })]
    const { unreadDelta, found } = patchSummaries(input, [1, 2], 'seen', true)
    expect(unreadDelta).toBe(-1)
    expect(found).toBe(2)
  })

  it('marking unread raises the count', () => {
    const { unreadDelta } = patchSummaries([summary(1, { seen: true })], [1], 'seen', false)
    expect(unreadDelta).toBe(1)
  })

  it('flagged never moves the unread delta', () => {
    const { unreadDelta, messages } = patchSummaries([summary(1)], [1], 'flagged', true)
    expect(unreadDelta).toBe(0)
    expect(messages[0].flagged).toBe(true)
  })

  it('reports zero found when no target is present', () => {
    const { found, messages } = patchSummaries([summary(1)], [99], 'seen', true)
    expect(found).toBe(0)
    expect(messages[0].seen).toBe(false)
  })
})

describe('patchFolderUnread', () => {
  it('adjusts the one folder, deep in the tree', () => {
    const tree = [node('INBOX', 5, [node('INBOX/Sub', 3)])]
    const patched = patchFolderUnread(tree, 'INBOX/Sub', -1)
    expect(patched[0].unread).toBe(5)
    expect(patched[0].children[0].unread).toBe(2)
  })

  it('never goes below zero', () => {
    const patched = patchFolderUnread([node('INBOX', 0)], 'INBOX', -3)
    expect(patched[0].unread).toBe(0)
  })

  it('leaves a null count null', () => {
    const patched = patchFolderUnread([node('INBOX', null)], 'INBOX', -1)
    expect(patched[0].unread).toBeNull()
  })

  it('returns the tree untouched on a zero delta', () => {
    const tree = [node('INBOX', 5)]
    expect(patchFolderUnread(tree, 'INBOX', 0)).toBe(tree)
  })
})

describe('removeSummaries', () => {
  it('removes only the targeted uids', () => {
    const { messages } = removeSummaries([summary(1), summary(2), summary(3)], [2])
    expect(messages.map(m => m.uid)).toEqual([1, 3])
  })

  it('counts removed and removedUnread from what was actually present', () => {
    const input = [summary(1, { seen: false }), summary(2, { seen: true })]
    const { removed, removedUnread } = removeSummaries(input, [1, 2, 99])
    expect(removed).toBe(2)
    expect(removedUnread).toBe(1)
  })

  it('an absent uid contributes nothing to either count', () => {
    const input = [summary(1)]
    const { removed, removedUnread, messages } = removeSummaries(input, [99])
    expect(removed).toBe(0)
    expect(removedUnread).toBe(0)
    expect(messages).toBe(input)
  })

  it('returns the same array reference when nothing matched', () => {
    const input = [summary(1), summary(2)]
    const { messages } = removeSummaries(input, [99])
    expect(messages).toBe(input)
  })
})

describe('patchFolderCounts', () => {
  it('adjusts both counters on the one folder, deep in the tree', () => {
    const tree = [node('INBOX', 5, [node('INBOX/Sub', 3)])]
    const patched = patchFolderCounts(tree, 'INBOX/Sub', { total: -1, unread: -1 })
    expect(patched[0].unread).toBe(5)
    expect(patched[0].children[0].unread).toBe(2)
    expect(patched[0].children[0].total).toBe(9)
  })

  it('never goes below zero on either counter', () => {
    const patched = patchFolderCounts([node('INBOX', 0)], 'INBOX', { total: -20, unread: -3 })
    expect(patched[0].unread).toBe(0)
    expect(patched[0].total).toBe(0)
  })

  it('leaves a null unread count null', () => {
    const patched = patchFolderCounts([node('INBOX', null)], 'INBOX', { total: -1, unread: -1 })
    expect(patched[0].unread).toBeNull()
  })

  it('leaves a null total count null while unread still moves', () => {
    const patched = patchFolderCounts([node('INBOX', 5, [], null)], 'INBOX', { total: -1, unread: -1 })
    expect(patched[0].total).toBeNull()
    expect(patched[0].unread).toBe(4)
  })

  it('returns the tree untouched when both deltas are zero', () => {
    const tree = [node('INBOX', 5)]
    expect(patchFolderCounts(tree, 'INBOX', { total: 0, unread: 0 })).toBe(tree)
  })

  it('returns the same node subtree reference on a branch with no match', () => {
    const tree = [node('INBOX', 5), node('Archive', 2)]
    const patched = patchFolderCounts(tree, 'INBOX', { total: -1, unread: -1 })
    expect(patched[1]).toBe(tree[1])
  })
})

describe('patchSearchResults', () => {
  it('patches only the rows of the mutated folder', () => {
    const rows = [result(1, 'INBOX', false), result(1, 'Archive', false)]
    const patch = patchSearchResults(rows, 'INBOX', [1], 'seen', true)
    expect(patch.found).toBe(1)
    expect(patch.unreadDelta).toBe(-1)
    expect(patch.results[0].seen).toBe(true)
    expect(patch.results[1].seen).toBe(false)
  })

  it('moves the flag without touching the unread delta', () => {
    const rows = [result(1, 'INBOX')]
    const patch = patchSearchResults(rows, 'INBOX', [1], 'flagged', true)
    expect(patch.unreadDelta).toBe(0)
    expect(patch.results[0].flagged).toBe(true)
  })

  it('reports zero found when no row of that folder holds the uid', () => {
    const rows = [result(1, 'Archive', false)]
    const patch = patchSearchResults(rows, 'INBOX', [1], 'seen', true)
    expect(patch.found).toBe(0)
    expect(patch.results[0].seen).toBe(false)
  })
})

describe('removeSearchResults', () => {
  it('removes only the rows of the mutated folder and counts unread', () => {
    const rows = [result(1, 'INBOX', false), result(1, 'Archive'), result(2, 'INBOX')]
    const removal = removeSearchResults(rows, 'INBOX', [1, 2])
    expect(removal.removed).toBe(2)
    expect(removal.removedUnread).toBe(1)
    expect(removal.results).toEqual([rows[1]])
  })

  it('returns the same array reference when nothing matched', () => {
    const rows = [result(1, 'Archive'), result(2, 'Archive')]
    const removal = removeSearchResults(rows, 'INBOX', [1, 2])
    expect(removal.removed).toBe(0)
    expect(removal.results).toBe(rows)
  })
})

describe('mapPageSummaries', () => {
  it('rewrites the flat list and grows no threads field on a flat page', () => {
    const mapped = mapPageSummaries(flatPage([summary(1), summary(2)]), messages =>
      messages.filter(message => message.uid !== 1))

    expect(mapped.messages.map(m => m.uid)).toEqual([2])
    expect('threads' in mapped).toBe(false)
  })

  it('rewrites every thread of a grouped page', () => {
    const mapped = mapPageSummaries(groupedPage([[3, 2], [1]]), messages =>
      messages.map(message => ({ ...message, seen: true })))

    expect(mapped.threads!.every(t => t.messages.every(m => m.seen))).toBe(true)
    expect(threadUids(mapped)).toEqual([[3, 2], [1]])
  })

  it('drops a thread the transform emptied and keeps a partially touched one', () => {
    const mapped = mapPageSummaries(groupedPage([[3, 2], [1]]), messages =>
      messages.filter(message => message.uid !== 1 && message.uid !== 2))

    expect(threadUids(mapped)).toEqual([[3]])
  })
})

describe('pageSummaries', () => {
  it('answers the flat list itself on a flat page', () => {
    const page = flatPage([summary(1)])
    expect(pageSummaries(page)).toBe(page.messages)
  })

  it('answers every thread member on a grouped page', () => {
    expect(pageSummaries(groupedPage([[3, 2], [1]])).map(m => m.uid)).toEqual([3, 2, 1])
  })

  it('counts a uid held by both faces once', () => {
    // A merged block 0 (useListRefresh) keeps the fresh flat list beside its merged threads.
    const page = { ...groupedPage([[3, 2]]), messages: [summary(3), summary(2)] }
    expect(pageSummaries(page).map(m => m.uid)).toEqual([3, 2])
  })
})

describe('patchPage', () => {
  it('flags a member inside its own thread', () => {
    const patch = patchPage(groupedPage([[3, 2], [1]]), [2], 'seen', true)

    expect(patch.found).toBe(1)
    expect(patch.page.threads![0].messages.map(m => m.seen)).toEqual([false, true])
    expect(patch.page.threads![1].messages[0].seen).toBe(false)
  })

  it('reports zero found when no thread holds the uid', () => {
    expect(patchPage(groupedPage([[1]]), [99], 'seen', true).found).toBe(0)
  })

  it('patches a flat page exactly as patchSummaries does', () => {
    const patch = patchPage(flatPage([summary(1), summary(2)]), [1], 'flagged', true)

    expect(patch.found).toBe(1)
    expect(patch.page.messages.map(m => m.flagged)).toEqual([true, false])
    expect('threads' in patch.page).toBe(false)
  })
})

describe('removeFromPage', () => {
  it('drops a member and leaves the rest of its thread', () => {
    const removal = removeFromPage(groupedPage([[3, 2], [1]]), [2])

    expect(removal.removed).toBe(1)
    expect(threadUids(removal.page)).toEqual([[3], [1]])
  })

  it('makes a thread that lost every member disappear', () => {
    const removal = removeFromPage(groupedPage([[3, 2], [1]]), [3, 2])

    expect(removal.removed).toBe(2)
    expect(threadUids(removal.page)).toEqual([[1]])
  })

  it('leaves total and totalThreads where the flat patch leaves total', () => {
    const removal = removeFromPage(groupedPage([[3, 2], [1]]), [3, 2])

    expect(removal.page.total).toBe(20)
    expect(removal.page.totalThreads).toBe(2)
  })

  it('reports zero removed when no face holds the uid', () => {
    expect(removeFromPage(groupedPage([[1]]), [99]).removed).toBe(0)
    expect(removeFromPage(flatPage([summary(1)]), [99]).removed).toBe(0)
  })
})

describe('blankPage', () => {
  it('empties both faces of a grouped page', () => {
    const blanked = blankPage(groupedPage([[3, 2], [1]]))

    expect(blanked.messages).toEqual([])
    expect(blanked.total).toBe(0)
    expect(blanked.threads).toEqual([])
    expect(blanked.totalThreads).toBe(0)
  })

  it('gives a flat page no threads field of its own', () => {
    const blanked = blankPage(flatPage([summary(1)]))

    expect(blanked.messages).toEqual([])
    expect(blanked.total).toBe(0)
    expect('threads' in blanked).toBe(false)
    expect('totalThreads' in blanked).toBe(false)
  })
})
