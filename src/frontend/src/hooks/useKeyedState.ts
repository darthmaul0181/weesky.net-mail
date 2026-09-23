import { useState, type Dispatch, type SetStateAction } from 'react'

/** State that starts over whenever `key` changes, reset during render so no frame commits the old value. */
export function useKeyedState<T>(init: () => T, key: string): [T, Dispatch<SetStateAction<T>>] {
  const [state, setState] = useState(init)
  const [seenKey, setSeenKey] = useState(key)
  if (seenKey !== key) {
    const fresh = init()
    setSeenKey(key)
    setState(() => fresh)
    return [fresh, setState]
  }
  return [state, setState]
}
