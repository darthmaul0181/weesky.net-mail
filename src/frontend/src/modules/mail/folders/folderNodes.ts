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
