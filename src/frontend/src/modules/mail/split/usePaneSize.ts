import { useCallback, useState } from 'react'
import { readStored, writeStored } from '../../../lib/safeStorage'

// Per device, in localStorage: a 4K screen and a laptop want different splits.
export function usePaneSize(
  storageKey: string, defaultSize: number, min: number,
): [number, (next: number) => void] {
  const [size, setSize] = useState(() => {
    const stored = Number(readStored(storageKey))
    return Number.isFinite(stored) && stored >= min ? Math.round(stored) : defaultSize
  })

  const update = useCallback((next: number) => {
    const clamped = Math.max(min, Math.round(next))
    setSize(clamped)
    writeStored(storageKey, String(clamped))
  }, [storageKey, min])

  return [size, update]
}
