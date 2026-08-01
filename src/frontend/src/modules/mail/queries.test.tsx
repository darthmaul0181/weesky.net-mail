import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { settle } from '../../test-utils'
import { POLL_INTERVAL, mailKeys, useCreateFolder, useFolders, useMessage, useMessages, useMessageStream, useReplaceIdentities, useSearchMessages, useSendMessage, useSetFlags } from './queries'
import type { MailFolderNode } from './api/mailTypes'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
  createMailFolder: vi.fn(),
  getPreferences: vi.fn(),
  searchMessages: vi.fn(),
  sendMessage: vi.fn(),
  setMessageFlags: vi.fn(),
  putIdentities: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: {
    getMailFolders: mocks.getMailFolders,
    getMailMessages: mocks.getMailMessages,
    getMailMessage: mocks.getMailMessage,
    createMailFolder: mocks.createMailFolder,
    getPreferences: mocks.getPreferences,
    searchMessages: mocks.searchMessages,
    sendMessage: mocks.sendMessage,
    setMessageFlags: mocks.setMessageFlags,
    putIdentities: mocks.putIdentities,
  },
}))

// Mutable so a test can switch accounts mid-flight, the way the account menu does.
const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: auth.activeAccountId, email: 'alice@weesky.be' },
    activeAccountId: auth.activeAccountId,
  }),
}))

beforeEach(() => { auth.activeAccountId = 'primary' })

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

  // Criteria live in the key: two different searches are two cache entries, never one
  // overwriting the other.
  it('gives different criteria different search keys', () => {
    const a = mailKeys.search('primary', { folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 50)
    const b = mailKeys.search('primary', { folderPath: 'INBOX', allFolders: false, quick: 'y' }, 0, 50)
    expect(a).not.toEqual(b)
  })

  // A page fetched at 30 per page is not the same page at 100: without the size in the key,
  // changing it would serve rows computed under the old one.
  it('gives different page sizes different keys', () => {
    expect(mailKeys.messages('primary', 'INBOX', 0, 30))
      .not.toEqual(mailKeys.messages('primary', 'INBOX', 0, 100))
  })
})

describe('useFolders', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30' })
  })

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

  // The shell mounts this on every route for the notification watcher; a user who asked for no
  // notification must not be put on a poll that buys them nothing.
  it('issues no request when the caller does not enable it', async () => {
    mocks.getMailFolders.mockResolvedValue([])
    const { wrapper } = createWrapper()

    renderHook(() => useFolders(false), { wrapper })
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })

    expect(mocks.getMailFolders).not.toHaveBeenCalled()
  })

  // /mail asks for the tree unconditionally, so the shell's disabled observer costs nothing.
  it('runs when one observer enables it and another does not', async () => {
    mocks.getMailFolders.mockResolvedValue([])
    const { wrapper } = createWrapper()

    renderHook(() => { useFolders(false); useFolders() }, { wrapper })

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalledTimes(1))
  })

  // A notification is useful only while the tab is elsewhere, so the poll has to survive the
  // loss of focus — but only for those who asked: an untouched tab must keep costing nothing.
  it.each([
    [{ 'mail.notifySound': 'false', 'mail.notifyDesktop': 'false' }, false],
    [{ 'mail.notifySound': 'true', 'mail.notifyDesktop': 'false' }, true],
    [{ 'mail.notifySound': 'false', 'mail.notifyDesktop': 'true' }, true],
  ])('polls in the background only when a notification is on', async (preferences, expected) => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })
    mocks.getMailFolders.mockResolvedValue([])
    const { wrapper, client } = createWrapper()

    renderHook(() => useFolders(), { wrapper })

    // Before the preferences land the flag is false whatever they say, so an assertion made
    // here passes against a hook that polls in the background for everyone. Settle first.
    await waitFor(() => expect(client.getQueryData(['preferences'])).toBeDefined())
    await waitFor(() =>
      expect(client.getQueryCache().find({ queryKey: mailKeys.folders('primary') })).toBeDefined())
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })

    // Cast: the cache types its stored options as QueryOptions, which omits the observer-only
    // fields the query is nonetheless created with.
    expect((client.getQueryCache().find({ queryKey: mailKeys.folders('primary') })!
      .options as { refetchIntervalInBackground?: boolean }).refetchIntervalInBackground)
      .toBe(expected)
  })
})

