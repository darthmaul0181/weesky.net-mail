import { useLayoutEffect, useMemo, useRef } from 'react'

/**
 * One forwarder per key, built once and never rebuilt, each reading the handler of the render it
 * is called in. A memoised row handed a callback object its list rebuilt every render re-draws
 * whenever anything does; this is what a long list hands its rows instead.
 *
 * The key set is read once, at mount: build the handlers unconditionally, or a key added later has
 * no forwarder and one removed leaves a forwarder that throws when it is called.
 */
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
      forwarders[key] = (...args) => latest.current[key](...args)
    }
    return forwarders as T
    // Built once: a dependency on the handlers is the very rebuild this hook exists to avoid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
}
