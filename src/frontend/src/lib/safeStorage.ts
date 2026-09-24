export type Area = 'local' | 'session'

function storageOf(area: Area): Storage {
  return area === 'session' ? window.sessionStorage : window.localStorage
}

// A blocked store (private mode, a locked-down profile) throws on the accessor itself, not just
// on a method call, so every function here degrades to "nothing stored" / a no-op on any exception.

export function readStored(key: string, area: Area = 'local'): string | null {
  try {
    return storageOf(area).getItem(key)
  } catch {
    return null
  }
}

export function writeStored(key: string, value: string, area: Area = 'local'): void {
  try {
    storageOf(area).setItem(key, value)
  } catch { /* noop */ }
}

export function removeStored(key: string, area: Area = 'local'): void {
  try {
    storageOf(area).removeItem(key)
  } catch { /* noop */ }
}

export function storedKeys(area: Area = 'local'): string[] {
  try {
    return Object.keys(storageOf(area))
  } catch {
    return []
  }
}
