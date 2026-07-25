import type { MailFolderNode } from '../api/mailTypes'

/**
 * A custom MIME so a folder can recognise our payload from its dragover types alone: the browser
 * withholds dataTransfer *values* until drop, but always exposes the list of types.
 */
export const DRAG_MIME = 'application/x-weesky-messages'

export interface DragPayload {
  sourcePath: string
  uids: number[]
}

/**
 * The dragged row carries the whole checked selection when it belongs to it, itself alone
 * otherwise — so dragging an unchecked row never disturbs a selection made for something else.
 */
export function dragUids(selectedUids: number[], uid: number): number[] {
  return selectedUids.includes(uid) ? selectedUids : [uid]
}

export function serializeDrag(payload: DragPayload): string {
  return JSON.stringify(payload)
}

/** Null for anything that is not our shape: a foreign drag, a truncated string, no uids. */
export function parseDrag(raw: string): DragPayload | null {
  try {
    const value = JSON.parse(raw)
    if (typeof value?.sourcePath !== 'string') return null
    if (!Array.isArray(value.uids) || value.uids.length === 0) return null
    if (!value.uids.every((uid: unknown) => typeof uid === 'number')) return null
    return { sourcePath: value.sourcePath, uids: value.uids }
  } catch {
    return null
  }
}

/** A drop target: a real, selectable folder that is not the one the messages already sit in. */
export function canDropInto(folder: MailFolderNode, sourcePath: string | null): boolean {
  return folder.selectable && folder.path !== sourcePath
}
