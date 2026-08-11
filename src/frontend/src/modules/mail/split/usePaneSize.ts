import { useCallback, useState } from 'react'

/**
 * A pane size persisted per device — a 4K screen and a laptop have different ideal splits,
 * which is why this is localStorage and not a backend preference.
 */
export function usePaneSize(
  storageKey: string, defaultSize: number, min: number,
): [number, (next: number) => void] {
  const [size, setSize] = useState(() => {
    const stored = Number(localStorage.getItem(storageKey))
    return Number.isFinite(stored) && stored >= min ? Math.round(stored) : defaultSize
  })

  const update = useCallback((next: number) => {
    const clamped = Math.max(min, Math.round(next))
    setSize(clamped)
    localStorage.setItem(storageKey, String(clamped))
  }, [storageKey, min])

  return [size, update]
}
