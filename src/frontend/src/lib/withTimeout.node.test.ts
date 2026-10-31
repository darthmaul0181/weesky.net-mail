import { afterEach, describe, expect, it, vi } from 'vitest'
import { RequestTimeoutError, anySignal, withTimeout } from './withTimeout'

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals() })

describe('anySignal without AbortSignal.any', () => {
  const withoutAny = () =>
    vi.stubGlobal('AbortSignal', Object.assign(Object.create(AbortSignal) as object, { any: undefined }))

  it('aborts with the first reason and detaches from every input', () => {
    withoutAny()
    const [a, b] = [new AbortController(), new AbortController()]
    const removed = vi.spyOn(a.signal, 'removeEventListener')
    const combined = anySignal([a.signal, b.signal])

    b.abort('gone')

    expect(combined.reason).toBe('gone')
    expect(removed).toHaveBeenCalledWith('abort', expect.any(Function))
  })

  it('attaches nothing when a later input is already aborted', () => {
    withoutAny()
    const [a, b] = [new AbortController(), new AbortController()]
    b.abort('early')
    const added = vi.spyOn(a.signal, 'addEventListener')

    expect(anySignal([a.signal, b.signal]).reason).toBe('early')
    expect(added).not.toHaveBeenCalled()
  })
})

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
