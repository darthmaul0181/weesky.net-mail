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

  it('throws with statusText when body text is empty', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status: 400,
      ok: false,
      text: () => Promise.resolve(''),
      statusText: 'Bad Request',
    }))
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

  it('getAccount calls GET /api/Account', async () => {
    const { api } = await import('./api.js')
    await api.getAccount()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Account'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('getQuota calls GET /api/Account/Quota', async () => {
    const { api } = await import('./api.js')
    await api.getQuota()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Account/Quota'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('changeFullName calls POST /api/Account/FullName', async () => {
    const { api } = await import('./api.js')
    await api.changeFullName('John Doe')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Account/FullName'),
      expect.objectContaining({ method: 'POST' })
    )
  })
})

describe('isAdmin state', () => {
  it('getIsAdmin is false by default', async () => {
    const { getIsAdmin } = await import('./api.js')
    expect(getIsAdmin()).toBe(false)
  })

  it('setIsAdmin(true) makes getIsAdmin return true', async () => {
    const { setIsAdmin, getIsAdmin } = await import('./api.js')
    setIsAdmin(true)
    expect(getIsAdmin()).toBe(true)
  })

  it('setIsAdmin(false) makes getIsAdmin return false', async () => {
    const { setIsAdmin, getIsAdmin } = await import('./api.js')
    setIsAdmin(true)
    setIsAdmin(false)
    expect(getIsAdmin()).toBe(false)
  })

  it('clearToken resets isAdmin to false', async () => {
    const { setToken, setIsAdmin, clearToken, getIsAdmin } = await import('./api.js')
    setToken('tok', 60)
    setIsAdmin(true)
    clearToken()
    expect(getIsAdmin()).toBe(false)
  })
})

describe('admin api methods', () => {
  beforeEach(() => mockFetch(200))

  it('adminGetUsers calls GET /api/Admin/users', async () => {
    const { api } = await import('./api.js')
    await api.adminGetUsers()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('adminCreateUser calls POST /api/Admin/users', async () => {
    const { api } = await import('./api.js')
    await api.adminCreateUser({ userName: 'alice', domainId: 'WSY', password: 'pw' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('adminUpdateUser calls PUT /api/Admin/users/:id', async () => {
    const { api } = await import('./api.js')
    await api.adminUpdateUser(5, { userName: 'alice' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users/5'),
      expect.objectContaining({ method: 'PUT' })
    )
  })

  it('adminDeleteUser calls DELETE /api/Admin/users/:id', async () => {
    const { api } = await import('./api.js')
    await api.adminDeleteUser(5)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users/5'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('adminGetDomains calls GET /api/Admin/domains', async () => {
    const { api } = await import('./api.js')
    await api.adminGetDomains()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('adminCreateDomain calls POST /api/Admin/domains', async () => {
    const { api } = await import('./api.js')
    await api.adminCreateDomain({ id: 'TST', name: 'test.com' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('adminUpdateDomain calls PUT /api/Admin/domains/:id', async () => {
    const { api } = await import('./api.js')
    await api.adminUpdateDomain('WSY', { name: 'new.com' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/WSY'),
      expect.objectContaining({ method: 'PUT' })
    )
  })

  it('adminDeleteDomain calls DELETE /api/Admin/domains/:id', async () => {
    const { api } = await import('./api.js')
    await api.adminDeleteDomain('WSY')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/WSY'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('adminGetUserQuota calls GET /api/Admin/users/:id/quota', async () => {
    const { api } = await import('./api.js')
    await api.adminGetUserQuota(5)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users/5/quota'),
      expect.objectContaining({ method: 'GET' })
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
