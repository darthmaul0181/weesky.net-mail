import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { type InfiniteData, type QueryClient } from '@tanstack/react-query'
import type {
  MailFolderNode, MailFolderPage, MailMessageSummary, MailSearchPage, MailSearchResult,
} from './api/mailTypes'
import { mailKeys, useDeleteMessages, useFolders, useMoveMessages, useSearchMessages } from './queries'
import { createHold } from './hold'
import { createTestQueryClient, settle, waitFor, withQueryClient } from '../../test-utils'
import { folderNodeOf, pageOf, summaryOf } from './mailTestHarness'

const mocks = vi.hoisted(() => ({
  moveMessages: vi.fn(), copyMessages: vi.fn(), deleteMessages: vi.fn(), searchMessages: vi.fn(),
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getPreferences: vi.fn(() => Promise.resolve({})),
}))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => import('../../test-auth'))

let client: QueryClient
let wrapper: ReturnType<typeof withQueryClient>

const streamOf = (folderPath: string, blocks: MailMessageSummary[][]): InfiniteData<MailFolderPage> => ({
  pages: blocks.map(block => pageOf(block, { folderPath })),
  pageParams: blocks.map((_, index) => index),
})

/** A grouped page as the backend sends one: the rows live in `threads`, `messages` stays empty. */
const groupedPageOf = (folderPath: string, groups: MailMessageSummary[][]): MailFolderPage => ({
  ...pageOf([], { folderPath }),
  threads: groups.map(messages => ({ messages })), totalThreads: groups.length,
})

const node = (path: string, total: number, unread: number): MailFolderNode =>
  folderNodeOf({ path, total, unread, uidNext: 100 })

const searchCriteria = { folderPath: '', allFolders: true, quick: 'x' }
const searchKey = mailKeys.search('primary', searchCriteria, 0, 50)

const searchRow = (uid: number, folderPath: string, over: Partial<MailSearchResult> = {}): MailSearchResult =>
  ({ ...summaryOf(uid, over), folderPath, uidValidity: 1 })

const searchPageOf = (results: MailSearchResult[], total: number): MailSearchPage =>
  ({ total, page: 0, pageSize: 50, results })

