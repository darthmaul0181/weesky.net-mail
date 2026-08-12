import type { ContactScope } from './ContactScopes'

/**
 * A custom MIME so a scope can recognise our payload from its dragover types alone: the browser
 * withholds dataTransfer *values* until drop, but always exposes the list of types. Distinct from
 * the mail's, so dragging messages over the contacts column offers nothing.
 */
export const CONTACT_DRAG_MIME = 'application/x-weesky-contacts'

export interface ContactDragPayload { ids: string[] }

/**
 * The dragged tile carries the whole checked selection when it belongs to it, itself alone
 * otherwise — so dragging an unchecked tile never disturbs a selection made for something else.
 */
export function dragIds(selectedIds: string[], id: string): string[] {
  return selectedIds.includes(id) ? selectedIds : [id]
}

export function serializeContactDrag(payload: ContactDragPayload): string {
  return JSON.stringify(payload)
}

/** Null for anything that is not our shape: a foreign drag, a truncated string, no ids. */
export function parseContactDrag(raw: string): ContactDragPayload | null {
  try {
    const value = JSON.parse(raw)
    if (!Array.isArray(value?.ids) || value.ids.length === 0) return null
    if (!value.ids.every((id: unknown) => typeof id === 'string')) return null
    return { ids: value.ids }
  } catch {
    return null
  }
}

/**
 * A drop target is a scope a contact can belong to. `all` is the complete view rather than a
 * group, so nothing can be added to it — the same refusal `canDropInto` makes for the source
 * folder. Groups, when they land, are targets by construction.
 */
export function canDropIntoScope(scope: ContactScope): boolean {
  return scope !== 'all'
}
