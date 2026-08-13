import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useMessageList } from './useMessageList'

const mocks = vi.hoisted(() => ({ getMailMessages: vi.fn(), getPreferences: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' }),
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

/** A grouped page: `threads` carries the conversations, and `totalThreads` is what the pager
    pages — `total` keeps counting messages. */
function groupedPageOf(threads: number[][], total: number, totalThreads: number) {
  return {
    ...pageOf([], total),
    threads: threads.map(uids => ({ messages: pageOf(uids, total).messages })),
    totalThreads,
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

  // The pager pages conversations in grouped mode: pacing it on the message count would offer
  // pages the folder cannot fill. 300 messages in 61 threads at 30 a page — the two units give
  // 9 and 2, so the assertion fails if lastPage ever goes back to counting messages.
  it('exposes thread groups and pages on totalThreads when the page is grouped', async () => {
    mocks.getPreferences.mockResolvedValue({
      'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.groupConversations': 'true',
    })
    mocks.getMailMessages.mockResolvedValue(groupedPageOf([[30, 10]], 300, 61))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.groups).toHaveLength(1))
    expect(result.current.groups[0].key).toBe(10)
    expect(result.current.groups[0].messages.map(m => m.uid)).toEqual([30, 10])
    expect(result.current.messages.map(m => m.uid)).toEqual([30, 10])
    expect(result.current.paging?.lastPage).toBe(2)
    // The heading still counts messages: only the pager moved to threads.
    expect(result.current.total).toBe(300)
    expect(mocks.getMailMessages).toHaveBeenCalledWith(
      'INBOX', 0, 30, expect.objectContaining({ grouped: true }))
  })

  // One shape for both modes: a flat row is a conversation of one, so Task 9's row never learns
  // which mode it is drawing.
  it('wraps every message as its own group in flat mode', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 25))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.groups).toHaveLength(2))
    expect(result.current.groups.map(g => g.key)).toEqual([1, 2])
    expect(result.current.messages.map(m => m.uid)).toEqual([1, 2])
    expect(mocks.getMailMessages).toHaveBeenCalledWith(
      'INBOX', 0, 10, expect.objectContaining({ grouped: false }))
  })

  // The streamed counterpart of the dedupe: an arrival shifts the thread window, so a block
  // repeats the previous block's last conversation.
  it('shows a thread repeated across blocks once', async () => {
    mocks.getPreferences.mockResolvedValue({
      'mail.pageSize': 'all', 'mail.showPreview': 'true', 'mail.groupConversations': 'true',
    })
    const first = groupedPageOf(Array.from({ length: 100 }, (_, i) => [i + 1]), 250, 250)
    mocks.getMailMessages
      .mockResolvedValueOnce(first)
      .mockResolvedValueOnce(groupedPageOf([[100], [200, 101]], 250, 250))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })
    await waitFor(() => expect(result.current.groups).toHaveLength(100))

    act(() => { result.current.streaming?.loadMore() })

    await waitFor(() => expect(result.current.groups).toHaveLength(101))
    expect(result.current.groups[100].messages.map(m => m.uid)).toEqual([200, 101])
    expect(result.current.messages.map(m => m.uid).filter(uid => uid === 100)).toHaveLength(1)
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
