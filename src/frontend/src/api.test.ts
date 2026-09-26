import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { ContactDraft } from './modules/contacts/contactTypes'
import type { ExternalDomainPayload } from './modules/settings/admin/useExternalDomains'

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

function mockFetch(status: number, { json, text, ok }: { json?: unknown; text?: string; ok?: boolean } = {}) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    status,
    ok: ok ?? (status >= 200 && status < 300),
    json: () => Promise.resolve(json ?? {}),
    text: () => Promise.resolve(text ?? ''),
    statusText: text ?? '',
  }))
}

/** The init request() hands fetch: its headers are always a plain record. */
interface SentInit extends RequestInit { headers: Record<string, string> }

// request() always calls fetch with a string URL and a record of headers; fetch's own type is wider.
const fetchCall = (n = 0) => vi.mocked(fetch).mock.calls[n] as [string, SentInit]
/** The JSON a request() carried; a body that is not a string fails the parse, and the test with it. */
const sentJson = (options: SentInit): unknown => JSON.parse(typeof options.body === 'string' ? options.body : '')

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

  const draft: ContactDraft = {
    firstName: 'Bruno', lastName: 'Mertens', nickname: null, displayName: null, middleName: null,
    namePrefix: null, nameSuffix: null, organization: null, department: null, jobTitle: null,
    birthday: null, website: null, notes: null, photo: null, isFavorite: false,
    addresses: [{ position: null, address: 'bruno@example.com', type: '', pref: null }],
  }

  it('createContact POSTs the contact', async () => {
    const { api } = await import('./api.js')
    await api.createContact(draft)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify(draft) })
    )
  })

  it('updateContact PUTs to the contact id', async () => {
    const { api } = await import('./api.js')
    await api.updateContact('11111111-1111-1111-1111-111111111111', { ...draft, firstName: 'B' })
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

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Contacts/Import')
    const body = options.body
    expect(body).toBeInstanceOf(FormData)
    expect(body instanceof FormData ? body.get('file') : null).toBe(file)
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

    expect(fetchCall()[0]).toContain('/api/Contacts/Export')
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
    await api.adminCreateUser({
      userName: 'alice', domainId: 'WSY', password: 'pw', fullName: '', quotaMb: 1024, active: true, admin: false,
    })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/users'),
      expect.objectContaining({ method: 'POST' })
    )
  })

  it('adminUpdateUser calls PUT /api/Admin/users/:id', async () => {
    const { api } = await import('./api.js')
    await api.adminUpdateUser(5, {
      userName: 'alice', domainId: 'WSY', password: null, fullName: '', quotaMb: 1024, active: true, admin: false,
    })
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
    await api.adminUpdateDomain('WSY', { id: 'WSY', name: 'new.com' })
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

  // Absent by default rather than false: the API treats the flag's presence as the acknowledgement.
  it('adminDeleteDomain omits the alias acknowledgement unless it is given', async () => {
    const { api } = await import('./api.js')
    await api.adminDeleteDomain('WSY')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.not.stringContaining('deleteAliases'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('adminDeleteDomain carries the alias acknowledgement when confirmed', async () => {
    const { api } = await import('./api.js')
    await api.adminDeleteDomain('WSY', true)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/WSY?deleteAliases=true'),
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
    const rules = [{ id: '1', name: 'r', enabled: true, matchAll: true, stopAfter: false, conditions: [], actions: [] }]
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
  it('lets a caller abort reach fetch', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')
    const controller = new AbortController()

    await api.getMailFolders({ signal: controller.signal })
    controller.abort()

    expect(fetchCall()[1].signal?.aborted).toBe(true)
  })

  it('sends no signal on a write when none is given', async () => {
    mockFetch(200, { json: 'INBOX/New' })
    const { api } = await import('./api.js')

    await api.createMailFolder('INBOX', 'New')

    expect(fetchCall()[1].signal).toBeUndefined()
  })
})

describe('read deadline', () => {
  afterEach(() => { vi.useRealTimers() })

  /** A fetch that settles only when its signal aborts, as a frozen backend would. */
  const hungFetch = () => vi.fn((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init.signal?.addEventListener('abort', () => reject(init.signal?.reason as Error))
  }))

  it('rejects a hung GET with RequestTimeoutError once 30 s have passed, and aborts it', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', hungFetch())
    const { api } = await import('./api.js')
    const { RequestTimeoutError } = await import('./lib/withTimeout')

    const pending = api.getMailFolders()
    const settled = expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(29_999)
    expect(fetchCall()[1].signal?.aborted).toBe(false)
    await vi.advanceTimersByTimeAsync(1)

    await settled
    expect(fetchCall()[1].signal?.aborted).toBe(true)
  })

  // The backend builds the whole file before its first byte: an attachment, an export.
  it.each([
    ['requestBlob', (m: typeof import('./api.js')) => m.requestBlob('/api/Contacts/Export')],
    ['the message source', (m: typeof import('./api.js')) => m.api.getMessageSource('INBOX', 1)],
  ])('gives %s two minutes', async (_name, run) => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', hungFetch())
    const api = await import('./api.js')
    const { RequestTimeoutError } = await import('./lib/withTimeout')

    const settled = expect(run(api)).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(119_999)
    expect(fetchCall()[1].signal?.aborted).toBe(false)
    await vi.advanceTimersByTimeAsync(1)

    await settled
  })

  it('takes a per-call deadline', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', hungFetch())
    const { request } = await import('./api.js')
    const { RequestTimeoutError } = await import('./lib/withTimeout')

    const settled = expect(request('GET', '/api/Account', undefined, { timeoutMs: 5_000 }))
      .rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(5_000)

    await settled
  })

  it('lets a caller abort win, as an abort and not a timeout', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', hungFetch())
    const { api } = await import('./api.js')
    const controller = new AbortController()

    const pending = api.getMailFolders({ signal: controller.signal })
    const settled = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    await vi.advanceTimersByTimeAsync(10_000)
    controller.abort()

    await settled
  })

  it('combines the signals by hand where AbortSignal.any is missing', async () => {
    vi.stubGlobal('AbortSignal', Object.assign(Object.create(AbortSignal) as object, { any: undefined }))
    vi.stubGlobal('fetch', hungFetch())
    const { api } = await import('./api.js')
    const controller = new AbortController()

    const pending = api.getMailFolders({ signal: controller.signal })
    const settled = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    controller.abort()

    await settled
  })

  it('never cuts a write', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', hungFetch())
    const { api } = await import('./api.js')
    let outcome = 'pending'
    void api.sendMessage({} as never).then(() => { outcome = 'resolved' }, () => { outcome = 'rejected' })

    await vi.advanceTimersByTimeAsync(10 * 60_000)

    expect(outcome).toBe('pending')
    expect(fetchCall()[1].signal).toBeUndefined()
  })

  it('does not cut a slow body once the headers are in', async () => {
    vi.useFakeTimers()
    let deliver: (blob: Blob) => void = () => {}
    let signal: AbortSignal | null | undefined
    vi.stubGlobal('fetch', vi.fn((_url: string, init: RequestInit) => {
      signal = init.signal
      return Promise.resolve({
        status: 200, ok: true, headers: { get: () => null },
        blob: () => new Promise<Blob>(resolve => { deliver = resolve }),
      })
    }))
    const { requestBlob } = await import('./api.js')

    const pending = requestBlob('/api/Calendars/1/Export')
    await vi.advanceTimersByTimeAsync(5 * 60_000)
    expect(signal?.aborted).toBe(false)
    deliver(new Blob(['ics']))

    await expect(pending).resolves.toMatchObject({ fileName: 'attachment' })
  })
})

