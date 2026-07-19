import { describe, it, expect } from 'vitest'
import { flatten, indent, isSystemFolder, parentOf, sortFolders } from './folderNodes'
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
  // From the leaf name, not by splitting: the separator belongs to the server.
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

describe('sortFolders', () => {
  const names = (nodes: MailFolderNode[]) => flatten(sortFolders(nodes)).map(f => f.node.name)

  it('pins the inbox first however the server ordered the list', () => {
    expect(names([
      node({ path: 'Zeta', name: 'Zeta' }),
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
      node({ path: 'Alpha', name: 'Alpha' }),
    ])).toEqual(['INBOX', 'Alpha', 'Zeta'])
  })

  // A system folder sits under its own name, where the user would look for it.
  it('interleaves system folders alphabetically instead of grouping them', () => {
    expect(names([
      node({ path: 'Developpement', name: 'Developpement' }),
      node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash' }),
      node({ path: 'Courrier indésirable', name: 'Courrier indésirable' }),
      node({ path: 'Drafts', name: 'Drafts', specialUse: 'drafts' }),
      node({ path: 'Brouillons', name: 'Brouillons' }),
    ])).toEqual(['Brouillons', 'Courrier indésirable', 'Deleted Items', 'Developpement', 'Drafts'])
  })

  // A codepoint sort files every accented name after "Z".
  it('sorts accented names where a reader expects them', () => {
    expect(names([
      node({ path: 'Zeta', name: 'Zeta' }),
      node({ path: 'Éléments supprimés', name: 'Éléments supprimés' }),
      node({ path: 'Envoyés', name: 'Envoyés' }),
    ])).toEqual(['Éléments supprimés', 'Envoyés', 'Zeta'])
  })

  it('ignores case, so a lowercase name is not exiled to the end', () => {
    expect(names([
      node({ path: 'English', name: 'English' }),
      node({ path: 'e-commerce', name: 'e-commerce' }),
      node({ path: 'Drafts', name: 'Drafts' }),
    ])).toEqual(['Drafts', 'e-commerce', 'English'])
  })

  it('sorts children within their parent and leaves the hierarchy intact', () => {
    const sorted = sortFolders([
      node({
        path: 'Projects', name: 'Projects',
        children: [
          node({ path: 'Projects/Zeta', name: 'Zeta' }),
          node({ path: 'Projects/Alpha', name: 'Alpha' }),
        ],
      }),
      node({ path: 'Archive', name: 'Archive' }),
    ])

    expect(flatten(sorted).map(f => [f.node.name, f.depth]))
      .toEqual([['Archive', 0], ['Projects', 0], ['Alpha', 1], ['Zeta', 1]])
  })

  it('does not mutate the tree it was given', () => {
    const input = [node({ path: 'Zeta', name: 'Zeta' }), node({ path: 'Alpha', name: 'Alpha' })]

    sortFolders(input)

    expect(input.map(n => n.name)).toEqual(['Zeta', 'Alpha'])
  })
})

describe('isSystemFolder', () => {
  it('is true for any folder holding a role, not just the inbox', () => {
    expect(isSystemFolder(node({ specialUse: 'inbox' }))).toBe(true)
    expect(isSystemFolder(node({ specialUse: 'trash' }))).toBe(true)
    expect(isSystemFolder(node({ specialUse: null }))).toBe(false)
  })
})
