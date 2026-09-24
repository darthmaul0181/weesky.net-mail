import { collator } from '../../../lib/intl'
import type { MailFolderNode, SpecialUse } from '../api/mailTypes'

/** Where the row and reader actions file a message. Null is "no folder holds that role". */
export interface RolePaths {
  trash: string | null
  archive: string | null
  junk: string | null
}

/** Flattens the tree so a parent picker or a flat list can show every folder. */
export function flatten(nodes: MailFolderNode[], depth = 0): Array<{ node: MailFolderNode; depth: number }> {
  return nodes.flatMap(node => [{ node, depth }, ...flatten(node.children, depth + 1)])
}

/** The first folder in tree order that `match` accepts, anywhere in the tree. */
export function findFolder(
  nodes: readonly MailFolderNode[] | undefined, match: (node: MailFolderNode) => boolean,
): MailFolderNode | undefined {
  for (const node of nodes ?? []) {
    const found = match(node) ? node : findFolder(node.children, match)
    if (found) return found
  }
  return undefined
}

export function folderByPath(nodes: readonly MailFolderNode[] | undefined, path: string | null) {
  return findFolder(nodes, node => node.path === path)
}

export function folderByRole(nodes: readonly MailFolderNode[] | undefined, role: SpecialUse) {
  return findFolder(nodes, node => node.specialUse === role)
}

/** Found by role, never by the name "INBOX", which a server is free to spell otherwise. */
export function inboxOf(nodes: readonly MailFolderNode[] | undefined) {
  return folderByRole(nodes, 'inbox')
}

// Strips the leaf name, so any separator works: the backend rejects names containing one.
export function parentOf(folder: MailFolderNode): string {
  return folder.path.length > folder.name.length
    ? folder.path.slice(0, folder.path.length - folder.name.length - 1)
    : ''
}

// Anywhere in the tree, since a server may nest its trash under the inbox. Two folders claiming one
// role is a server-side conflict: the first in tree order wins.
export function rolePathsOf(nodes: MailFolderNode[]): RolePaths {
  const paths: RolePaths = { trash: null, archive: null, junk: null }

  for (const { node } of flatten(nodes)) {
    const role = node.specialUse
    if ((role === 'trash' || role === 'archive' || role === 'junk') && paths[role] === null) {
      paths[role] = node.path
    }
  }
  return paths
}

/** Nesting shown in a flat <select>, where indentation is the only cue available. */
export function indent(depth: number): string {
  return ' '.repeat(depth * 3)
}

/** Locked against renaming, deletion and hiding — the API refuses those three too. */
export function isSystemFolder(node: MailFolderNode): boolean {
  return Boolean(node.specialUse)
}

// Inbox first, then everything by name, role folders interleaved: here the question is "where is my
// folder". localeCompare, or every accented name files after "Z".
export function sortFolders(nodes: MailFolderNode[]): MailFolderNode[] {
  return [...nodes]
    .sort((a, b) => {
      if (a.specialUse === 'inbox') return -1
      if (b.specialUse === 'inbox') return 1
      return collator({ sensitivity: 'base', numeric: true }).compare(a.name, b.name)
    })
    .map(node => (node.children.length ? { ...node, children: sortFolders(node.children) } : node))
}