describe('account transport', () => {
  it('sends X-Account-Id for a connected account', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')

    await api.getMailFolders({ accountId: '11111111-1111-1111-1111-111111111111' })

    expect(fetchCall()[1].headers['X-Account-Id']).toBe('11111111-1111-1111-1111-111111111111')
  })

  it('sends no X-Account-Id header for the primary account', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')

    await api.getMailFolders({ accountId: 'primary' })

    expect(fetchCall()[1].headers['X-Account-Id']).toBeUndefined()
  })

  it('sends no X-Account-Id header when unset', async () => {
    mockFetch(200, { json: [] })
    const { api } = await import('./api.js')

    await api.getMailFolders()

    expect(fetchCall()[1].headers['X-Account-Id']).toBeUndefined()
  })

  it('requestBlob sends X-Account-Id for a connected account', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status: 200,
      ok: true,
      headers: { get: () => null },
      blob: () => Promise.resolve(new Blob(['x'])),
      text: () => Promise.resolve(''),
    }))
    const { requestBlob } = await import('./api.js')

    await requestBlob('/api/Mail/Messages/Attachment?folder=INBOX&uid=1&part=1', { accountId: 'acct-2' })

    expect(fetchCall()[1].headers['X-Account-Id']).toBe('acct-2')
  })

  it('requestBlob sends no header for the primary account', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status: 200,
      ok: true,
      headers: { get: () => null },
      blob: () => Promise.resolve(new Blob(['x'])),
      text: () => Promise.resolve(''),
    }))
    const { requestBlob } = await import('./api.js')

    await requestBlob('/api/Mail/Messages/Attachment?folder=INBOX&uid=1&part=1', { accountId: 'primary' })

    expect(fetchCall()[1].headers['X-Account-Id']).toBeUndefined()
  })
})

