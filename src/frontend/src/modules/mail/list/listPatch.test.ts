import { describe, expect, it } from 'vitest'
import type { MailFolderNode, MailMessageSummary, MailSearchResult } from '../api/mailTypes'
import {
  patchFolderCounts, patchFolderUnread, patchSearchResults, patchSummaries,
  removeSearchResults, removeSummaries,
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
