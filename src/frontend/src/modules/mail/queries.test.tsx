import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { POLL_INTERVAL, mailKeys, useCreateFolder, useFolders, useMessage, useMessages, useMessageStream } from './queries'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
  createMailFolder: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: {
    getMailFolders: mocks.getMailFolders,
    getMailMessages: mocks.getMailMessages,
    getMailMessage: mocks.getMailMessage,
    createMailFolder: mocks.createMailFolder,
  },
}))

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary', email: 'alice@weesky.be' } }),
}))

function pageOf(uids: number[], total: number) {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}

function createWrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return {
    client,
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}

describe('mailKeys', () => {
  it('scopes every key by account id', () => {
    expect(mailKeys.folders('primary')).toEqual(['mail', 'primary', 'folders'])
    expect(mailKeys.messages('primary', 'INBOX', 0, 30)).toEqual(['mail', 'primary', 'messages', 'INBOX', 0, 30])
    expect(mailKeys.message('primary', 'INBOX', 42)).toEqual(['mail', 'primary', 'message', 'INBOX', 42])
  })

  // A stream caches a sequence of pages, a page caches one: sharing a key is a type error that
  // would only show at runtime.
  it('keeps the stream key apart from the page key', () => {
    expect(mailKeys.messageStream('primary', 'INBOX', 100))
      .toEqual(['mail', 'primary', 'messageStream', 'INBOX', 100])
    expect(mailKeys.messageStream('primary', 'INBOX', 100))
      .not.toEqual(mailKeys.messages('primary', 'INBOX', 0, 100))
  })

  it('gives different accounts different keys', () => {
    expect(mailKeys.folders('primary')).not.toEqual(mailKeys.folders('linked-1'))
  })

  // A page fetched at 30 per page is not the same page at 100: without the size in the key,
  // changing it would serve rows computed under the old one.
  it('gives different page sizes different keys', () => {
    expect(mailKeys.messages('primary', 'INBOX', 0, 30))
      .not.toEqual(mailKeys.messages('primary', 'INBOX', 0, 100))
  })
})

describe('useFolders', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads the folder tree', async () => {
    mocks.getMailFolders.mockResolvedValue([{ path: 'INBOX', name: 'INBOX', children: [] }])
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useFolders(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.[0].path).toBe('INBOX')
  })

  it('surfaces a failure', async () => {
    mocks.getMailFolders.mockRejectedValue(new Error('boom'))
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useFolders(), { wrapper })

    await waitFor(() => expect(result.current.isError).toBe(true))
  })

  // The poll: one LIST+STATUS a minute. TanStack pauses it while the tab is unfocused and
  // the app-wide refetchOnWindowFocus fires the catch-up tick on return.
  it('polls the folders every minute', async () => {
    vi.useFakeTimers()
    try {
      mocks.getMailFolders.mockResolvedValue([])
      const { wrapper } = createWrapper()
      renderHook(() => useFolders(), { wrapper })

      await act(async () => { await vi.advanceTimersByTimeAsync(0) })
      expect(mocks.getMailFolders).toHaveBeenCalledTimes(1)

      await act(async () => { await vi.advanceTimersByTimeAsync(POLL_INTERVAL) })
      expect(mocks.getMailFolders).toHaveBeenCalledTimes(2)
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('useMessages', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not fetch until a folder is selected', () => {
    const { wrapper } = createWrapper()

    renderHook(() => useMessages(null, 0, 30), { wrapper })

    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('requests the selected folder and page', async () => {
    mocks.getMailMessages.mockResolvedValue({ folderPath: 'INBOX', messages: [], total: 0, page: 1, pageSize: 50 })
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessages('INBOX', 1, 30), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 30, expect.anything())
  })
})

describe('useMessage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not fetch without a uid', () => {
    const { wrapper } = createWrapper()

    renderHook(() => useMessage('INBOX', null), { wrapper })

    expect(mocks.getMailMessage).not.toHaveBeenCalled()
  })

  it('fetches once both folder and uid are known', async () => {
    mocks.getMailMessage.mockResolvedValue({ uid: 42, subject: 'Hello' })
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessage('INBOX', 42), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.subject).toBe('Hello')
  })
})

describe('useMessageStream', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => focusManager.setFocused(undefined))

  it('asks for block 0 first', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 2))
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessageStream('INBOX', 100, true), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100, expect.anything())
  })

  it('issues no request when it is not the active mode', () => {
    const { wrapper } = createWrapper()

    renderHook(() => useMessageStream('INBOX', 100, false), { wrapper })

    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('fetches the next block by index', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 500))
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessageStream('INBOX', 2, true), { wrapper })
    await waitFor(() => expect(result.current.hasNextPage).toBe(true))
    result.current.fetchNextPage()

    await waitFor(() =>
      expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 2, expect.anything()))
  })

  it('reports no next block after a short one', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessageStream('INBOX', 2, true), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(result.current.hasNextPage).toBe(false)
  })

  // App.tsx turns focus refetching on app-wide; here it would refetch *every* loaded block, so
  // forty blocks would be forty IMAP connections and forty full folder sorts.
  it('does not refetch its blocks when the window regains focus', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 2))
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessageStream('INBOX', 100, true), { wrapper })
    await waitFor(() => expect(result.current.data).toBeDefined())

    await act(async () => {
      focusManager.setFocused(false)
      focusManager.setFocused(true)
      await Promise.resolve()
    })

    expect(mocks.getMailMessages).toHaveBeenCalledTimes(1)
  })
})

describe('folder mutations', () => {
  beforeEach(() => vi.clearAllMocks())

  it('invalidates the folder tree on success', async () => {
    mocks.createMailFolder.mockResolvedValue('INBOX/Projects')
    const { client, wrapper } = createWrapper()
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useCreateFolder(), { wrapper })
    await result.current.mutateAsync({ parentPath: 'INBOX', name: 'Projects' })

    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.folders('primary') }))
  })

  it('does not invalidate when the mutation fails', async () => {
    mocks.createMailFolder.mockRejectedValue(new Error('nope'))
    const { client, wrapper } = createWrapper()
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useCreateFolder(), { wrapper })
    await expect(result.current.mutateAsync({ parentPath: '', name: 'x' })).rejects.toThrow('nope')

    expect(invalidate).not.toHaveBeenCalled()
  })
})
