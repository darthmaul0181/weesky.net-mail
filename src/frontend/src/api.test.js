import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

beforeEach(() => {
  localStorage.clear()
  vi.resetModules()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('hasSession', () => {
  it('is false with no session', async () => {
    const { hasSession } = await import('./api.js')
    expect(hasSession()).toBe(false)
  })

  it('is true after markLoggedIn', async () => {
    const { markLoggedIn, hasSession } = await import('./api.js')
    markLoggedIn()
    expect(hasSession()).toBe(true)
  })

  it('is false after clearSession', async () => {
    const { markLoggedIn, clearSession, hasSession } = await import('./api.js')
    markLoggedIn()
    clearSession()
    expect(hasSession()).toBe(false)
  })
})

describe('clearSession', () => {
  it('removes the session flag from localStorage', async () => {
    const { markLoggedIn, clearSession } = await import('./api.js')
    markLoggedIn()
    clearSession()
    expect(localStorage.getItem('sessionActive')).toBeNull()
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

  it('sends credentials: include on every request', async () => {
    mockFetch(200)
    const { api } = await import('./api.js')
    await api.getAliases()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ credentials: 'include' })
    )
  })
})

describe('api methods', () => {
  beforeEach(() => mockFetch(200))

  it('login calls POST /api/Login', async () => {
    const { api } = await import('./api.js')
    await api.login('user@example.com', 'pass')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Login'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('logout calls DELETE /api/Login', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')
    await api.logout()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Login'),
      expect.objectContaining({ method: 'DELETE' })
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

  it('clearSession resets isAdmin to false', async () => {
    const { markLoggedIn, setIsAdmin, clearSession, getIsAdmin } = await import('./api.js')
    markLoggedIn()
    setIsAdmin(true)
    clearSession()
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
  it('clears session and calls the unauthorized handler', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 }))
    const { markLoggedIn, setUnauthorizedHandler, hasSession, api } = await import('./api.js')
    markLoggedIn()
    const handler = vi.fn()
    setUnauthorizedHandler(handler)
    await expect(api.getAliases()).rejects.toThrow('Unauthorized')
    expect(handler).toHaveBeenCalledOnce()
    expect(hasSession()).toBe(false)
  })
})
