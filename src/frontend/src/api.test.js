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

  it('adminGetVirtualDomains calls GET /api/Admin/domains/virtuals', async () => {
    const { api } = await import('./api.js')
    await api.adminGetVirtualDomains()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/virtuals'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('adminAddVirtualDomainOwner calls PUT /api/Admin/domains/virtuals/:domainId', async () => {
    const { api } = await import('./api.js')
    await api.adminAddVirtualDomainOwner('dom1', 42)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/virtuals/dom1'),
      expect.objectContaining({ method: 'PUT' })
    )
  })

  it('adminRemoveVirtualDomainOwner calls DELETE /api/Admin/domains/virtuals/:domainId/:userId', async () => {
    const { api } = await import('./api.js')
    await api.adminRemoveVirtualDomainOwner('dom1', 42)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/virtuals/dom1/42'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })
})

describe('rules api methods', () => {
  beforeEach(async () => {
    vi.resetModules()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status: 200,
      ok: true,
      json: () => Promise.resolve({}),
    }))
    const { markLoggedIn } = await import('./api.js')
    markLoggedIn()
  })

  it('getFolders calls GET /api/Account/Folders', async () => {
    const { api } = await import('./api.js')
    await api.getFolders()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Account/Folders'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('getRuleProviders calls GET /api/Rules/Providers', async () => {
    const { api } = await import('./api.js')
    await api.getRuleProviders()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules/Providers'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('getRules calls GET /api/Rules', async () => {
    const { api } = await import('./api.js')
    await api.getRules()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('saveRules calls PUT /api/Rules with body', async () => {
    const { api } = await import('./api.js')
    const rules = [{ id: '1', name: 'r' }]
    await api.saveRules(rules, 'weesky', null)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ rules, providerId: 'weesky', scriptName: null }) })
    )
  })

  it('deleteRules calls DELETE /api/Rules', async () => {
    const { api } = await import('./api.js')
    await api.deleteRules()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('checkCompatibility calls POST /api/Rules/CompatibilityCheck', async () => {
    const { api } = await import('./api.js')
    await api.checkCompatibility('rainloop', [])
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules/CompatibilityCheck'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('getRawScript calls GET /api/Rules/Raw', async () => {
    const { api } = await import('./api.js')
    await api.getRawScript()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules/Raw'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('saveRawScript calls PUT /api/Rules/Raw with body', async () => {
    const { api } = await import('./api.js')
    await api.saveRawScript('keep;', 'myscript')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Rules/Raw'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ content: 'keep;', scriptName: 'myscript' }) })
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

describe('ApiError', () => {
  it('carries the HTTP status', async () => {
    mockFetch(502, { ok: false, text: JSON.stringify({ message: 'Unable to connect to the mail service' }) })
    const { api } = await import('./api.js')

    await expect(api.getMailFolders()).rejects.toMatchObject({
      name: 'ApiError',
      status: 502,
      message: 'Unable to connect to the mail service',
    })
  })

  it('exposes the backend error string as a code', async () => {
    mockFetch(404, { ok: false, text: JSON.stringify({ message: 'Message not found' }) })
    const { api } = await import('./api.js')

    await expect(api.getMailMessage('INBOX', 1)).rejects.toMatchObject({
      status: 404,
      code: 'Message not found',
    })
  })

  it('exposes the credentials code on a 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status: 401,
      text: () => Promise.resolve(JSON.stringify({ message: 'credentials_unavailable' })),
    }))
    const { api } = await import('./api.js')

    await expect(api.getMailFolders()).rejects.toMatchObject({ status: 401, code: 'credentials_unavailable' })
  })

  it('falls back to plain text when the body is not JSON', async () => {
    mockFetch(400, { ok: false, text: 'A folder name is required' })
    const { api } = await import('./api.js')

    await expect(api.getMailFolders()).rejects.toMatchObject({
      status: 400,
      message: 'A folder name is required',
      code: null,
    })
  })

  it('is still an Error, so existing catch blocks keep working', async () => {
    mockFetch(400, { ok: false, text: 'Bad Request' })
    const { api } = await import('./api.js')

    await expect(api.getMailFolders()).rejects.toBeInstanceOf(Error)
  })
})

describe('abort support', () => {
  it('passes the signal through to fetch', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')
    const controller = new AbortController()

    await api.getMailFolders({ signal: controller.signal })

    expect(globalThis.fetch.mock.calls[0][1].signal).toBe(controller.signal)
  })

  it('sends no signal when none is given', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')

    await api.getMailFolders()

    expect(globalThis.fetch.mock.calls[0][1].signal).toBeUndefined()
  })
})

