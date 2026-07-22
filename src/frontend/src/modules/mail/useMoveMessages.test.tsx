import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type { MailFolderNode, MailFolderPage, MailMessageSummary } from './api/mailTypes'
import { mailKeys, useDeleteMessages, useMoveMessages } from './queries'
import { settle } from '../../test-utils'

const mocks = vi.hoisted(() => ({
  moveMessages: vi.fn(), copyMessages: vi.fn(), deleteMessages: vi.fn(),
}))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  ...over,
})

const pageOf = (folderPath: string, messages: MailMessageSummary[]): MailFolderPage => ({
  folderPath, uidValidity: 1, total: 20, page: 0, pageSize: 50, messages,
})

const streamOf = (folderPath: string, blocks: MailMessageSummary[][]): InfiniteData<MailFolderPage> => ({
  pages: blocks.map(block => pageOf(folderPath, block)),
  pageParams: blocks.map((_, index) => index),
})

const node = (path: string, total: number, unread: number): MailFolderNode => ({
  path, name: path, specialUse: null, selectable: true, subscribed: true,
  total, unread, uidValidity: 1, uidNext: 100, highestModSeq: null, children: [],
})

const sourcePagesKey = mailKeys.messages('primary', 'INBOX', 0, 50)
const sourceStreamKey = mailKeys.messageStream('primary', 'INBOX', 100)
const targetPagesKey = mailKeys.messages('primary', 'Archive', 0, 50)
const targetStreamKey = mailKeys.messageStream('primary', 'Archive', 100)
const foldersKey = mailKeys.folders('primary')

const sourcePage = () => client.getQueryData<MailFolderPage>(sourcePagesKey)
const sourceStream = () => client.getQueryData<InfiniteData<MailFolderPage>>(sourceStreamKey)
const targetPage = () => client.getQueryData<MailFolderPage>(targetPagesKey)
const targetStream = () => client.getQueryData<InfiniteData<MailFolderPage>>(targetStreamKey)
const folder = (path: string) =>
  client.getQueryData<MailFolderNode[]>(foldersKey)!.find(entry => entry.path === path)!

