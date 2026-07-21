import type { MailFolderNode } from '../api/mailTypes'

/** What the poll compares between two ticks. All change signals, nothing else. */
export interface FolderSnapshot {
  uidNext: number | null
  total: number | null
  unread: number | null
  highestModSeq: number | null
  uidValidity: number
}

export function snapshotOf(node: MailFolderNode): FolderSnapshot {
  return {
    uidNext: node.uidNext,
    total: node.total,
    unread: node.unread,
    highestModSeq: node.highestModSeq,
    uidValidity: node.uidValidity,
  }
}

// Null-to-value is discovery (a counter the server just started reporting), not change.
function moved(previous: number | null, next: number | null): boolean {
  return previous !== null && next !== null && previous !== next
}

export function folderChanged(previous: FolderSnapshot, next: FolderSnapshot): boolean {
  return moved(previous.uidNext, next.uidNext)
    || moved(previous.total, next.total)
    || moved(previous.unread, next.unread)
    || moved(previous.highestModSeq, next.highestModSeq)
}

/** Every cached UID for the folder is a lie when this fires. */
export function uidValidityBroke(previous: FolderSnapshot, next: FolderSnapshot): boolean {
  return previous.uidValidity !== next.uidValidity
}
