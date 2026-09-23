import type { ContactScope } from './ContactScopes'

/** A MIME of our own, recognised from the dragover types alone (values are withheld until drop),
 * and distinct from the mail's, so dragged messages offer nothing here. */
export const CONTACT_DRAG_MIME = 'application/x-weesky-contacts'

export interface ContactDragPayload { ids: string[] }

/** The whole checked selection when the dragged tile is in it, the tile alone otherwise. */
export function dragIds(selectedIds: string[], id: string): string[] {
  return selectedIds.includes(id) ? selectedIds : [id]
}

export function serializeContactDrag(payload: ContactDragPayload): string {
  return JSON.stringify(payload)
}

/** Null for anything that is not our shape: a foreign drag, a truncated string, no ids. */
export function parseContactDrag(raw: string): ContactDragPayload | null {
  try {
    const value: unknown = JSON.parse(raw)
    if (typeof value !== 'object' || value === null || !('ids' in value)) return null
    const ids: unknown = value.ids
    if (!Array.isArray(ids) || ids.length === 0) return null
    if (!ids.every((id): id is string => typeof id === 'string')) return null
    return { ids }
  } catch {
    return null
  }
}

/** `all` is the complete view, not a group, so nothing can be added to it (the mail's source
 * folder refusal). */
export function canDropIntoScope(scope: ContactScope): boolean {
  return scope !== 'all'
}