/** uid 1 sits in the source page AND in source stream block 0 — the dedup case. */
function seed() {
  const page = pageOf('INBOX', [summary(1), summary(2, { seen: true }), summary(3)])
  const stream = streamOf('INBOX', [[summary(1), summary(4)], [summary(5)]])
  const target = pageOf('Archive', [summary(9)])
  const targetBlocks = streamOf('Archive', [[summary(9)]])
  const tree = [node('INBOX', 20, 5), node('Archive', 3, 1)]

  client.setQueryData(sourcePagesKey, page)
  client.setQueryData(sourceStreamKey, stream)
  client.setQueryData(targetPagesKey, target)
  client.setQueryData(targetStreamKey, targetBlocks)
  client.setQueryData(foldersKey, tree)
  return { page, stream, target, targetBlocks, tree }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

const uidsOf = (messages: MailMessageSummary[]) => messages.map(message => message.uid)

describe('useMoveMessages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
  })

  it('takes the rows out of every source cache and drops the target caches', async () => {
    seed()
    const pending = deferred<void>()
    mocks.moveMessages.mockReturnValue(pending.promise)

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1, 2], targetFolderPath: 'Archive', copy: false,
      })
    })

    // Patched while the request is still in flight — that is what "optimistic" means.
    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(uidsOf(sourceStream()!.pages[0].messages)).toEqual([4])
    expect(uidsOf(sourceStream()!.pages[1].messages)).toEqual([5])
    // uid 1 unseen, uid 2 seen: two off the total, one off the badge.
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
    // Removed, never invalidated: a refetch here would cost one IMAP connection per block.
    expect(targetPage()).toBeUndefined()
    expect(targetStream()).toBeUndefined()
    expect(folder('Archive').total).toBe(5)
    expect(folder('Archive').unread).toBe(2)
    expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [1, 2], 'Archive')

    await act(async () => { pending.resolve() })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(folder('INBOX').total).toBe(18)
  })

  it('counts a uid held by two source caches only once', async () => {
    seed()
    mocks.moveMessages.mockResolvedValue(undefined)

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // Both copies gone, but the counters move by one, not two.
    expect(uidsOf(sourcePage()!.messages)).toEqual([2, 3])
    expect(uidsOf(sourceStream()!.pages[0].messages)).toEqual([4])
    expect(folder('INBOX').total).toBe(19)
    expect(folder('INBOX').unread).toBe(4)
    expect(folder('Archive').total).toBe(4)
    expect(folder('Archive').unread).toBe(2)
  })

  it('counts uids split across a cached page and a stream block as two, not one', async () => {
    // uid 1 lives only in the page, uid 2 only in stream block 1 — two different caches,
    // neither uid overlapping the other's cache, unlike the dedup case above.
    const page = pageOf('INBOX', [summary(1), summary(3)])
    const stream = streamOf('INBOX', [[summary(4)], [summary(2, { seen: true })]])
    const tree = [node('INBOX', 20, 5), node('Archive', 3, 1)]
    client.setQueryData(sourcePagesKey, page)
    client.setQueryData(sourceStreamKey, stream)
    client.setQueryData(foldersKey, tree)
    mocks.moveMessages.mockResolvedValue(undefined)

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1, 2], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(uidsOf(sourceStream()!.pages[1].messages)).toEqual([])
    // uid 1 unread, uid 2 already seen: two off the total, one off the badge.
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
  })

  it('leaves the source alone on a copy and raises the target total only', async () => {
    const seeded = seed()
    mocks.copyMessages.mockResolvedValue(undefined)
    const writes = vi.spyOn(client, 'setQueryData')

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1, 2], targetFolderPath: 'Archive', copy: true,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    expect(sourcePage()).toBe(seeded.page)
    expect(sourceStream()).toBe(seeded.stream)
    // Identity survives a deep-equal rewrite via structural sharing, so prove no write was even
    // aimed at the source caches.
    const written = writes.mock.calls.map(([key]) => JSON.stringify(key))
    expect(written).not.toContain(JSON.stringify(sourcePagesKey))
    expect(written).not.toContain(JSON.stringify(sourceStreamKey))

    expect(targetPage()).toBeUndefined()
    expect(targetStream()).toBeUndefined()
    expect(folder('INBOX').total).toBe(20)
    expect(folder('INBOX').unread).toBe(5)
    // Unread is unknowable without a removal, so the badge waits for the poll.
    expect(folder('Archive').total).toBe(5)
    expect(folder('Archive').unread).toBe(1)
    expect(mocks.copyMessages).toHaveBeenCalledWith('INBOX', [1, 2], 'Archive')
  })

  it('restores every cache, dropped target included, when the move fails', async () => {
    const seeded = seed()
    const pending = deferred<void>()
    mocks.moveMessages.mockReturnValue(pending.promise)
    const onError = vi.fn()

    const { result } = renderHook(() => useMoveMessages(onError), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1, 2], targetFolderPath: 'Archive', copy: false,
      })
    })

    // Patched first, so the restoration below is a real round trip and not a no-op.
    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(targetPage()).toBeUndefined()

    await act(async () => { pending.reject(new Error('boom')) })
    await waitFor(() => expect(result.current.isError).toBe(true))

    // Deep, not identity: TanStack's structural sharing rebuilds the container it writes.
    expect(sourcePage()).toStrictEqual(seeded.page)
    expect(sourceStream()).toStrictEqual(seeded.stream)
    expect(targetPage()).toStrictEqual(seeded.target)
    expect(targetStream()).toStrictEqual(seeded.targetBlocks)
    expect(client.getQueryData<MailFolderNode[]>(foldersKey)).toStrictEqual(seeded.tree)
    expect(onError).toHaveBeenCalledWith('Could not move the message')
  })

  it('reports the copy failure in its own words', async () => {
    seed()
    mocks.copyMessages.mockRejectedValue(new Error('boom'))
    const onError = vi.fn()

    const { result } = renderHook(() => useMoveMessages(onError), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: true,
      })
    })
    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(onError).toHaveBeenCalledWith('Could not copy the message')
  })

  it('never invalidates a stream key', async () => {
    seed()
    mocks.moveMessages.mockResolvedValue(undefined)
    const spy = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    expect(spy).not.toHaveBeenCalled()
  })
})

describe('useDeleteMessages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
  })

  it('empties the source caches and touches no other folder', async () => {
    const seeded = seed()
    mocks.deleteMessages.mockResolvedValue(undefined)

    const { result } = renderHook(() => useDeleteMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1, 2] })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(uidsOf(sourceStream()!.pages[0].messages)).toEqual([4])
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
    // No target exists for a delete: the other folder's caches and counters stay as seeded.
    expect(targetPage()).toBe(seeded.target)
    expect(targetStream()).toBe(seeded.targetBlocks)
    expect(folder('Archive').total).toBe(3)
    expect(folder('Archive').unread).toBe(1)
    expect(mocks.deleteMessages).toHaveBeenCalledWith('INBOX', [1, 2])
  })

  it('rolls the source caches back and says so', async () => {
    const seeded = seed()
    mocks.deleteMessages.mockRejectedValue(new Error('boom'))
    const onError = vi.fn()

    const { result } = renderHook(() => useDeleteMessages(onError), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1, 2] })
    })
    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(sourcePage()).toStrictEqual(seeded.page)
    expect(sourceStream()).toStrictEqual(seeded.stream)
    expect(client.getQueryData<MailFolderNode[]>(foldersKey)).toStrictEqual(seeded.tree)
    expect(onError).toHaveBeenCalledWith('Could not delete the message')
  })
})
