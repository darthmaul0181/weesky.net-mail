import type { MailFolderNode } from '../api/mailTypes'

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

/** Nesting shown in a flat <select>, where indentation is the only cue available. */
export function indent(depth: number): string {
  return ' '.repeat(depth * 3)
}

/**
 * A folder currently playing a well-known role. These are locked against renaming, deletion
 * and hiding: the first two break the role for every client on the mailbox, the third strands
 * whatever gets filed into it. The API refuses them too — this is not the only guard.
 */
export function isSystemFolder(node: MailFolderNode): boolean {
  return Boolean(node.specialUse)
}

/**
 * Orders a tree for the folders list: the inbox first, then everything else by name, at every
 * level.
 *
 * Deliberately *not* the mail column's order, which floats the well-known folders to the top
 * because that is where a reader reaches for them. Here the question is "where is the folder I
 * am looking for", so a system folder sits under its own name among the rest — with "Deleted
 * Items" between "Courrier indésirable" and "Developpement" rather than in a block of its own.
 *
 * Compared with localeCompare: this mailbox is full of accented names, and a codepoint sort
 * would file "Éléments supprimés" after "Zeta". Case-insensitive, so "e-commerce" lands
 * between "Drafts" and "English" rather than after every capitalised name.
 */
export function sortFolders(nodes: MailFolderNode[]): MailFolderNode[] {
  return [...nodes]
    .sort((a, b) => {
      // The inbox is not a folder among others: it is where mail arrives, it cannot be
      // renamed, and every client shows it first.
      if (a.specialUse === 'inbox') return -1
      if (b.specialUse === 'inbox') return 1
      return a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true })
    })
    .map(node => (node.children.length ? { ...node, children: sortFolders(node.children) } : node))
}
