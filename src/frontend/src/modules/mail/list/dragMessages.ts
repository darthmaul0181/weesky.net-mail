import type { MailFolderNode } from '../api/mailTypes'

// A custom MIME lets a folder recognise our payload on dragover: the browser withholds dataTransfer
// values until drop but always exposes the types.
export const DRAG_MIME = 'application/x-weesky-messages'

export interface DragPayload {
  sourcePath: string
  uids: number[]
}

// The whole selection when every uid of the row is checked, the row's own uids otherwise, so dragging
// an unchecked row never disturbs a selection; a collapsed thread drags every member.
export function dragUids(selectedUids: number[], rowUids: number[]): number[] {
  return rowUids.every(uid => selectedUids.includes(uid)) ? selectedUids : rowUids
}

export function serializeDrag(payload: DragPayload): string {
  return JSON.stringify(payload)
}

/** Null for anything that is not our shape: a foreign drag, a truncated string, no uids. */
export function parseDrag(raw: string): DragPayload | null {
  try {
    const value: unknown = JSON.parse(raw)
    if (typeof value !== 'object' || value === null || !('sourcePath' in value) || !('uids' in value)) return null
    const sourcePath: unknown = value.sourcePath
    const uids: unknown = value.uids
    if (typeof sourcePath !== 'string') return null
    if (!Array.isArray(uids) || uids.length === 0) return null
    if (!uids.every((uid): uid is number => typeof uid === 'number')) return null
    return { sourcePath, uids }
  } catch {
    return null
  }
}

/** A drop target: a real, selectable folder that is not the one the messages already sit in. */
export function canDropInto(folder: MailFolderNode, sourcePath: string | null): boolean {
  return folder.selectable && folder.path !== sourcePath
}
