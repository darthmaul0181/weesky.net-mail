import { describe, it, expect } from 'vitest'
import {
  findFolder, flatten, folderByPath, folderByRole, inboxOf, indent, isSystemFolder, parentOf, rolePathsOf,
  sortFolders,
} from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', selectable: true, subscribed: true,
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

describe('rolePathsOf', () => {
  it('answers the path of each action role', () => {
    expect(rolePathsOf([
      node({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
      node({ path: 'Archives', name: 'Archives', specialUse: 'archive' }),
      node({ path: 'Spam', name: 'Spam', specialUse: 'junk' }),
    ])).toEqual({ trash: 'Corbeille', archive: 'Archives', junk: 'Spam' })
  })

  it('finds a role held by a nested folder', () => {
    expect(rolePathsOf([
      node({
        path: 'INBOX', name: 'INBOX', specialUse: 'inbox',
        children: [node({ path: 'INBOX.Trash', name: 'Trash', specialUse: 'trash' })],
      }),
    ]).trash).toBe('INBOX.Trash')
  })

  it('answers null for a role no folder holds', () => {
    expect(rolePathsOf(tree)).toEqual({ trash: 'Corbeille', archive: null, junk: null })
  })

  it('answers null for every role on an empty tree', () => {
    expect(rolePathsOf([])).toEqual({ trash: null, archive: null, junk: null })
  })

  // Two folders claiming one role is a server-side conflict, not a client decision: the first
  // in tree order wins, deterministically.
  it('keeps the first folder claiming a role', () => {
    expect(rolePathsOf([
      node({ path: 'Trash', name: 'Trash', specialUse: 'trash' }),
      node({ path: 'Deleted', name: 'Deleted', specialUse: 'trash' }),
    ]).trash).toBe('Trash')
  })
})

describe('isSystemFolder', () => {
  it('is true for any folder holding a role, not just the inbox', () => {
    expect(isSystemFolder(node({ specialUse: 'inbox' }))).toBe(true)
    expect(isSystemFolder(node({ specialUse: 'trash' }))).toBe(true)
    expect(isSystemFolder(node({}))).toBe(false)
  })
})

describe('findFolder', () => {
  it('finds a nested folder by path, and nothing for a path no folder holds', () => {
    expect(folderByPath(tree, 'Projects/Alpha')?.name).toBe('Alpha')
    expect(folderByPath(tree, 'Nowhere')).toBeUndefined()
    expect(folderByPath(tree, null)).toBeUndefined()
    expect(folderByPath(undefined, 'INBOX')).toBeUndefined()
  })

  it('finds a folder by role, the first in tree order winning', () => {
    const nested = [node({ path: 'A', children: [node({ path: 'A/Bin', specialUse: 'trash' })] }),
      node({ path: 'Bin', specialUse: 'trash' })]

    expect(folderByRole(nested, 'trash')?.path).toBe('A/Bin')
    expect(folderByRole(tree, 'sent')).toBeUndefined()
    expect(inboxOf(tree)?.path).toBe('INBOX')
    expect(findFolder(tree, folder => !folder.subscribed)?.path).toBe('Projects/Alpha')
  })
})
