import { useCallback, useEffect, useRef, useState } from 'react'

/** Fade then collapse. The 140/160 split lives in the keyframes as percentages, so it follows. */
export const ROW_EXIT_MS = 300

const NONE: ReadonlySet<number> = new Set()

export interface RowExit {
  /** The uids currently playing their exit: the list draws them, the caches no longer will. */
  departing: ReadonlySet<number>
  /** Marks the batch, then fires `fire` — the caller's own `mutate` — once the exit has played. */
  depart: (uids: number[], fire: () => void) => void
}

/**
 * Holds a row on screen while it leaves, then fires the mutation that removes it.
 *
 * The order is forced rather than chosen: `useMoveMessages`' own `onMutate` drops the uid from the
 * list caches before the request goes out, so React unmounts the row in the same tick as the click
 * and there is no interval where it is still mounted and already leaving. Firing late is what
 * creates one. The mutation is optimistic, so the 300ms cost nothing anybody waits on.
 */
export function useRowExit(): RowExit {
  const [departing, setDeparting] = useState<ReadonlySet<number>>(NONE)
  // The set is mirrored in a ref so `depart` can read it without depending on it: a new identity
  // on every departure would re-render every row that takes it as a prop.
  const leaving = useRef(new Set<number>())
  const pending = useRef(new Map<() => void, ReturnType<typeof setTimeout>>())

  // A departure the user asked for is never cancelled by an unmount — it is fired early instead.
  useEffect(() => () => {
    const runs = [...pending.current.keys()]
    for (const id of pending.current.values()) clearTimeout(id)
    pending.current.clear()
    for (const run of runs) run()
  }, [])

  const depart = useCallback((uids: number[], fire: () => void) => {
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { fire(); return }

    const fresh = uids.filter(uid => !leaving.current.has(uid))
    if (fresh.length === 0) return  // A second click on a row already leaving arms nothing.
    for (const uid of fresh) leaving.current.add(uid)
    setDeparting(new Set(leaving.current))

    const run = () => {
      pending.current.delete(run)
      for (const uid of fresh) leaving.current.delete(uid)
      // Both land in one batch, so the order changes no render — it is released first so a `fire`
      // that throws cannot strand the uid here, invisible and no longer clickable.
      setDeparting(leaving.current.size === 0 ? NONE : new Set(leaving.current))
      fire()
    }
    pending.current.set(run, setTimeout(run, ROW_EXIT_MS))
  }, [])

  return { departing, depart }
}
