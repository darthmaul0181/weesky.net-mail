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

/**
 * Whether a pane is folded away, persisted beside its size and for the same reason: a 4K screen
 * has room for a column a laptop would rather spend on the reader, so this is the device's answer
 * and not the account's. Only `'true'` folds — an absent key, and any value a future build does
 * not recognise, leaves the pane where the user last saw it rather than hiding it.
 */
export function usePaneCollapsed(storageKey: string): [boolean, (next: boolean) => void] {
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(storageKey) === 'true')

  const update = useCallback((next: boolean) => {
    setCollapsed(next)
    localStorage.setItem(storageKey, String(next))
  }, [storageKey])

  return [collapsed, update]
}
