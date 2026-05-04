import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

beforeEach(() => {
  localStorage.clear()
  vi.resetModules()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('hasToken', () => {
  it('is false with no token', async () => {
    const { hasToken } = await import('./api.js')
    expect(hasToken()).toBe(false)
  })

  it('is true after setToken', async () => {
    const { setToken, hasToken } = await import('./api.js')
    setToken('tok', 60)
    expect(hasToken()).toBe(true)
  })

  it('is false after clearToken', async () => {
    const { setToken, clearToken, hasToken } = await import('./api.js')
    setToken('tok', 60)
    clearToken()
    expect(hasToken()).toBe(false)
  })
})

describe('setToken', () => {
  it('with persist writes token and expiry to localStorage', async () => {
    const { setToken } = await import('./api.js')
    const before = Date.now()
    setToken('tok', 60, true)
    expect(localStorage.getItem('authToken')).toBe('tok')
    expect(Number(localStorage.getItem('authExpiry'))).toBeGreaterThan(before)
  })

  it('without persist clears localStorage', async () => {
    localStorage.setItem('authToken', 'old')
    localStorage.setItem('authExpiry', '9999')
    const { setToken } = await import('./api.js')
    setToken('tok', 60, false)
    expect(localStorage.getItem('authToken')).toBeNull()
    expect(localStorage.getItem('authExpiry')).toBeNull()
  })
})

describe('clearToken', () => {
  it('removes token and expiry from localStorage', async () => {
    const { setToken, clearToken } = await import('./api.js')
    setToken('tok', 60, true)
    clearToken()
    expect(localStorage.getItem('authToken')).toBeNull()
    expect(localStorage.getItem('authExpiry')).toBeNull()
  })
})

describe('token restoration on module load', () => {
  it('restores a valid persisted token', async () => {
    localStorage.setItem('authToken', 'saved')
    localStorage.setItem('authExpiry', String(Date.now() + 60_000))
    const { hasToken } = await import('./api.js')
    expect(hasToken()).toBe(true)
  })

  it('discards an expired token', async () => {
    localStorage.setItem('authToken', 'saved')
    localStorage.setItem('authExpiry', String(Date.now() - 1_000))
    const { hasToken } = await import('./api.js')
    expect(hasToken()).toBe(false)
  })

  it('clears localStorage when token is expired', async () => {
    localStorage.setItem('authToken', 'saved')
    localStorage.setItem('authExpiry', String(Date.now() - 1_000))
    await import('./api.js')
    expect(localStorage.getItem('authToken')).toBeNull()
    expect(localStorage.getItem('authExpiry')).toBeNull()
  })
})

describe('401 handling', () => {
  it('clears token and calls the unauthorized handler', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 }))
    const { setToken, setUnauthorizedHandler, hasToken, api } = await import('./api.js')

    setToken('tok', 60)
    const handler = vi.fn()
    setUnauthorizedHandler(handler)

    await expect(api.getAliases()).rejects.toThrow('Unauthorized')
    expect(handler).toHaveBeenCalledOnce()
    expect(hasToken()).toBe(false)
  })
})