describe('connected accounts', () => {
  beforeEach(() => mockFetch(200))

  it('getConnectedAccounts calls GET /api/ConnectedAccounts', async () => {
    const { api } = await import('./api.js')
    await api.getConnectedAccounts()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('connectAccount POSTs to /api/ConnectedAccounts', async () => {
    const { api } = await import('./api.js')
    await api.connectAccount('dom-1', 'a@b.c', 'secret')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ domainId: 'dom-1', email: 'a@b.c', password: 'secret' }),
      })
    )
  })

  it('updateConnectedAccountPassword PUTs to the Password sub-route', async () => {
    const { api } = await import('./api.js')
    await api.updateConnectedAccountPassword('acct-1', 'newpass')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts/acct-1/Password'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ password: 'newpass' }) })
    )
  })

  it('deleteConnectedAccount DELETEs the account id', async () => {
    const { api } = await import('./api.js')
    await api.deleteConnectedAccount('acct-1')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts/acct-1'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('startOAuthConnect POSTs the target to the Start sub-route', async () => {
    const { api } = await import('./api.js')
    await api.startOAuthConnect({ domainId: 'd1' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts/OAuth/Start'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ domainId: 'd1', accountId: null }),
      })
    )
  })

  it('completeOAuthConnect POSTs the state to the Complete sub-route', async () => {
    const { api } = await import('./api.js')
    await api.completeOAuthConnect('s1')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts/OAuth/Complete'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ state: 's1' }) })
    )
  })

  it('getConnectableDomains calls GET /api/ConnectedAccounts/Domains', async () => {
    const { api } = await import('./api.js')
    await api.getConnectableDomains()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ConnectedAccounts/Domains'),
      expect.objectContaining({ method: 'GET' })
    )
  })
})

