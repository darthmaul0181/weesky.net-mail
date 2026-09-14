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
