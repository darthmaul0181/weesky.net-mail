import { describe, it, expect } from 'vitest'
import { shouldRetry } from './retryPolicy'

describe('shouldRetry', () => {
  it('retries an ordinary failure twice and then gives up', () => {
    const boom = new Error('boom')
    expect(shouldRetry(0, boom)).toBe(true)
    expect(shouldRetry(1, boom)).toBe(true)
    expect(shouldRetry(2, boom)).toBe(false)
  })

  // api.js has already cleared the session: a retry only delays the redirect to /login.
  it('never retries an unauthorized failure', () => {
    expect(shouldRetry(0, Object.assign(new Error('nope'), { status: 401 }))).toBe(false)
  })

  // The account's stored password no longer decrypts; no round trip changes that.
  it('never retries a credentials conflict', () => {
    expect(shouldRetry(0, Object.assign(new Error('nope'), { status: 409 }))).toBe(false)
  })

  it('tolerates an error carrying no status', () => {
    expect(shouldRetry(0, null)).toBe(true)
  })
})