// Every request names the mailbox it is for: without the header the backend answers from the
// primary account, so a switched-to mailbox would fill its own cache with somebody else's mail.
describe('account scoping on the wire', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30' })
  })

  it('carries the active account on a read', async () => {
    mocks.getMailFolders.mockResolvedValue([])
    auth.activeAccountId = 'linked-1'
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useFolders(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mocks.getMailFolders).toHaveBeenCalledWith(
      expect.objectContaining({ accountId: 'linked-1' }))
  })

  it('carries the active account on a paged read', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))
    auth.activeAccountId = 'linked-1'
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useMessages('INBOX', 0, 30), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 30,
      expect.objectContaining({ accountId: 'linked-1' }))
  })

  // The placeholder exists so paging does not flash empty. Carried across a mailbox it shows one
  // account's mail under another's heading — worse than the empty it was avoiding, and the one
  // thing a reader cannot tell apart from real data.
  it('does not hold the previous account page on screen while the new one loads', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))
    const { wrapper } = createWrapper()
    const { result, rerender } = renderHook(() => useMessages('INBOX', 0, 30), { wrapper })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    mocks.getMailMessages.mockImplementation(() => new Promise(() => {}))
    auth.activeAccountId = 'linked-1'
    rerender()

    expect(result.current.data).toBeUndefined()
  })

  // Same reason, one step closer: another folder's mail under this folder's name is the same lie.
  it('does not hold the previous folder page on screen while the new one loads', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))
    const { wrapper } = createWrapper()
    const { result, rerender } = renderHook(
      ({ folder }: { folder: string }) => useMessages(folder, 0, 30),
      { wrapper, initialProps: { folder: 'INBOX' } })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    mocks.getMailMessages.mockImplementation(() => new Promise(() => {}))
    rerender({ folder: 'Archive' })

    expect(result.current.data).toBeUndefined()
  })

  // What the placeholder is actually for: the next page of the same folder, same mailbox.
  it('keeps the current page on screen while the next page of the same folder loads', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 60))
    const { wrapper } = createWrapper()
    const { result, rerender } = renderHook(
      ({ page }: { page: number }) => useMessages('INBOX', page, 30),
      { wrapper, initialProps: { page: 0 } })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    mocks.getMailMessages.mockImplementation(() => new Promise(() => {}))
    rerender({ page: 1 })

    expect(result.current.data?.messages.map(m => m.uid)).toEqual([1])
  })

  // The subtle one: a write is aimed at the mailbox it was fired from. Switching while it is in
  // flight must not redirect a STORE already on the wire into the other account's folder.
  it('fires useSetFlags with the account it was rendered under, even after a switch', async () => {
    let land!: () => void
    mocks.setMessageFlags.mockImplementation(
      () => new Promise<void>(resolve => { land = () => resolve() }))
    const { wrapper } = createWrapper()

    const { result, rerender } = renderHook(() => useSetFlags(), { wrapper })
    act(() => result.current.mutate({ folderPath: 'INBOX', uids: [7], flag: 'seen', value: true }))
    await waitFor(() => expect(mocks.setMessageFlags).toHaveBeenCalled())

    auth.activeAccountId = 'linked-1'
    rerender()
    await act(async () => { land() })

    // Neither replayed against the new mailbox nor redirected into it.
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
    expect(mocks.setMessageFlags).toHaveBeenCalledWith(
      'INBOX', [7], 'seen', true, { accountId: 'primary' })
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

describe('useSearchMessages', () => {
  beforeEach(() => vi.clearAllMocks())

  it('fetches when criteria are set', async () => {
    mocks.searchMessages.mockResolvedValue({ total: 1, page: 0, pageSize: 50, results: [] })
    const { wrapper } = createWrapper()

    const { result } = renderHook(
      () => useSearchMessages({ folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 50),
      { wrapper },
    )

    await waitFor(() => expect(result.current.data?.total).toBe(1))
    expect(mocks.searchMessages).toHaveBeenCalledWith(
      { folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 50, expect.anything())
  })

  it('stays idle with null criteria', async () => {
    const { wrapper } = createWrapper()

    renderHook(() => useSearchMessages(null, 0, 50), { wrapper })
    await settle()

    expect(mocks.searchMessages).not.toHaveBeenCalled()
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

describe('useSendMessage', () => {
  beforeEach(() => vi.clearAllMocks())

  const sendArgs = {
    to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [],
    priority: 'normal' as const,
  }

  it('calls api.sendMessage with the composed args', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    const { wrapper } = createWrapper()

    const { result } = renderHook(() => useSendMessage(), { wrapper })
    await result.current.mutateAsync(sendArgs)

    expect(mocks.sendMessage).toHaveBeenCalledWith(sendArgs, { accountId: 'primary' })
  })

  it('invalidates the folders and the sent folder lists when the tree has a sent node', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    const { client, wrapper } = createWrapper()
    const tree: MailFolderNode[] = [
      {
        path: 'Sent', name: 'Sent', specialUse: 'sent', selectable: true, subscribed: true,
        total: 1, unread: 0, uidValidity: 1, uidNext: 2, highestModSeq: null, children: [],
      },
    ]
    client.setQueryData(mailKeys.folders('primary'), tree)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSendMessage(), { wrapper })
    await result.current.mutateAsync(sendArgs)

    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.folders('primary') })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.messagesIn('primary', 'Sent') })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.messageStreamIn('primary', 'Sent') })
  })

  it('does not invalidate any sent-folder key when the tree holds no sent node', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: false })
    const { client, wrapper } = createWrapper()
    const tree: MailFolderNode[] = [
      {
        path: 'INBOX', name: 'INBOX', specialUse: 'inbox', selectable: true, subscribed: true,
        total: 1, unread: 0, uidValidity: 1, uidNext: 2, highestModSeq: null, children: [],
      },
    ]
    client.setQueryData(mailKeys.folders('primary'), tree)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSendMessage(), { wrapper })
    await result.current.mutateAsync(sendArgs)

    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.folders('primary') })
    expect(invalidate).toHaveBeenCalledTimes(1)
  })
})

describe('useReplaceIdentities', () => {
  beforeEach(() => vi.clearAllMocks())

  // The set is per mailbox: without the header the connected account's identities would be
  // written over the primary's.
  it('names the active account on the PUT and invalidates its own key', async () => {
    auth.activeAccountId = 'linked-1'
    mocks.putIdentities.mockResolvedValue(undefined)
    const { client, wrapper } = createWrapper()
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const rows = [{ address: 'shared@ext.example', displayName: 'Shared', isDefault: true }]

    const { result } = renderHook(() => useReplaceIdentities(), { wrapper })
    await result.current.mutateAsync(rows)

    expect(mocks.putIdentities).toHaveBeenCalledWith(rows, { accountId: 'linked-1' })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.identities('linked-1') })
  })
})
