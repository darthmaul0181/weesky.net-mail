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

function mockFetch(status, { json, text, ok } = {}) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    status,
    ok: ok ?? (status >= 200 && status < 300),
    json: () => Promise.resolve(json ?? {}),
    text: () => Promise.resolve(text ?? ''),
    statusText: text ?? '',
  }))
}

describe('request — response handling', () => {
  it('returns null on 204', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')
    await expect(api.getAliases()).resolves.toBeNull()
  })

  it('returns parsed JSON on 200', async () => {
    const data = [{ name: 'alias', domain: 'example.com' }]
    mockFetch(200, { json: data })
    const { api } = await import('./api.js')
    await expect(api.getAliases()).resolves.toEqual(data)
  })

  it('throws with body text on non-ok response', async () => {
    mockFetch(400, { ok: false, text: 'Bad Request' })
    const { api } = await import('./api.js')
    await expect(api.getAliases()).rejects.toThrow('Bad Request')
  })
})

describe('api methods', () => {
  beforeEach(() => mockFetch(200))

  it('login calls POST /api/BearerAuthenticator', async () => {
    const { api } = await import('./api.js')
    await api.login('user@example.com', 'pass')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/BearerAuthenticator'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('createAlias calls POST /api/Aliases', async () => {
    const { api } = await import('./api.js')
    await api.createAlias('test', 'example.com')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Aliases'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('deleteAlias calls DELETE /api/Aliases', async () => {
    const { api } = await import('./api.js')
    await api.deleteAlias('test', 'example.com')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Aliases'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('changePassword calls PATCH /api/Account/ChangeSecret', async () => {
    const { api } = await import('./api.js')
    await api.changePassword('old', 'new')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Account/ChangeSecret'),
      expect.objectContaining({ method: 'PATCH' })
    )
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
