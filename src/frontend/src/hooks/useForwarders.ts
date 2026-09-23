import { useLayoutEffect, useMemo, useRef } from 'react'

/** One forwarder per key, built once, each calling the handler of the latest render, so memoised
 * rows get stable callbacks. The key set is read at mount: build handlers unconditionally, or a
 * later key has no forwarder and a removed one throws. */
export function useForwarders<T extends Record<string, (...args: never[]) => unknown>>(
  handlers: T,
): T {
  const latest = useRef(handlers)
  useLayoutEffect(() => { latest.current = handlers })
  // A loop rather than `Object.fromEntries`, so the ref is never handed to a function
  // `react-hooks/refs` has to assume reads it during the render.
  return useMemo(() => {
    const forwarders: Record<string, (...args: never[]) => unknown> = {}
    for (const key of Object.keys(handlers)) {
      // key comes from Object.keys(handlers), so it is present in latest.current, which
      // always holds the same fixed key set (see the hook's own contract above).
      forwarders[key] = (...args) => latest.current[key]!(...args)
    }
    return forwarders as T
    // Built once: a dependency on the handlers is the very rebuild this hook exists to avoid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
}
