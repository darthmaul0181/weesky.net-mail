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

  it('throws with the ProblemDetails title rather than the raw JSON body', async () => {
    mockFetch(400, {
      ok: false,
      text: JSON.stringify({
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { part: ['The part field is required.'] },
      }),
    })
    const { api } = await import('./api.js')

    await expect(api.getAliases()).rejects.toMatchObject({
      message: 'One or more validation errors occurred.',
      code: null,
    })
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

  it('getIdentities calls GET /api/Identities', async () => {
    const { api } = await import('./api.js')
    await api.getIdentities()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Identities'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('putIdentities PUTs the whole list under an identities key', async () => {
    const { api } = await import('./api.js')
    const rows = [{ address: 'a@x.be', displayName: 'A', isDefault: true }]
    await api.putIdentities(rows)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Identities'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ identities: rows }) })
    )
  })

  it('getContacts calls GET /api/Contacts', async () => {
    const { api } = await import('./api.js')
    await api.getContacts()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('createContact POSTs the contact', async () => {
    const { api } = await import('./api.js')
    const draft = {
      firstName: 'Bruno', lastName: 'Mertens', nickname: null,
      isFavorite: false, addresses: ['bruno@example.com'],
    }
    await api.createContact(draft)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify(draft) })
    )
  })

  it('updateContact PUTs to the contact id', async () => {
    const { api } = await import('./api.js')
    await api.updateContact('11111111-1111-1111-1111-111111111111', { firstName: 'B' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/11111111-1111-1111-1111-111111111111'),
      expect.objectContaining({ method: 'PUT' })
    )
  })

  it('deleteContact DELETEs the contact id', async () => {
    const { api } = await import('./api.js')
    await api.deleteContact('22222222-2222-2222-2222-222222222222')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/22222222-2222-2222-2222-222222222222'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('setContactFavorite PUTs the flag to the Favorite sub-route', async () => {
    const { api } = await import('./api.js')
    await api.setContactFavorite('33333333-3333-3333-3333-333333333333', true)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/33333333-3333-3333-3333-333333333333/Favorite'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ isFavorite: true }) })
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

  it('posts an imported CSV as multipart without a JSON content type', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true, status: 200, json: async () => ({ created: 1, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [] }),
    })

    const file = new File(['First Name\r\nBruno'], 'contacts.csv', { type: 'text/csv' })
    const { api } = await import('./api.js')
    const report = await api.importContacts(file)

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Contacts/Import')
    expect(options.body).toBeInstanceOf(FormData)
    expect(options.body.get('file')).toBe(file)
    // The browser has to set the multipart boundary itself; naming a type here breaks the parse.
    expect(options.headers['Content-Type']).toBeUndefined()
    expect(report.created).toBe(1)
  })

  it('fetches the export as a blob with the served file name', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: () => 'attachment; filename="contacts-2026-07-27.csv"' },
      blob: async () => new Blob(['x']),
    })

    const { api } = await import('./api.js')
    const result = await api.exportContacts()

    expect(globalThis.fetch.mock.calls[0][0]).toContain('/api/Contacts/Export')
    expect(result.fileName).toBe('contacts-2026-07-27.csv')
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

  it('posts search criteria with paging', async () => {
    mockFetch(200, { json: { total: 0, page: 0, pageSize: 50, results: [] } })
    const { api } = await import('./api.js')

    await api.searchMessages({ folderPath: 'INBOX', allFolders: false, quick: 'hello' }, 0, 50)

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toBe('https://api.mail.weesky.net/api/Mail/Messages/Search')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({
      folderPath: 'INBOX', allFolders: false, quick: 'hello', page: 0, pageSize: 50,
    })
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

// Unlike mailAttachmentUrl, this one is an <img src>, not a request path: a relative URL would
// resolve against the SPA origin, where the endpoint does not exist.
describe('stagedAttachmentUrl', () => {
  it('is absolute, against the API origin', async () => {
    const { stagedAttachmentUrl, API_BASE } = await import('./api.js')

    const url = stagedAttachmentUrl('abc')

    expect(url).toBe(`${API_BASE}/api/Mail/Attachments/abc/content`)
    expect(url).toMatch(/^https?:\/\//)
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

describe('sendMessage', () => {
  it('posts the payload', async () => {
    mockFetch(200, { json: { appendedToSent: true } })
    const { api } = await import('./api.js')

    await api.sendMessage({ to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [] })

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Mail/Send')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body).to[0]).toBe('a@b.c')
  })
})

describe('saveDraft', () => {
  it('posts the payload to /api/Mail/Drafts', async () => {
    mockFetch(200, { json: { uid: 7, folderPath: 'Drafts' } })
    const { api } = await import('./api.js')

    const result = await api.saveDraft({ to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [] })

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Mail/Drafts')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body).to[0]).toBe('a@b.c')
    expect(result).toEqual({ uid: 7, folderPath: 'Drafts' })
  })
})

describe('openDraft', () => {
  it('posts { folder, uid } to /api/Mail/Drafts/Open', async () => {
    mockFetch(200, { json: { to: [], cc: [], bcc: [], subject: '', fromAddress: null, htmlBody: '', attachments: [], inReplyTo: null, references: [] } })
    const { api } = await import('./api.js')

    await api.openDraft('Drafts', 7)

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Mail/Drafts/Open')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ folder: 'Drafts', uid: 7 })
  })
})

describe('deleteAttachment', () => {
  it('targets the id', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.deleteAttachment('11111111-2222-3333-4444-555555555555')

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Mail/Attachments/11111111-2222-3333-4444-555555555555'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })
})

describe('uploadAttachment', () => {
  let sent
  class FakeXhr {
    upload = {}
    open(method, url) { this.method = method; this.url = url }
    send(form) { sent = { xhr: this, form } }
    abort() {}
  }
  beforeEach(() => { sent = undefined; vi.stubGlobal('XMLHttpRequest', FakeXhr) })
  afterEach(() => { vi.unstubAllGlobals() })

  it('resolves with the parsed body on 200', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    sent.xhr.status = 200
    sent.xhr.responseText = '{"id":"i","fileName":"a.txt","size":1,"contentType":"text/plain"}'
    sent.xhr.onload()
    await expect(done).resolves.toEqual({ id: 'i', fileName: 'a.txt', size: 1, contentType: 'text/plain' })
    expect(sent.xhr.withCredentials).toBe(true)
    expect(sent.xhr.url).toContain('/api/Mail/Attachments')
  })

  it('rejects with the enveloppe message and reports progress', async () => {
    const { uploadAttachment } = await import('./api.js')
    const onProgress = vi.fn()
    const done = uploadAttachment(new File(['x'], 'a.txt'), { onProgress })
    sent.xhr.upload.onprogress({ lengthComputable: true, loaded: 1, total: 2 })
    sent.xhr.status = 400
    sent.xhr.responseText = '{"message":"The attachment exceeds the 25 MB limit"}'
    sent.xhr.onload()
    await expect(done).rejects.toThrow('The attachment exceeds the 25 MB limit')
    expect(onProgress).toHaveBeenCalledWith(0.5)
  })

  it('rejects with the code from the error envelope on a 401', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    sent.xhr.status = 401
    sent.xhr.responseText = '{"message":"credentials_unavailable"}'
    sent.xhr.onload()
    await expect(done).rejects.toMatchObject({ status: 401, code: 'credentials_unavailable' })
  })

  it('aborts the xhr and rejects when the signal fires mid-flight', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    const done = uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })
    const abortSpy = vi.spyOn(sent.xhr, 'abort')
    const removeSpy = vi.spyOn(controller.signal, 'removeEventListener')
    controller.abort()
    await expect(done).rejects.toThrow('Aborted')
    expect(abortSpy).toHaveBeenCalledOnce()
    expect(removeSpy).toHaveBeenCalledWith('abort', expect.any(Function))
  })

  it('detaches the abort listener once the request settles', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    const removeSpy = vi.spyOn(controller.signal, 'removeEventListener')
    const done = uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })
    sent.xhr.status = 200
    sent.xhr.responseText = '{"id":"i"}'
    sent.xhr.onload()
    await done
    expect(removeSpy).toHaveBeenCalledWith('abort', expect.any(Function))
  })

  it('rejects immediately when the signal is already aborted', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    controller.abort()
    await expect(uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })).rejects.toThrow('Aborted')
    expect(sent).toBeUndefined()
  })
})

