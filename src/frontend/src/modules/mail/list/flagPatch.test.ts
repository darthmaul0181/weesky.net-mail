import { describe, expect, it } from 'vitest'
import type { MailFolderNode, MailMessageSummary } from '../api/mailTypes'
import { patchFolderUnread, patchSummaries } from './flagPatch'

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  ...over,
})

const node = (path: string, unread: number | null, children: MailFolderNode[] = []): MailFolderNode => ({
  path, name: path, specialUse: null, selectable: true, subscribed: true,
  total: 10, unread, uidValidity: 1, uidNext: 100, highestModSeq: null, children,
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