const searchIn = () => client.getQueryData<MailSearchPage>(searchKey)

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
  const page = pageOf([summaryOf(1), summaryOf(2, { seen: true }), summaryOf(3)])
  const stream = streamOf('INBOX', [[summaryOf(1), summaryOf(4)], [summaryOf(5)]])
  const target = pageOf([summaryOf(9)], { folderPath: 'Archive' })
  const targetBlocks = streamOf('Archive', [[summaryOf(9)]])
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
    client = createTestQueryClient({ mutations: { retry: false } })
    wrapper = withQueryClient(client)
  })

  it('patches at once, sends only when the hold releases, with keepalive', async () => {
    seed()
    mocks.moveMessages.mockResolvedValue(undefined)
    const hold = createHold()
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })
    expect(uidsOf(sourcePage()!.messages)).toEqual([2, 3])
    expect(mocks.moveMessages).not.toHaveBeenCalled()

    await act(async () => { hold.release(); await settle() })

    expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [1], 'Archive', { accountId: 'primary', keepalive: true })
  })

  it('an Undo restores the caches in silence and sends nothing', async () => {
    seed()
    const onError = vi.fn()
    const hold = createHold()
    const { result } = renderHook(() => useMoveMessages(onError), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })

    await act(async () => { hold.cancel(); await settle() })

    expect(uidsOf(sourcePage()!.messages)).toEqual([1, 2, 3])
    expect(folder('INBOX').total).toBe(20)
    expect(mocks.moveMessages).not.toHaveBeenCalled()
    expect(onError).not.toHaveBeenCalled()
  })

  const heldMove = (result: { current: ReturnType<typeof useMoveMessages> }, uids: number[]) => {
    const hold = createHold()
    result.current.mutate({ folderPath: 'INBOX', uids, targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    return hold
  }

  it('an Undo puts the rows back where they stood, keeping a write made meanwhile', async () => {
    seed()
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    let hold!: ReturnType<typeof createHold>
    await act(async () => { hold = heldMove(result, [1]) })
    client.setQueryData<MailFolderPage>(sourcePagesKey, page => page && {
      ...page, messages: page.messages.map(row => row.uid === 2 ? { ...row, flagged: true } : row),
    })

    await act(async () => { hold.cancel(); await settle() })

    expect(uidsOf(sourcePage()!.messages)).toEqual([1, 2, 3])
    expect(sourcePage()!.messages[1]!.flagged).toBe(true)
    expect(sourceStream()!.pages.map(block => uidsOf(block.messages))).toEqual([[1, 4], [5]])
    expect([folder('INBOX').total, folder('INBOX').unread]).toEqual([20, 5])
    expect([folder('Archive').total, folder('Archive').unread]).toEqual([3, 1])
  })

  it('an Undo of the second of two held moves leaves the first one out', async () => {
    seed()
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    let first!: ReturnType<typeof createHold>
    let second!: ReturnType<typeof createHold>
    await act(async () => { first = heldMove(result, [1]) })
    await act(async () => { second = heldMove(result, [3]) })

    await act(async () => { second.cancel(); await settle() })

    expect(uidsOf(sourcePage()!.messages)).toEqual([2, 3])
    expect(sourceStream()!.pages.map(block => uidsOf(block.messages))).toEqual([[4], [5]])
    expect([folder('INBOX').total, folder('INBOX').unread]).toEqual([19, 4])
    await act(async () => { first.cancel(); await settle() })
    expect(uidsOf(sourcePage()!.messages)).toEqual([1, 2, 3])
  })

  it('an Undo of the first of two held moves leaves the second one out', async () => {
    seed()
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    let first!: ReturnType<typeof createHold>
    let second!: ReturnType<typeof createHold>
    await act(async () => { first = heldMove(result, [1]) })
    await act(async () => { second = heldMove(result, [3]) })

    await act(async () => { first.cancel(); await settle() })

    expect(uidsOf(sourcePage()!.messages)).toEqual([1, 2])
    expect([folder('INBOX').total, folder('INBOX').unread]).toEqual([19, 4])
    await act(async () => { second.cancel(); await settle() })
  })

  it('an Undo brings back a conversation the move had emptied, in its place', async () => {
    const groupedKey = mailKeys.messages('primary', 'INBOX', 0, 50, true)
    client.setQueryData(groupedKey, groupedPageOf('INBOX',
      [[summaryOf(1), summaryOf(2)], [summaryOf(3)], [summaryOf(4)]]))
    client.setQueryData(foldersKey, [node('INBOX', 20, 5), node('Archive', 3, 1)])
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    let hold!: ReturnType<typeof createHold>
    await act(async () => { hold = heldMove(result, [2, 3]) })

    await act(async () => { hold.cancel(); await settle() })

    expect(client.getQueryData<MailFolderPage>(groupedKey)!.threads!
      .map(thread => uidsOf(thread.messages))).toEqual([[1, 2], [3], [4]])
  })

  it.each([
    ['streaming', { 'mail.pageSize': 'all' }],
    ['paged', { 'mail.pageSize': '50' }],
  ])('a held move the server refuses restores the caches and refreshes the %s list', async (mode, preferences) => {
    const seeded = seed()
    client.setQueryData(['preferences'], preferences)
    mocks.moveMessages.mockRejectedValue(new Error('boom'))
    mocks.getMailMessages.mockResolvedValue(pageOf([summaryOf(1)]))
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const onError = vi.fn()
    const { result } = renderHook(() => useMoveMessages(onError), { wrapper })
    let hold!: ReturnType<typeof createHold>
    await act(async () => { hold = heldMove(result, [1]) })

    await act(async () => { hold.release(); await settle() })

    expect(sourcePage()).toStrictEqual(seeded.page)
    expect(onError).toHaveBeenCalledWith('Could not move the message')
    if (mode === 'streaming') {
      expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100, { accountId: 'primary', grouped: false })
    } else {
      expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.messagesIn('primary', 'INBOX') })
    }
  })

  it('keeps the cached tree while a held move waits', async () => {
    const { tree: seeded } = seed()
    mocks.getMailFolders.mockResolvedValue(seeded)
    const tree = renderHook(() => useFolders(), { wrapper })
    await settle()
    const hold = createHold()
    const moved = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      moved.result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })
    const patched = folder('INBOX').total
    expect(patched).toBe(19)
    mocks.getMailFolders.mockClear()

    await act(async () => { await tree.result.current.refetch() })

    expect(mocks.getMailFolders).not.toHaveBeenCalled()
    expect(folder('INBOX').total).toBe(patched)
    await act(async () => { hold.cancel(); await settle() })
  })

  it.each([
    ['sent', (hold: ReturnType<typeof createHold>) => hold.release()],
    ['undone', (hold: ReturnType<typeof createHold>) => hold.cancel()],
  ])('refreshes the tree a held move kept back once it is %s', async (_state, end) => {
    const { tree: seeded } = seed()
    mocks.getMailFolders.mockResolvedValue(seeded)
    mocks.moveMessages.mockResolvedValue(undefined)
    renderHook(() => useFolders(), { wrapper })
    await settle()
    const hold = createHold()
    const moved = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      moved.result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })
    mocks.getMailFolders.mockClear()

    await act(async () => { await client.invalidateQueries({ queryKey: foldersKey }) })
    expect(mocks.getMailFolders).not.toHaveBeenCalled()

    await act(async () => { end(hold); await settle() })
    expect(mocks.getMailFolders).toHaveBeenCalledTimes(1)
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
    expect(uidsOf(sourceStream()!.pages[0]!.messages)).toEqual([4])
    expect(uidsOf(sourceStream()!.pages[1]!.messages)).toEqual([5])
    // uid 1 unseen, uid 2 seen: two off the total, one off the badge.
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
    // Removed, never invalidated: a refetch here would cost one IMAP connection per block.
    expect(targetPage()).toBeUndefined()
    expect(targetStream()).toBeUndefined()
    expect(folder('Archive').total).toBe(5)
    expect(folder('Archive').unread).toBe(2)
    expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [1, 2], 'Archive', { accountId: 'primary' })

    await act(async () => { pending.resolve() })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(uidsOf(sourcePage()!.messages)).toEqual([3])
    expect(folder('INBOX').total).toBe(18)
  })

  it('takes the rows out of a grouped page, dropping a thread it emptied', async () => {
    const groupedKey = mailKeys.messages('primary', 'INBOX', 0, 50, true)
    client.setQueryData(groupedKey,
      groupedPageOf('INBOX', [[summaryOf(1), summaryOf(2, { seen: true })], [summaryOf(3)]]))
    client.setQueryData(foldersKey, [node('INBOX', 20, 5), node('Archive', 3, 1)])
    mocks.moveMessages.mockResolvedValue(undefined)

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [2, 3], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // uid 3 was its thread's only member, so the row leaves with it; uid 2's thread keeps uid 1.
    expect(client.getQueryData<MailFolderPage>(groupedKey)!.threads!
      .map(thread => thread.messages.map(message => message.uid))).toEqual([[1]])
    // Both counters move off the thread members — `messages` holds nothing to count here.
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
    expect(folder('Archive').total).toBe(5)
    expect(folder('Archive').unread).toBe(2)
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
    expect(uidsOf(sourceStream()!.pages[0]!.messages)).toEqual([4])
    expect(folder('INBOX').total).toBe(19)
    expect(folder('INBOX').unread).toBe(4)
    expect(folder('Archive').total).toBe(4)
    expect(folder('Archive').unread).toBe(2)
  })

  it('counts uids split across a cached page and a stream block as two, not one', async () => {
    // uid 1 lives only in the page, uid 2 only in stream block 1 — two different caches,
    // neither uid overlapping the other's cache, unlike the dedup case above.
    const page = pageOf([summaryOf(1), summaryOf(3)])
    const stream = streamOf('INBOX', [[summaryOf(4)], [summaryOf(2, { seen: true })]])
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
    expect(uidsOf(sourceStream()!.pages[1]!.messages)).toEqual([])
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
    expect(mocks.copyMessages).toHaveBeenCalledWith('INBOX', [1, 2], 'Archive', { accountId: 'primary' })
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

  it('drops the row from cached search results and rolls back on error', async () => {
    seed()
    // A search page holding the moved INBOX row and a same-uid Archive row that must survive.
    const seededSearch = searchPageOf([searchRow(1, 'INBOX'), searchRow(1, 'Archive')], 2)
    client.setQueryData(searchKey, seededSearch)
    const pending = deferred<void>()
    mocks.moveMessages.mockReturnValue(pending.promise)

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })

    // Row of the mutated folder gone, total decremented, the other folder's row kept.
    expect(uidsOf(searchIn()!.results)).toEqual([1])
    expect(searchIn()!.results[0]!.folderPath).toBe('Archive')
    expect(searchIn()!.total).toBe(1)

    await act(async () => { pending.reject(new Error('boom')) })
    await waitFor(() => expect(result.current.isError).toBe(true))

    // Snapshot restored: the row and the total are back.
    expect(searchIn()).toStrictEqual(seededSearch)
  })

  it('cancels an in-flight search fetch so a late resolve cannot resurrect the moved row', async () => {
    seed()
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    const staleFetch = deferred<MailSearchPage>()
    // The mount fetch is in flight; the reconcile refetch after settle returns the re-windowed page.
    mocks.searchMessages
      .mockReturnValueOnce(staleFetch.promise)
      .mockResolvedValue(searchPageOf([searchRow(3, 'INBOX')], 1))
    mocks.moveMessages.mockResolvedValue(undefined)

    // A search view is loading over the already-cached page: its fetch is in flight.
    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalled())

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    // Optimistic removal then reconcile refetch both agree the row is gone.
    expect(uidsOf(searchIn()!.results)).toEqual([3])

    // The pre-removal server list resolves late; the cancelled fetch must not overwrite the cache.
    await act(async () => {
      staleFetch.resolve(searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    })
    await settle()

    expect(uidsOf(searchIn()!.results)).toEqual([3])
  })

  // A search that has never loaded holds no row to protect: cancelLoaded leaves its first fetch
  // alone and the settle-invalidate reconciles it. Which of the two pages wins is that
  // invalidate's business — pinned above; what is pinned here is that neither leaves it blank.
  it('lands the results of a search still on its first fetch when the move fires', async () => {
    seed()
    const firstLoad = deferred<MailSearchPage>()
    mocks.searchMessages
      .mockReturnValueOnce(firstLoad.promise)
      .mockResolvedValue(searchPageOf([searchRow(3, 'INBOX')], 1))
    mocks.moveMessages.mockResolvedValue(undefined)

    // Cold search cache: this is the very first fetch, not a refetch over a cached page.
    const search = renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalledTimes(1))

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await act(async () => {
      firstLoad.resolve(searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    })
    await settle()

    // Populated either way today — the reconcile covers this path too. The point is that no
    // future change to either half may leave the pane pending with no data.
    expect(search.result.current.data?.results.length).toBeGreaterThan(0)
  })

  it('reconciles the active search on settle after a move', async () => {
    seed()
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    mocks.searchMessages.mockResolvedValue(searchPageOf([searchRow(3, 'INBOX')], 1))
    mocks.moveMessages.mockResolvedValue(undefined)

    // Mount the search view; its initial fetch is the baseline the reconcile adds to.
    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalledTimes(1))

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    // The removal re-windows the page: the mounted search refetched against the server.
    expect(mocks.searchMessages).toHaveBeenCalledTimes(2)
  })

  it('does not reconcile the search on a copy', async () => {
    seed()
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX')], 1))
    mocks.searchMessages.mockResolvedValue(searchPageOf([searchRow(1, 'INBOX')], 1))
    mocks.copyMessages.mockResolvedValue(undefined)

    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalledTimes(1))

    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({
        folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: true,
      })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    // A copy leaves the source row in place: nothing to re-window, no reconcile refetch.
    expect(mocks.searchMessages).toHaveBeenCalledTimes(1)
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

    // The removal reconcile invalidates search, never a stream key.
    const keys = spy.mock.calls.map(([filters]) => JSON.stringify(filters?.queryKey ?? []))
    expect(keys.some(key => key.includes('messageStream'))).toBe(false)
  })
})

