import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { type InfiniteData, type QueryClient } from '@tanstack/react-query'
import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchPage, MailSearchResult,
} from './api/mailTypes'
import { mailKeys, useSearchMessages, useSetFlags } from './queries'
import { createTestQueryClient, settle, waitFor, withQueryClient } from '../../test-utils'
import { folderNodeOf, pageOf, summaryOf } from './mailTestHarness'

const mocks = vi.hoisted(() => ({ setMessageFlags: vi.fn(), searchMessages: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => import('../../test-auth'))

let client: QueryClient
let wrapper: ReturnType<typeof withQueryClient>

/** A grouped page as the backend sends one: the rows live in `threads`, `messages` stays empty. */
const groupedPageOf = (groups: MailMessageSummary[][]): MailFolderPage => ({
  ...pageOf([]),
  threads: groups.map(messages => ({ messages })), totalThreads: groups.length,
})

const node = (path: string, unread: number): MailFolderNode =>
  folderNodeOf({ path, total: 10, unread, uidNext: 100 })

const searchCriteria = { folderPath: '', allFolders: true, quick: 'x' }
const searchKey = mailKeys.search('primary', searchCriteria, 0, 50)

const searchRow = (uid: number, folderPath: string, over: Partial<MailSearchResult> = {}): MailSearchResult =>
  ({ ...summaryOf(uid, over), folderPath, uidValidity: 1 })

const searchPageOf = (results: MailSearchResult[]): MailSearchPage =>
  ({ total: results.length, page: 0, pageSize: 50, results })

const pagesKey = mailKeys.messages('primary', 'INBOX', 0, 50)
const streamKey = mailKeys.messageStream('primary', 'INBOX', 100)
const foldersKey = mailKeys.folders('primary')
const searchIn = () => client.getQueryData<MailSearchPage>(searchKey)

const pageIn = () => client.getQueryData<MailFolderPage>(pagesKey)
const streamIn = () => client.getQueryData<InfiniteData<MailFolderPage>>(streamKey)
const treeIn = () => client.getQueryData<MailFolderNode[]>(foldersKey)

function seed(pageMessages: MailMessageSummary[], ...streamBlocks: MailMessageSummary[][]) {
  const page = pageOf(pageMessages)
  const stream: InfiniteData<MailFolderPage> = {
    pages: streamBlocks.map(block => pageOf(block)), pageParams: streamBlocks.map((_, index) => index),
  }
  const tree = [node('INBOX', 5)]
  client.setQueryData(pagesKey, page)
  client.setQueryData(streamKey, stream)
  client.setQueryData(foldersKey, tree)
  return { page, stream, tree }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

describe('useSetFlags', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = createTestQueryClient({ mutations: { retry: false } })
    wrapper = withQueryClient(client)
  })

  it('patches pages, stream blocks and the folder unread count optimistically', async () => {
    const seeded = seed([summaryOf(1), summaryOf(2, { seen: true })], [summaryOf(1), summaryOf(3)])
    const pending = deferred<void>()
    mocks.setMessageFlags.mockReturnValue(pending.promise)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })

    // Patched while the request is still in flight — that is what "optimistic" means.
    expect(pageIn()!.messages[0]!.seen).toBe(true)
    expect(streamIn()!.pages[0]!.messages[0]!.seen).toBe(true)
    expect(treeIn()![0]!.unread).toBe(4)
    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [1], 'seen', true, { accountId: 'primary' })
    // Copied, never mutated in place: the snapshot is the rollback.
    expect(seeded.page.messages[0]!.seen).toBe(false)

    await act(async () => { pending.resolve() })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(pageIn()!.messages[0]!.seen).toBe(true)
    expect(treeIn()![0]!.unread).toBe(4)
  })

  it('rolls all three caches back when the request fails', async () => {
    const seeded = seed([summaryOf(1)], [summaryOf(1)])
    const pending = deferred<void>()
    mocks.setMessageFlags.mockReturnValue(pending.promise)
    const onError = vi.fn()

    const { result } = renderHook(() => useSetFlags(onError), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })

    // Patched first, so the restoration below is a real round trip and not a no-op.
    expect(pageIn()!.messages[0]!.seen).toBe(true)
    expect(streamIn()!.pages[0]!.messages[0]!.seen).toBe(true)
    expect(treeIn()![0]!.unread).toBe(4)

    await act(async () => { pending.reject(new Error('boom')) })
    await waitFor(() => expect(result.current.isError).toBe(true))

    // Deep, not identity: TanStack's structural sharing rebuilds the container it writes.
    expect(pageIn()).toStrictEqual(seeded.page)
    expect(streamIn()).toStrictEqual(seeded.stream)
    expect(treeIn()).toStrictEqual(seeded.tree)
    expect(onError).toHaveBeenCalledWith('Could not update the message')
  })

  it('leaves the folder count alone on a flagged mutation', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'flagged', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(pageIn()!.messages[0]!.flagged).toBe(true)
    expect(streamIn()!.pages[0]!.messages[0]!.flagged).toBe(true)
    expect(treeIn()![0]!.unread).toBe(5)
  })

  it('leaves the folder count alone when no cache holds the uid', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    mocks.setMessageFlags.mockResolvedValue(undefined)
    // A written-then-structurally-shared object is handed back identical, so identity proves
    // nothing here: only the write itself can be asserted.
    const writes = vi.spyOn(client, 'setQueryData')

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [999], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(treeIn()![0]!.unread).toBe(5)
    expect(writes).not.toHaveBeenCalled()
  })

  it('counts a uid duplicated across two stream blocks only once', async () => {
    // dedupeByUid's reason to exist: an arrival between two fetches pushes a row into the next
    // block, so the same uid legitimately sits in both.
    seed([summaryOf(1, { seen: true })], [summaryOf(5), summaryOf(6)], [summaryOf(5), summaryOf(7)])
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [5], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // Both copies patched, but the badge moves by one.
    expect(streamIn()!.pages[0]!.messages[0]!.seen).toBe(true)
    expect(streamIn()!.pages[1]!.messages[0]!.seen).toBe(true)
    expect(treeIn()![0]!.unread).toBe(4)
  })

  it('counts every uid of a batch, even split across two page caches', async () => {
    // What the multi-select sends: uid 1 sits in the cached page 0, uid 2 only in page 1.
    // Counting one cache's delta would move the badge by one instead of two.
    client.setQueryData(mailKeys.messages('primary', 'INBOX', 0, 50), pageOf([summaryOf(1)]))
    client.setQueryData(mailKeys.messages('primary', 'INBOX', 1, 50), pageOf([summaryOf(2)]))
    client.setQueryData(foldersKey, [node('INBOX', 5)])
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1, 2], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(treeIn()![0]!.unread).toBe(3)
  })

  it('takes the delta from the cache that actually holds the message', async () => {
    // The page cache knows nothing about uid 7; its silence must not become a zero delta.
    seed([summaryOf(1, { seen: true })], [summaryOf(7)])
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [7], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(streamIn()!.pages[0]!.messages[0]!.seen).toBe(true)
    expect(treeIn()![0]!.unread).toBe(4)
  })

  it('patches the cached search results of the mutated folder', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    // Same uid, two folders: only the INBOX row must move, the Archive row is another message.
    client.setQueryData(searchKey,
      searchPageOf([searchRow(1, 'INBOX'), searchRow(1, 'Archive')]))
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(searchIn()!.results[0]!.seen).toBe(true)
    expect(searchIn()!.results[1]!.seen).toBe(false)
  })

  it('rolls the search cache back when the request fails', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    const seededSearch = searchPageOf([searchRow(1, 'INBOX')])
    client.setQueryData(searchKey, seededSearch)
    const pending = deferred<void>()
    mocks.setMessageFlags.mockReturnValue(pending.promise)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })

    // Patched first, so the restoration below is a real round trip and not a no-op.
    expect(searchIn()!.results[0]!.seen).toBe(true)

    await act(async () => { pending.reject(new Error('boom')) })
    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(searchIn()).toStrictEqual(seededSearch)
  })

  it('does not reconcile the search on a flag toggle', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX')]))
    mocks.searchMessages.mockResolvedValue(searchPageOf([searchRow(1, 'INBOX')]))
    mocks.setMessageFlags.mockResolvedValue(undefined)

    // Mount a search view; its initial fetch is the only search call a flag toggle may leave.
    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalledTimes(1))

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    // The row stays put, so patchSearchResults suffices: no invalidate, no reconcile refetch.
    expect(mocks.searchMessages).toHaveBeenCalledTimes(1)
  })

  it('patches a grouped page inside its threads, and the badge with it', async () => {
    // The grouped shape: every row is a thread member and `messages` is empty, so a patch that
    // only rewrote the flat list would leave the star and the unread mark on screen till the poll.
    const groupedPagesKey = mailKeys.messages('primary', 'INBOX', 0, 50, true)
    const groupedStreamKey = mailKeys.messageStream('primary', 'INBOX', 100, true)
    client.setQueryData(groupedPagesKey, groupedPageOf([[summaryOf(1), summaryOf(2)], [summaryOf(3)]]))
    client.setQueryData<InfiniteData<MailFolderPage>>(groupedStreamKey, {
      pages: [groupedPageOf([[summaryOf(1), summaryOf(2)]])], pageParams: [0],
    })
    client.setQueryData(foldersKey, [node('INBOX', 5)])
    mocks.setMessageFlags.mockResolvedValue(undefined)

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [2], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    const patched = client.getQueryData<MailFolderPage>(groupedPagesKey)!
    expect(patched.threads!.map(t => t.messages.map(m => m.seen))).toEqual([[false, true], [false]])
    const stream = client.getQueryData<InfiniteData<MailFolderPage>>(groupedStreamKey)!
    expect(stream.pages[0]!.threads![0]!.messages[1]!.seen).toBe(true)
    // One badge move for the two caches, counted off the thread members themselves.
    expect(treeIn()![0]!.unread).toBe(4)
  })

  it('never invalidates the stream key', async () => {
    seed([summaryOf(1)], [summaryOf(1)])
    mocks.setMessageFlags.mockResolvedValue(undefined)
    const spy = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetFlags(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    const keys = spy.mock.calls.map(([filters]) => JSON.stringify(filters?.queryKey ?? []))
    expect(keys.some(key => key.includes('messageStream'))).toBe(false)
    expect(spy).not.toHaveBeenCalled()
  })
})
