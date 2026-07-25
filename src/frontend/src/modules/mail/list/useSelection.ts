import { useEffect, useRef, useState } from 'react'

/**
 * Checkbox selection over the loaded rows. `resetKey` (folder + page) clears it; the hook never
 * stores the row list, so the caller intersects `selected` with what is on screen — a departed
 * row stops counting on its own. `toggleRange` selects the inclusive slice from the last-toggled
 * anchor to `index`, over the `loadedUids` order the caller passes in.
 */
export function useSelection(resetKey: string) {
  const [selected, setSelected] = useState<Set<number>>(() => new Set())
  const anchor = useRef<number | null>(null)

  useEffect(() => {
    setSelected(new Set())
    anchor.current = null
  }, [resetKey])

  return {
    selected,
    has: (uid: number) => selected.has(uid),
    toggle(uid: number, index: number) {
      setSelected(prev => {
        const next = new Set(prev)
        if (next.has(uid)) next.delete(uid); else next.add(uid)
        return next
      })
      anchor.current = index
    },
    toggleRange(loadedUids: number[], index: number) {
      const from = anchor.current ?? index
      const [lo, hi] = from <= index ? [from, index] : [index, from]
      setSelected(prev => new Set([...prev, ...loadedUids.slice(lo, hi + 1)]))
      anchor.current = index
    },
    selectAll(loadedUids: number[]) {
      setSelected(new Set(loadedUids))
      anchor.current = null
    },
    clear() {
      setSelected(new Set())
      anchor.current = null
    },
  }
}
