import type { WhatsNewContext, WhatsNewEntry } from './catalog'
import type { WhatsNewItemState, WhatsNewMark, WhatsNewState } from './types'
import { compareVersions } from './version'

const EXPIRY_MS = 30 * 24 * 60 * 60 * 1000

export interface Novelty {
  entry: WhatsNewEntry
  state?: WhatsNewItemState
  /** Published after the account's baseline. */
  fresh: boolean
  active: boolean
  unlisted: boolean
}

export function noveltiesOf(
  catalog: readonly WhatsNewEntry[], state: WhatsNewState, context: WhatsNewContext, now: number,
): Novelty[] {
  const byId = new Map(state.items.map(item => [item.id, item]))
  return catalog
    .map((entry, order) => ({ entry, order }))
    .filter(({ entry }) => entry.when?.(context) ?? true)
    .sort((a, b) => compareVersions(b.entry.version, a.entry.version) || b.order - a.order)
    .map(({ entry }) => {
      const item = byId.get(entry.id)
      const fresh = state.baseline !== undefined && compareVersions(entry.version, state.baseline) > 0
      const expired = item?.firstShownAt !== undefined && now - Date.parse(item.firstShownAt) >= EXPIRY_MS
      const active = fresh && !item?.seenAt && !expired
      return { entry, state: item, fresh, active, unlisted: active && !item?.listedAt }
    })
}

const COLUMN = { shown: 'firstShownAt', listed: 'listedAt', seen: 'seenAt' } as const

/** The server's rule, applied ahead of its answer: an empty stamp fills, a set one stays. */
export function stamp(state: WhatsNewState, ids: string[], mark: WhatsNewMark, at: string): WhatsNewState {
  const column = COLUMN[mark], marked = new Set(ids)
  const known = new Set(state.items.map(item => item.id))
  const items: WhatsNewItemState[] = [...state.items, ...[...marked].filter(id => !known.has(id)).map(id => ({ id }))]
  return {
    ...state,
    items: items.map(item => marked.has(item.id)
      ? { ...item, firstShownAt: item.firstShownAt ?? at, [column]: item[column] ?? at }
      : item),
  }
}