describe('admin external domains', () => {
  beforeEach(() => mockFetch(200))

  it('adminGetExternalDomains calls GET /api/Admin/domains/external', async () => {
    const { api } = await import('./api.js')
    await api.adminGetExternalDomains()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/external'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  const domain: ExternalDomainPayload = {
    name: 'example.com', imapHost: 'imap.example.com', imapPort: 993, imapSecurity: 'SslOnConnect',
    smtpHost: 'smtp.example.com', smtpPort: 587, smtpSecurity: 'StartTls', sieveHost: null, sievePort: null,
    authMode: 'Password', oauthAuthorizationUrl: null, oauthTokenUrl: null, oauthScopes: null,
    oauthClientId: null, oauthClientSecret: null,
  }

  it('adminCreateExternalDomain POSTs the domain', async () => {
    const { api } = await import('./api.js')
    await api.adminCreateExternalDomain(domain)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/external'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify(domain) })
    )
  })

  it('adminUpdateExternalDomain PUTs to the domain id', async () => {
    const { api } = await import('./api.js')
    await api.adminUpdateExternalDomain('dom-1', domain)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/external/dom-1'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify(domain) })
    )
  })

  it('adminDeleteExternalDomain DELETEs the domain id', async () => {
    const { api } = await import('./api.js')
    await api.adminDeleteExternalDomain('dom-1')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Admin/domains/external/dom-1'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })
})

describe('mail endpoints', () => {
  it('encodes folder paths, which may contain a slash', async () => {
    mockFetch(200, { json: {} })
    const { api } = await import('./api.js')

    await api.getMailMessages('INBOX/Projects', 0, 50)

    expect(fetchCall()[0]).toContain('folder=INBOX%2FProjects')
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
    const { api, API_BASE } = await import('./api.js')

    // Guards the comparisons below against a silently undefined API_BASE (e.g. test.env not
    // reaching import.meta.env), which would otherwise make every `${API_BASE}/...` assertion
    // here pass vacuously against 'undefined/...'.
    expect(API_BASE).toMatch(/^https:\/\//)

    await api.searchMessages({ folderPath: 'INBOX', allFolders: false, quick: 'hello' }, 0, 50)

    const [url, options] = fetchCall()
    expect(url).toBe(`${API_BASE}/api/Mail/Messages/Search`)
    expect(options.method).toBe('POST')
    expect(sentJson(options)).toEqual({
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

  it('carries no account param when unset or primary', async () => {
    const { mailAttachmentUrl } = await import('./api.js')

    expect(mailAttachmentUrl('INBOX', 1, '1')).not.toContain('account=')
    expect(mailAttachmentUrl('INBOX', 1, '1', 'primary')).not.toContain('account=')
  })

  it('carries &account= for a connected account', async () => {
    const { mailAttachmentUrl } = await import('./api.js')

    const url = mailAttachmentUrl('INBOX', 1, '1', 'acct-2')

    expect(url).toContain('&account=acct-2')
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

  it('carries no account param when unset or primary', async () => {
    const { stagedAttachmentUrl } = await import('./api.js')

    expect(stagedAttachmentUrl('abc')).not.toContain('account=')
    expect(stagedAttachmentUrl('abc', 'primary')).not.toContain('account=')
  })

  it('carries ?account= for a connected account, since it is composer <img src>', async () => {
    const { stagedAttachmentUrl, API_BASE } = await import('./api.js')

    const url = stagedAttachmentUrl('abc', 'acct-2')

    expect(url).toBe(`${API_BASE}/api/Mail/Attachments/abc/content?account=acct-2`)
  })
})

describe('requestBlob', () => {
  function mockBlobFetch({ status = 200, disposition = null, ok = true, text = '' }:
    { status?: number; disposition?: string | null; ok?: boolean; text?: string } = {}) {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      status,
      ok,
      statusText: text,
      headers: { get: (h: string) => (h.toLowerCase() === 'content-disposition' ? disposition : null) },
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

    await api.sendMessage({ to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [], priority: 'normal' })

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Mail/Send')
    expect(options.method).toBe('POST')
    expect(sentJson(options)).toHaveProperty(['to', 0], 'a@b.c')
  })
})

describe('saveDraft', () => {
  it('posts the payload to /api/Mail/Drafts', async () => {
    mockFetch(200, { json: { uid: 7, folderPath: 'Drafts' } })
    const { api } = await import('./api.js')

    const result = await api.saveDraft({ to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [], priority: 'normal' })

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Mail/Drafts')
    expect(options.method).toBe('POST')
    expect(sentJson(options)).toHaveProperty(['to', 0], 'a@b.c')
    expect(result).toEqual({ uid: 7, folderPath: 'Drafts' })
  })
})

describe('openDraft', () => {
  it('posts { folder, uid } to /api/Mail/Drafts/Open', async () => {
    mockFetch(200, { json: { to: [], cc: [], bcc: [], subject: '', htmlBody: '', attachments: [], references: [] } })
    const { api } = await import('./api.js')

    await api.openDraft('Drafts', 7)

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Mail/Drafts/Open')
    expect(options.method).toBe('POST')
    expect(sentJson(options)).toEqual({ folder: 'Drafts', uid: 7 })
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
  class FakeXhr {
    upload: { onprogress: (event: { lengthComputable: boolean; loaded: number; total: number }) => void } =
      { onprogress: () => {} }
    method = ''
    url = ''
    headers: Record<string, string> | undefined
    status = 0
    statusText = ''
    responseText = ''
    withCredentials = false
    onload = () => {}
    onerror = () => {}
    onabort = () => {}
    open(method: string, url: string) { this.method = method; this.url = url }
    setRequestHeader(name: string, value: string) { this.headers = { ...this.headers, [name]: value } }
    send(form: FormData) { sent = { xhr: this, form } }
    abort() {}
  }
  let sent: { xhr: FakeXhr; form: FormData } | undefined
  const xhr = () => {
    if (!sent) throw new Error('nothing was sent')
    return sent.xhr
  }
  beforeEach(() => { sent = undefined; vi.stubGlobal('XMLHttpRequest', FakeXhr) })
  afterEach(() => { vi.unstubAllGlobals() })

  it('resolves with the parsed body on 200', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    xhr().status = 200
    xhr().responseText = '{"id":"i","fileName":"a.txt","size":1,"contentType":"text/plain"}'
    xhr().onload()
    await expect(done).resolves.toEqual({ id: 'i', fileName: 'a.txt', size: 1, contentType: 'text/plain' })
    expect(xhr().withCredentials).toBe(true)
    expect(xhr().url).toContain('/api/Mail/Attachments')
  })

  it('rejects with the enveloppe message and reports progress', async () => {
    const { uploadAttachment } = await import('./api.js')
    const onProgress = vi.fn()
    const done = uploadAttachment(new File(['x'], 'a.txt'), { onProgress })
    xhr().upload.onprogress({ lengthComputable: true, loaded: 1, total: 2 })
    xhr().status = 400
    xhr().responseText = '{"message":"The attachment exceeds the 25 MB limit"}'
    xhr().onload()
    await expect(done).rejects.toThrow('The attachment exceeds the 25 MB limit')
    expect(onProgress).toHaveBeenCalledWith(0.5)
  })

  it('rejects with the code from the error envelope on a 401', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    xhr().status = 401
    xhr().responseText = '{"message":"credentials_unavailable"}'
    xhr().onload()
    await expect(done).rejects.toMatchObject({ status: 401, code: 'credentials_unavailable' })
  })

  it('aborts the xhr and rejects when the signal fires mid-flight', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    const done = uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })
    const abortSpy = vi.spyOn(xhr(), 'abort')
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
    xhr().status = 200
    xhr().responseText = '{"id":"i"}'
    xhr().onload()
    await done
    expect(removeSpy).toHaveBeenCalledWith('abort', expect.any(Function))
  })

  it('rejects with an ApiError when a 2xx body is not JSON', async () => {
    const { uploadAttachment, ApiError } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    xhr().status = 200
    xhr().responseText = '<html>proxy page</html>'
    expect(() => xhr().onload()).not.toThrow()
    await expect(done).rejects.toBeInstanceOf(ApiError)
    await expect(done).rejects.toMatchObject({ status: 200 })
  })

  it('rejects as aborted when the xhr itself is aborted', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    const removeSpy = vi.spyOn(controller.signal, 'removeEventListener')
    const done = uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })
    xhr().onabort()
    await expect(done).rejects.toMatchObject({ message: 'Aborted', status: 0 })
    expect(removeSpy).toHaveBeenCalledWith('abort', expect.any(Function))
  })

  it('rejects immediately when the signal is already aborted', async () => {
    const { uploadAttachment } = await import('./api.js')
    const controller = new AbortController()
    controller.abort()
    await expect(uploadAttachment(new File(['x'], 'a.txt'), { signal: controller.signal })).rejects.toThrow('Aborted')
    expect(sent).toBeUndefined()
  })

  it('sets the X-Account-Id header for a connected account', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), { accountId: 'acct-2' })
    xhr().status = 200
    xhr().responseText = '{"id":"i"}'
    xhr().onload()
    await done
    expect(xhr().headers).toEqual({ 'X-Account-Id': 'acct-2' })
  })

  it('sets no header for the primary account', async () => {
    const { uploadAttachment } = await import('./api.js')
    const done = uploadAttachment(new File(['x'], 'a.txt'), { accountId: 'primary' })
    xhr().status = 200
    xhr().responseText = '{"id":"i"}'
    xhr().onload()
    await done
    expect(xhr().headers).toBeUndefined()
  })
})

