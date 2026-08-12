import { useEffect, useRef, useState } from 'react'

/**
 * Checkbox selection over the loaded rows, keyed by whatever identifies one: the mail's numeric
 * uids, the contacts' GUIDs. `resetKey` (folder + page, or the contacts scope) clears it; the hook
 * never stores the row list, so the caller intersects `selected` with what is on screen — a
 * departed row stops counting on its own. `toggleRange` selects the inclusive slice from the
 * last-toggled anchor to `index`, over the `keys` order the caller passes in.
 */
export function useSelection<T = number>(resetKey: string) {
  const [selected, setSelected] = useState<Set<T>>(() => new Set())
  const anchor = useRef<number | null>(null)

  useEffect(() => {
    setSelected(new Set())
    anchor.current = null
  }, [resetKey])

  return {
    selected,
    has: (key: T) => selected.has(key),
    toggle(key: T, index: number) {
      setSelected(prev => {
        const next = new Set(prev)
        if (next.has(key)) next.delete(key); else next.add(key)
        return next
      })
      anchor.current = index
    },
    toggleRange(keys: T[], index: number) {
      const from = anchor.current ?? index
      const [lo, hi] = from <= index ? [from, index] : [index, from]
      setSelected(prev => new Set([...prev, ...keys.slice(lo, hi + 1)]))
      anchor.current = index
    },
    selectAll(keys: T[]) {
      setSelected(new Set(keys))
      anchor.current = null
    },
    clear() {
      setSelected(new Set())
      anchor.current = null
    },
  }
}
