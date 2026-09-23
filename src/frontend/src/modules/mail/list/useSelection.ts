import { useKeyedState } from '../../../hooks/useKeyedState'

// Keyed by uid or GUID; `resetKey` clears it. The row list is never stored, so the caller intersects
// `selected` with what is on screen. `toggleRange` selects from the last-toggled anchor to `index`
// over the caller's `keys`; the anchor resets with the selection.
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
