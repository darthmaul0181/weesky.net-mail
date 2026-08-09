import { collator } from '../../../lib/intl'
import type { MailFolderNode } from '../api/mailTypes'

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

/**
 * Derives a folder's parent path by removing its leaf name. Works for any hierarchy
 * separator, because the leaf name is known and cannot contain one — the backend rejects
 * names that do.
 */
export function parentOf(folder: MailFolderNode): string {
  return folder.path.length > folder.name.length
    ? folder.path.slice(0, folder.path.length - folder.name.length - 1)
    : ''
}

/**
 * The target of each role action, anywhere in the tree — a server may nest its trash under the
 * inbox. Two folders claiming one role is a server-side conflict: the first in tree order wins.
 */
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

/**
 * Inbox first, then everything by name — system folders interleaved, not grouped: here the
 * question is "where is the folder I am looking for". `FolderTree.splitByRole` does the
 * opposite. localeCompare, or every accented name files after "Z".
 */
export function sortFolders(nodes: MailFolderNode[]): MailFolderNode[] {
  return [...nodes]
    .sort((a, b) => {
      if (a.specialUse === 'inbox') return -1
      if (b.specialUse === 'inbox') return 1
      return collator({ sensitivity: 'base', numeric: true }).compare(a.name, b.name)
    })
    .map(node => (node.children.length ? { ...node, children: sortFolders(node.children) } : node))
}