describe('useDeleteMessages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = createTestQueryClient({ mutations: { retry: false } })
    wrapper = withQueryClient(client)
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
    expect(uidsOf(sourceStream()!.pages[0]!.messages)).toEqual([4])
    expect(folder('INBOX').total).toBe(18)
    expect(folder('INBOX').unread).toBe(4)
    // No target exists for a delete: the other folder's caches and counters stay as seeded.
    expect(targetPage()).toBe(seeded.target)
    expect(targetStream()).toBe(seeded.targetBlocks)
    expect(folder('Archive').total).toBe(3)
    expect(folder('Archive').unread).toBe(1)
    expect(mocks.deleteMessages).toHaveBeenCalledWith('INBOX', [1, 2], { accountId: 'primary' })
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

  it('cancels an in-flight search fetch so a late resolve cannot resurrect the deleted row', async () => {
    seed()
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    const staleFetch = deferred<MailSearchPage>()
    mocks.searchMessages
      .mockReturnValueOnce(staleFetch.promise)
      .mockResolvedValue(searchPageOf([searchRow(3, 'INBOX')], 1))
    mocks.deleteMessages.mockResolvedValue(undefined)

    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalled())

    const { result } = renderHook(() => useDeleteMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1] })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    expect(uidsOf(searchIn()!.results)).toEqual([3])

    await act(async () => {
      staleFetch.resolve(searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    })
    await settle()

    expect(uidsOf(searchIn()!.results)).toEqual([3])
  })

  it('reconciles the active search on settle after a delete', async () => {
    seed()
    client.setQueryData(searchKey, searchPageOf([searchRow(1, 'INBOX'), searchRow(3, 'INBOX')], 2))
    mocks.searchMessages.mockResolvedValue(searchPageOf([searchRow(3, 'INBOX')], 1))
    mocks.deleteMessages.mockResolvedValue(undefined)

    renderHook(() => useSearchMessages(searchCriteria, 0, 50), { wrapper })
    await waitFor(() => expect(mocks.searchMessages).toHaveBeenCalledTimes(1))

    const { result } = renderHook(() => useDeleteMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1] })
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    await settle()

    // The removal re-windows the page: the mounted search refetched against the server.
    expect(mocks.searchMessages).toHaveBeenCalledTimes(2)
  })
})