describe('trusted senders', () => {
  it('getTrustedSenders reads the list', async () => {
    mockFetch(200, { json: ['news@example.com'] })
    const { api } = await import('./api.js')

    await expect(api.getTrustedSenders()).resolves.toEqual(['news@example.com'])

    const [url, options] = fetchCall()
    expect(url).toContain('/api/TrustedSenders')
    expect(options.method).toBe('GET')
  })

  it('trustSender posts the address', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.trustSender('news@example.com')

    const [, options] = fetchCall()
    expect(options.method).toBe('POST')
    expect(sentJson(options)).toEqual({ address: 'news@example.com' })
  })

  // A '+' is a legal local-part character and decodes to a space, so an unencoded query string
  // would untrust a different address than the one asked for.
  it('untrustSender encodes the address into the query string', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.untrustSender('news+weekly@example.com')

    const [url, options] = fetchCall()
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
    mockFetch(200, { json: { 'app.name': 'Scotty mail' } })
    const { api } = await import('./api.js')

    await expect(api.getAppSettings()).resolves.toEqual({ 'app.name': 'Scotty mail' })
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({ method: 'GET' }))
  })

  it('setAppSetting sends the key and the value in the body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setAppSetting('app.name', 'Scotty mail')

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ key: 'app.name', value: 'Scotty mail' }),
      }))
  })
})

describe('contact batches', () => {
  it('sends the batch in the body of a DELETE', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.deleteContacts(['a', 'b'])

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Contacts')
    expect(options.method).toBe('DELETE')
    expect(sentJson(options)).toEqual({ ids: ['a', 'b'] })
  })

  it('sends the batch and the flag when starring in bulk', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setContactsFavorite(['a'], true)

    const [url, options] = fetchCall()
    expect(url).toContain('/api/Contacts/Favorite')
    expect(options.method).toBe('PUT')
    expect(sentJson(options)).toEqual({ ids: ['a'], isFavorite: true })
  })
})