describe('mail endpoints', () => {
  it('encodes folder paths, which may contain a slash', async () => {
    mockFetch(200, { json: {} })
    const { api } = await import('./api.js')

    await api.getMailMessages('INBOX/Projects', 0, 50)

    expect(globalThis.fetch.mock.calls[0][0]).toContain('folder=INBOX%2FProjects')
  })

  it('sends folder paths in the body for mutations', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.deleteMailFolder('INBOX/Projects')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Folders'),
      expect.objectContaining({ method: 'DELETE', body: JSON.stringify({ path: 'INBOX/Projects' }) })
    )
  })

  it('passes the subscription state', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setMailFolderSubscription('Projects', false)

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Folders/Subscription'),
      expect.objectContaining({ body: JSON.stringify({ path: 'Projects', subscribed: false }) })
    )
  })

  it('PUTs the batch flags body', async () => {
    mockFetch(200, { json: {} })
    const { api } = await import('./api.js')

    await api.setMessageFlags('INBOX/Sub', [1, 2], 'seen', true)

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Messages/Flags'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ folderPath: 'INBOX/Sub', uids: [1, 2], flag: 'seen', value: true }) })
    )
  })

  it('POSTs the move body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.moveMessages('INBOX/Sub', [1, 2], 'INBOX/Archive')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Messages/Move'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ folderPath: 'INBOX/Sub', uids: [1, 2], targetFolderPath: 'INBOX/Archive' }) })
    )
  })

  it('POSTs the copy body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.copyMessages('INBOX/Sub', [1, 2], 'INBOX/Archive')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Messages/Copy'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ folderPath: 'INBOX/Sub', uids: [1, 2], targetFolderPath: 'INBOX/Archive' }) })
    )
  })

  it('DELETEs with the folder and uids in the body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.deleteMessages('INBOX/Sub', [1, 2])

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Messages'),
      expect.objectContaining({ method: 'DELETE', body: JSON.stringify({ folderPath: 'INBOX/Sub', uids: [1, 2] }) })
    )
  })

  it('fetches folder roles', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')

    await api.getFolderRoles()

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/FolderRoles'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('sends the role and folder path in the body when setting a role', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setFolderRole('Sent', 'INBOX/Projects')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/FolderRoles'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ role: 'Sent', folderPath: 'INBOX/Projects' }) })
    )
  })

  it('sends the role as an encoded query parameter when clearing a role', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.clearFolderRole('Sent/Archive')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/FolderRoles?role=Sent%2FArchive'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })
})

describe('mailAttachmentUrl', () => {
  it('encodes both the folder and the part', async () => {
    const { mailAttachmentUrl } = await import('./api.js')

    const url = mailAttachmentUrl('INBOX/Projects', 42, '2.1')

    expect(url).toContain('folder=INBOX%2FProjects')
    expect(url).toContain('uid=42')
    expect(url).toContain('part=2.1')
  })
})

describe('requestBlob', () => {
  function mockBlobFetch({ status = 200, disposition = null, ok = true, text = '' } = {}) {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status,
      ok,
      statusText: text,
      headers: { get: (h) => (h.toLowerCase() === 'content-disposition' ? disposition : null) },
      blob: () => Promise.resolve(new Blob(['data'])),
      text: () => Promise.resolve(text),
    }))
  }

  it('returns the blob and the file name from Content-Disposition', async () => {
    mockBlobFetch({ disposition: 'attachment; filename="report.pdf"' })
    const { requestBlob } = await import('./api.js')

    const result = await requestBlob('/api/Mail/Messages/Attachment?folder=INBOX&uid=1&part=2')

    expect(result.fileName).toBe('report.pdf')
    expect(result.blob).toBeInstanceOf(Blob)
  })

  it('falls back to a default file name', async () => {
    mockBlobFetch({ disposition: null })
    const { requestBlob } = await import('./api.js')

    expect((await requestBlob('/x')).fileName).toBe('attachment')
  })

  it('throws an ApiError carrying the status on failure', async () => {
    mockBlobFetch({ status: 404, ok: false, text: 'Attachment not found' })
    const { requestBlob } = await import('./api.js')

    await expect(requestBlob('/x')).rejects.toMatchObject({ name: 'ApiError', status: 404 })
  })

  it('clears the session on a 401', async () => {
    mockBlobFetch({ status: 401, ok: false })
    const { markLoggedIn, setUnauthorizedHandler, hasSession, requestBlob } = await import('./api.js')
    markLoggedIn()
    const handler = vi.fn()
    setUnauthorizedHandler(handler)

    await expect(requestBlob('/x')).rejects.toThrow('Unauthorized')
    expect(handler).toHaveBeenCalledOnce()
    expect(hasSession()).toBe(false)
  })
})

describe('preferences', () => {
  it('reads every preference in one call', async () => {
    mockFetch(200, { json: { 'mail.pageSize': '30' } })
    const { api } = await import('./api.js')

    await api.getPreferences()

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Preferences'),
      expect.objectContaining({ method: 'GET' }))
  })

  it('sends the key and the value in the body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setPreference('mail.pageSize', '50')

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Preferences'),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ key: 'mail.pageSize', value: '50' }),
      }))
  })
})
