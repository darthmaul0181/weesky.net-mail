export class RequestTimeoutError extends Error {
  constructor() {
    super('Request timed out')
    this.name = 'RequestTimeoutError'
  }
}

/** Aborts `run` and rejects with RequestTimeoutError after `ms`, whether or not `run` honours the signal. */
export function withTimeout<T>(run: (signal: AbortSignal) => Promise<T>, ms: number): Promise<T> {
  const controller = new AbortController()
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => { controller.abort(); reject(new RequestTimeoutError()) }, ms)
    run(controller.signal).then(resolve, reject).finally(() => clearTimeout(timer))
  })
}

/** AbortSignal.any, done by hand where the browser predates it (Safari before 17.4).
    Listeners detach only on abort, fine since every signal passed in is per-request. */
export function anySignal(signals: AbortSignal[]): AbortSignal {
  if (typeof AbortSignal.any === 'function') return AbortSignal.any(signals)
  const controller = new AbortController()
  const early = signals.find(signal => signal.aborted)
  if (early) { controller.abort(early.reason); return controller.signal }
  const onAbort = (event: Event) => {
    for (const signal of signals) signal.removeEventListener('abort', onAbort)
    controller.abort((event.target as AbortSignal).reason)
  }
  for (const signal of signals) signal.addEventListener('abort', onAbort)
  return controller.signal
}
