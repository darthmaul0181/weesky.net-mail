import { describe, it, expect } from 'vitest'
import { inboxIsUnread } from './useFaviconBadge'
import type { MailFolderNode } from '../modules/mail/api/mailTypes'

function node(over: Partial<MailFolderNode> = {}): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: 2, children: [], ...over,
  } as MailFolderNode
}

describe('inboxIsUnread', () => {
  it('is false before the tree has loaded', () => {
    expect(inboxIsUnread(undefined)).toBe(false)
  })

  it('follows the inbox unread count', () => {
    expect(inboxIsUnread([node({ specialUse: 'inbox', unread: 3 })])).toBe(true)
    expect(inboxIsUnread([node({ specialUse: 'inbox', unread: 0 })])).toBe(false)
  })

  // Nobody is behind on their deleted mail, and an unread count on junk advertises exactly what
  // the filter was meant to spare them — the rule the folder tree's own badges follow.
  it('ignores unread mail outside the inbox', () => {
    const folders = [
      node({ path: 'INBOX', specialUse: 'inbox', unread: 0 }),
      node({ path: 'Junk', specialUse: 'junk', unread: 12 }),
      node({ path: 'Trash', specialUse: 'trash', unread: 4 }),
    ]

    expect(inboxIsUnread(folders)).toBe(false)
  })

  it('finds an inbox nested under a namespace prefix', () => {
    const folders = [node({
      path: 'INBOX', name: 'INBOX', selectable: false,
      children: [node({ path: 'INBOX.Inbox', specialUse: 'inbox', unread: 1 })],
    })]

    expect(inboxIsUnread(folders)).toBe(true)
  })
})
