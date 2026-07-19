import { describe, it, expect } from 'vitest'
import { flatten, indent, isSystemFolder, parentOf } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
  node({
    path: 'Projects', name: 'Projects',
    children: [node({ path: 'Projects/Alpha', name: 'Alpha', subscribed: false })],
  }),
]

describe('parentOf', () => {
  // Derived from the leaf name rather than by splitting on a separator, because the separator
  // belongs to the server: '.' on the home server, '/' elsewhere.
  it('strips the leaf name whatever the separator', () => {
    expect(parentOf(node({ path: 'INBOX/Projects', name: 'Projects' }))).toBe('INBOX')
    expect(parentOf(node({ path: 'INBOX.Projects', name: 'Projects' }))).toBe('INBOX')
  })

  it('returns empty for a top-level folder', () => {
    expect(parentOf(node({ path: 'INBOX', name: 'INBOX' }))).toBe('')
  })
})

describe('flatten', () => {
  it('includes children with their depth', () => {
    expect(flatten(tree).map(f => [f.node.name, f.depth]))
      .toEqual([['INBOX', 0], ['Corbeille', 0], ['Projects', 0], ['Alpha', 1]])
  })
})

describe('indent', () => {
  it('widens with the depth', () => {
    expect(indent(0)).toBe('')
    expect(indent(2)).toHaveLength(6)
  })
})

describe('isSystemFolder', () => {
  it('is true for any folder holding a role, not just the inbox', () => {
    expect(isSystemFolder(node({ specialUse: 'inbox' }))).toBe(true)
    expect(isSystemFolder(node({ specialUse: 'trash' }))).toBe(true)
    expect(isSystemFolder(node({ specialUse: null }))).toBe(false)
  })
})
