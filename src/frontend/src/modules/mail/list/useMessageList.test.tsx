import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useMessageList } from './useMessageList'

const mocks = vi.hoisted(() => ({ getMailMessages: vi.fn(), getPreferences: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function pageOf(uids: number[], total: number) {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}

describe('useMessageList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('pages when a size is chosen', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 25))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.paging).not.toBeNull())
    expect(result.current.streaming).toBeNull()
    expect(result.current.paging).toMatchObject({ page: 0, lastPage: 2 })
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 10, expect.anything())
  })

  it('streams on "all", and asks for blocks of 100', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 2))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.streaming).not.toBeNull())
    expect(result.current.paging).toBeNull()
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100, expect.anything())
  })

  // The inactive mode must not fire a second request for the same folder.
  it('runs exactly one of the two queries', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.messages).toHaveLength(1))
    expect(mocks.getMailMessages).toHaveBeenCalledTimes(1)
  })

  it('waits for the preferences before claiming a mode', () => {
    mocks.getPreferences.mockReturnValue(new Promise(() => {}))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.paging).toBeNull()
    expect(result.current.streaming).toBeNull()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('reports the total from the freshest block', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 3812))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.total).toBe(3812))
  })

  it('reports isError only when the first block fails', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockRejectedValue(new Error('boom'))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(result.current.messages).toEqual([])
    expect(result.current.streaming?.loadMoreFailed).toBe(false)
  })

  it('keeps loaded rows and offers retry when a later block fails', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    const fullBlock = pageOf(Array.from({ length: 100 }, (_, i) => i + 1), 250)
    mocks.getMailMessages.mockResolvedValueOnce(fullBlock).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.messages).toHaveLength(100))
    expect(result.current.streaming?.hasMore).toBe(true)

    act(() => { result.current.streaming?.loadMore() })

    await waitFor(() => expect(result.current.streaming?.loadMoreFailed).toBe(true))
    expect(result.current.messages).toHaveLength(100)
    expect(result.current.isError).toBe(false)
  })

  // A message arriving mid-scroll shifts every row by one, so the last row of a block comes
  // back as the first row of the next: the reader must not see it twice.
  it('shows a uid repeated across blocks once, in its first position', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    const first = pageOf(Array.from({ length: 100 }, (_, i) => i + 1), 250)
    mocks.getMailMessages
      .mockResolvedValueOnce(first)
      .mockResolvedValueOnce(pageOf([100, 101, 102], 250))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })
    await waitFor(() => expect(result.current.messages).toHaveLength(100))

    act(() => { result.current.streaming?.loadMore() })

    await waitFor(() => expect(result.current.messages).toHaveLength(102))
    const uids = result.current.messages.map(m => m.uid)
    expect(uids.filter(uid => uid === 100)).toHaveLength(1)
    expect(uids.indexOf(100)).toBe(99)
  })

  it('does not query a new folder at the previous folder\'s page', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 25))

    const { result, rerender } = renderHook(
      ({ folder }: { folder: string }) => useMessageList(folder),
      { wrapper, initialProps: { folder: 'INBOX' } },
    )

    await waitFor(() => expect(result.current.paging).not.toBeNull())
    act(() => { result.current.paging?.onSelect(1) })
    await waitFor(() => expect(result.current.paging?.page).toBe(1))

    rerender({ folder: 'Archive' })

    await waitFor(() => expect(result.current.paging?.page).toBe(0))
    expect(mocks.getMailMessages).not.toHaveBeenCalledWith('Archive', 1, 10, expect.anything())
  })
})