describe('trusted senders', () => {
  it('getTrustedSenders reads the list', async () => {
    mockFetch(200, { json: ['news@example.com'] })
    const { api } = await import('./api.js')

    await expect(api.getTrustedSenders()).resolves.toEqual(['news@example.com'])

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/TrustedSenders')
    expect(options.method).toBe('GET')
  })

  it('trustSender posts the address', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.trustSender('news@example.com')

    const [, options] = globalThis.fetch.mock.calls[0]
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ address: 'news@example.com' })
  })

  // A '+' is a legal local-part character and decodes to a space, so an unencoded query string
  // would untrust a different address than the one asked for.
  it('untrustSender encodes the address into the query string', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.untrustSender('news+weekly@example.com')

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('address=news%2Bweekly%40example.com')
    expect(options.method).toBe('DELETE')
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

describe('app settings', () => {
  it('reads every app setting in one call', async () => {
    mockFetch(200, { json: { 'app.name': 'Snoopy mail' } })
    const { api } = await import('./api.js')

    await expect(api.getAppSettings()).resolves.toEqual({ 'app.name': 'Snoopy mail' })
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({ method: 'GET' }))
  })

  it('setAppSetting sends the key and the value in the body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setAppSetting('app.name', 'Snoopy mail')

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ key: 'app.name', value: 'Snoopy mail' }),
      }))
  })
})
