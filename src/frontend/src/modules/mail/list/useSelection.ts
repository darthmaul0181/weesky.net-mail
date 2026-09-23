import { useKeyedState } from '../../../hooks/useKeyedState'

/**
 * Checkbox selection over the loaded rows, keyed by whatever identifies one: the mail's numeric
 * uids, the contacts' GUIDs. `resetKey` (folder + page, or the contacts scope) clears it; the hook
 * never stores the row list, so the caller intersects `selected` with what is on screen — a
 * departed row stops counting on its own. `toggleRange` selects the inclusive slice from the
 * last-toggled anchor to `index`, over the `keys` order the caller passes in; the anchor resets
 * with the selection.
 */
export function useSelection<T = number>(resetKey: string) {
  const [selected, setSelected] = useKeyedState<Set<T>>(() => new Set(), resetKey)
  const [anchor, setAnchor] = useKeyedState<number | null>(() => null, resetKey)

  return {
    selected,
    has: (key: T) => selected.has(key),
    toggle(key: T, index: number) {
      setSelected(prev => {
        const next = new Set(prev)
        if (next.has(key)) next.delete(key); else next.add(key)
        return next
      })
      setAnchor(index)
    },
    /** A batch on or off in one call — the thread row's checkbox, whatever mix it covered. */
    setMany(keys: T[], on: boolean) {
      setSelected(prev => {
        const next = new Set(prev)
        keys.forEach(key => { if (on) next.add(key); else next.delete(key) })
        return next
      })
    },
    toggleRange(keys: T[], index: number) {
      const from = anchor ?? index
      const [lo, hi] = from <= index ? [from, index] : [index, from]
      setSelected(prev => new Set([...prev, ...keys.slice(lo, hi + 1)]))
      setAnchor(index)
    },
    selectAll(keys: T[]) {
      setSelected(new Set(keys))
      setAnchor(null)
    },
    clear() {
      setSelected(new Set())
      setAnchor(null)
    },
  }
}
