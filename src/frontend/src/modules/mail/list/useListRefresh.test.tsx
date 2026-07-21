import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type { MailFolderNode, MailFolderPage } from '../api/mailTypes'
import { mailKeys } from '../queries'
import { useListRefresh } from './useListRefresh'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getPreferences: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function inbox(overrides: Partial<MailFolderNode> = {}): MailFolderNode {
  return {
    path: 'INBOX', name: 'INBOX', specialUse: null, selectable: true, subscribed: true,
    total: 5, unread: 2, uidValidity: 100, uidNext: 10, highestModSeq: 40, children: [],
    ...overrides,
  }
}

function pageOf(uids: number[]): MailFolderPage {
  return {
    folderPath: 'INBOX', uidValidity: 100, total: 5, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}

/** Renders the hook, waits for the baseline snapshot, then applies the next poll answer. */
async function renderWithBaseline(pageSize: string, first: MailFolderNode) {
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': pageSize, 'mail.showPreview': 'true' })
  mocks.getMailFolders.mockResolvedValue([first])

  const rendered = renderHook(() => useListRefresh('INBOX'), { wrapper })
  await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
  await waitFor(() =>
    expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

  return {
    ...rendered,
    tick: (next: MailFolderNode) =>
      act(() => { client.setQueryData(mailKeys.folders('primary'), [next]) }),
  }
}

describe('useListRefresh', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('does nothing on the baseline observation', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    await renderWithBaseline('10', inbox())

    expect(spy).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('refreshes the paged list when the folder moved', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox({ uidNext: 11, total: 6, unread: 3 }))

    await waitFor(() => expect(spy).toHaveBeenCalledWith(
      { queryKey: ['mail', 'primary', 'messages', 'INBOX'] }))
  })

  it('stays quiet when nothing moved', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox())

    expect(spy).not.toHaveBeenCalled()
  })

  // THE lock of this feature: three loaded blocks, a delta, and exactly ONE request goes
  // out — block 0. invalidateQueries on the stream would refetch every block.
  it('refreshes one block in streaming mode, never the whole stream', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('all', inbox())

    const key = mailKeys.messageStream('primary', 'INBOX', 100)
    client.setQueryData<InfiniteData<MailFolderPage>>(key, {
      pages: [pageOf([30, 29]), pageOf([28, 27]), pageOf([26, 25])],
      pageParams: [0, 1, 2],
    })
    mocks.getMailMessages.mockResolvedValue(pageOf([31, 30]))

    await tick(inbox({ uidNext: 12 }))

    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalledTimes(1))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100)
    expect(spy).not.toHaveBeenCalled()

    const data = client.getQueryData<InfiniteData<MailFolderPage>>(key)!
    expect(data.pages).toHaveLength(3)
    expect(data.pages[0].messages.map(m => m.uid)).toEqual([31, 30])
    expect(data.pages[1].messages.map(m => m.uid)).toEqual([28, 27])
  })

  it('resets the folder outright when uidValidity broke', async () => {
    const reset = vi.spyOn(client, 'resetQueries')
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox({ uidValidity: 101 }))

    await waitFor(() => expect(reset).toHaveBeenCalledTimes(1))
    expect(invalidate).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('swallows a failed block-0 refresh in silence', async () => {
    const { tick } = await renderWithBaseline('all', inbox())
    mocks.getMailMessages.mockRejectedValue(new Error('refused'))

    await tick(inbox({ uidNext: 12 }))
    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalled())
    // Nothing to assert beyond "no crash": the list keeps whatever it had.
  })

  it('re-baselines when the displayed folder changes', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailFolders.mockResolvedValue([inbox(), inbox({ path: 'Archive', name: 'Archive', uidNext: 50 })])

    const { rerender } = renderHook(({ path }) => useListRefresh(path), {
      wrapper, initialProps: { path: 'INBOX' as string | null },
    })
    await waitFor(() =>
      expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

    rerender({ path: 'Archive' })
    // Archive's first observation is a baseline, not a change against INBOX's snapshot.
    expect(spy).not.toHaveBeenCalled()
  })
})
