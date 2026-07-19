import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { mailKeys, useCreateFolder, useFolders, useMessage, useMessages } from './queries'

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
    expect(mailKeys.messages('primary', 'INBOX', 0)).toEqual(['mail', 'primary', 'messages', 'INBOX', 0])
    expect(mailKeys.message('primary', 'INBOX', 42)).toEqual(['mail', 'primary', 'message', 'INBOX', 42])
  })

  it('gives different accounts different keys', () => {
    expect(mailKeys.folders('primary')).not.toEqual(mailKeys.folders('linked-1'))
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
})

describe('useMessages', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not fetch until a folder is selected', () => {
    const { wrapper } = createWrapper()

    renderHook(() => useMessages(null, 0), { wrapper })

    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('requests the selected folder and page', async () => {
    mocks.getMailMessages.mockResolvedValue({ folderPath: 'INBOX', messages: [], total: 0, page: 1, pageSize: 50 })
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessages('INBOX', 1), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 50, expect.anything())
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
