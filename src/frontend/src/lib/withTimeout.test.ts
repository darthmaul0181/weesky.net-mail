import { afterEach, describe, expect, it, vi } from 'vitest'
import { RequestTimeoutError, withTimeout } from './withTimeout'

afterEach(() => { vi.useRealTimers() })

describe('withTimeout', () => {
  it('resolves with the request’s own value when it answers in time', async () => {
    await expect(withTimeout(() => Promise.resolve('done'), 1000)).resolves.toBe('done')
  })

  it('passes the request’s own failure through', async () => {
    await expect(withTimeout(() => Promise.reject(new Error('refused')), 1000)).rejects.toThrow('refused')
  })

  it('rejects with RequestTimeoutError and aborts the request once the delay runs out', async () => {
    vi.useFakeTimers()
    let signal: AbortSignal | undefined
    const pending = withTimeout(s => { signal = s; return new Promise(() => {}) }, 30_000)
    const settled = expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)

    vi.advanceTimersByTime(29_999)
    expect(signal?.aborted).toBe(false)
    vi.advanceTimersByTime(1)

    await settled
    expect(signal?.aborted).toBe(true)
  })
})
