import { describe, it, expect } from 'vitest'
import { shouldRetry } from './retryPolicy'
import { RequestTimeoutError } from './withTimeout'

describe('shouldRetry', () => {
  it('retries an ordinary failure twice and then gives up', () => {
    const boom = new Error('boom')
    expect(shouldRetry(0, boom)).toBe(true)
    expect(shouldRetry(1, boom)).toBe(true)
    expect(shouldRetry(2, boom)).toBe(false)
  })

  // api.ts has already cleared the session: a retry only delays the redirect to /login.
  it('never retries an unauthorized failure', () => {
    expect(shouldRetry(0, Object.assign(new Error('nope'), { status: 401 }))).toBe(false)
  })

  // The account's stored password no longer decrypts; no round trip changes that.
  it('never retries a credentials conflict', () => {
    expect(shouldRetry(0, Object.assign(new Error('nope'), { status: 409 }))).toBe(false)
  })

  // The server already went 30 s without a word: a retry would triple the spinner before saying so.
  it('never retries a timed-out read', () => {
    expect(shouldRetry(0, new RequestTimeoutError())).toBe(false)
  })

  it('tolerates an error carrying no status', () => {
    expect(shouldRetry(0, null)).toBe(true)
  })
})
