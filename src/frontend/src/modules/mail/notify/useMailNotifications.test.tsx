import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode, type ReactNode } from 'react'
import type { MailFolderNode } from '../api/mailTypes'
import { mailKeys } from '../queries'
import { useMailNotifications } from './useMailNotifications'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getPreferences: vi.fn(),
  playNewMailSound: vi.fn(), showDesktopNotification: vi.fn(), claimNotification: vi.fn(),
  navigate: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))
vi.mock('./channels', () => ({
  playNewMailSound: mocks.playNewMailSound,
  showDesktopNotification: mocks.showDesktopNotification,
  claimNotification: mocks.claimNotification,
}))
vi.mock('react-router-dom', () => ({ useNavigate: () => mocks.navigate }))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

/** What main.tsx actually renders into. Its dev double-invoke runs mount → cleanup → mount on
    the same refs, which is a different hook lifecycle, not a slower one. */
function strictWrapper({ children }: { children: ReactNode }) {
  return <StrictMode>{wrapper({ children })}</StrictMode>
}

/**
 * A macrotask boundary, which drains every pending microtask. The notification is raised at the
 * end of the describe-fetch's await chain: a silence assertion made before that chain drains
 * holds against any hook whatsoever, including one that notifies on every tick.
 */
async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
}

function inbox(overrides: Partial<MailFolderNode> = {}): MailFolderNode {
  return {
    path: 'INBOX', name: 'INBOX', specialUse: 'inbox', selectable: true, subscribed: true,
    total: 5, unread: 2, uidValidity: 100, uidNext: 10, highestModSeq: 40, children: [],
    ...overrides,
  }
}

function pageOf(uids: number[]) {
  return {
    folderPath: 'INBOX', uidValidity: 100, total: 5, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: `Subject ${uid}`, fromName: `Sender ${uid}`, fromAddress: 'a@b.c',
      date: '2026-07-21T00:00:00Z', seen: false, flagged: false, answered: false,
      hasAttachments: false, size: 0, preview: '',
    })),
  }
}

/**
 * Seeds the folder tree rather than letting the query fetch it: with both settings off the
 * shell's observer is disabled and no fetch would ever arrive, and the hook is what is under
 * test here, not the poll.
 */
async function renderWithBaseline(
  preferences: Record<string, string>, first: MailFolderNode[] = [inbox()], under = wrapper,
) {
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })
  mocks.getMailFolders.mockResolvedValue(first)
  client.setQueryData(mailKeys.folders('primary'), first)

  const rendered = renderHook(() => useMailNotifications(), { wrapper: under })
  await waitFor(() => expect(client.getQueryData(['preferences'])).toBeDefined())
  await settle()

  return {
    ...rendered,
    tick: (...next: MailFolderNode[]) =>
      act(() => { client.setQueryData(mailKeys.folders('primary'), next) }),
  }
}

/**
 * Holds the description fetch open, so an unmount or a dependency change lands while it is in
 * flight. Without `started()` the hook is torn down before it has even decided: the race the
 * test means to stage never happens, and the silence it asserts proves nothing.
 */
function heldFetch() {
  let release: (page: unknown) => void = () => {}
  mocks.getMailMessages.mockImplementation((
    _folder: string, _page: number, _size: number, { signal }: { signal: AbortSignal },
  ) => new Promise((resolve, reject) => {
    release = resolve
    signal.addEventListener('abort', () => reject(new Error('aborted')))
  }))

  return {
    started: () => waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalled()),
    release: (page: unknown) => act(async () => { release(page) }),
  }
}

const soundOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'false' }
const desktopOn = { 'mail.notifySound': 'false', 'mail.notifyDesktop': 'true' }
const bothOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'true' }
const bothOff = { 'mail.notifySound': 'false', 'mail.notifyDesktop': 'false' }

describe('useMailNotifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.claimNotification.mockReturnValue(true)
    mocks.getMailMessages.mockResolvedValue(pageOf([11, 9]))
    // staleTime keeps the seeded tree from being refetched behind a tick and put back.
    client = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    })
  })

  it('says nothing on the baseline observation', async () => {
    await renderWithBaseline(bothOn)
    await settle()

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  // main.tsx renders under StrictMode, so this is the dev server's lifecycle, not an exotic one:
  // a mounted flag that is only ever cleared latches false on the first cleanup and gags the
  // whole session, making the feature unverifiable by hand.
  it('plays the sound under StrictMode, whose double-invoke reuses the refs', async () => {
    const { tick } = await renderWithBaseline(soundOn, [inbox()], strictWrapper)

    await tick(inbox({ uidNext: 11, total: 6, unread: 3 }))

    await waitFor(() => expect(mocks.playNewMailSound).toHaveBeenCalled())
  })

  it('plays the sound when mail arrives', async () => {
    const { tick } = await renderWithBaseline(soundOn)

    await tick(inbox({ uidNext: 11, total: 6, unread: 3 }))

    await waitFor(() => expect(mocks.playNewMailSound).toHaveBeenCalledTimes(1))
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('names the sender and subject of a single arrival', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      'Sender 11 — Subject 11', expect.any(String), expect.any(Function)))
  })

  it('counts when several arrive at once', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([12, 11, 9]))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 12 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      '2 new messages', expect.any(String), expect.any(Function)))
  })

  // Always fetched, never read from the message cache: the same poll tick drives the list
  // refresh, whose write lands after this one reads, so the cache holds the pre-arrival page.
  it('fetches the page rather than reading a cache the arrival has not reached yet', async () => {
    const { tick } = await renderWithBaseline(bothOn)
    client.setQueryData(mailKeys.messages('primary', 'INBOX', 0, 30), pageOf([9]))

    await tick(inbox({ uidNext: 11 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      'Sender 11 — Subject 11', expect.any(String), expect.any(Function)))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 30, expect.anything())
  })

  // A deletion moves total, a read-flip moves unread; neither is new mail.
  it.each([
    ['a deletion elsewhere', inbox({ total: 4 })],
    ['a read-flip elsewhere', inbox({ unread: 1 })],
  ])('stays silent on %s', async (_label, next) => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(next)
    await settle()

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('stays silent with both settings off, and issues no fetch', async () => {
    const { tick } = await renderWithBaseline(bothOff)

    await tick(inbox({ uidNext: 11 }))
    await settle()

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  // The second tab must not beep: the bubble dedupes through its tag, the sound cannot. The
  // claim carries the arrival's uidNext, which is what the other tab compares against.
  it('stays silent when another tab claimed the arrival', async () => {
    mocks.claimNotification.mockReturnValue(false)
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 12 }))
    await settle()

    expect(mocks.claimNotification).toHaveBeenCalledWith(100, 12)
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  // The folder named INBOX is a decoy: the role sits on another one, and mail landing in the
  // decoy is somebody else's folder filling up.
  it('watches the inbox by role, not by name', async () => {
    const decoy = inbox({ path: 'INBOX', name: 'INBOX', specialUse: null })
    const real = inbox({ path: 'Courrier', name: 'Courrier', specialUse: 'inbox' })
    const { tick } = await renderWithBaseline(bothOn, [decoy, real])

    await tick(inbox({ path: 'INBOX', name: 'INBOX', specialUse: null, uidNext: 40 }), real)
    await settle()

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  // The server rebuilt the folder: uidNext leaps to an unrelated value. Announcing that leap
  // would also claim it, and the claim would gag every tab until the real uidNext caught up.
  it('re-baselines in silence when uidValidity breaks', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidValidity: 200, uidNext: 4783 }))
    await settle()

    expect(mocks.claimNotification).not.toHaveBeenCalled()
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()

    await tick(inbox({ uidValidity: 200, uidNext: 4784 }))

    await waitFor(() => expect(mocks.playNewMailSound).toHaveBeenCalledTimes(1))
    // Claimed under the new numbering, so the entry banked before the break cannot gag it.
    expect(mocks.claimNotification).toHaveBeenCalledWith(200, 4784)
  })

  // The role moved to another folder: its uidNext has nothing to do with the old one's.
  it('re-baselines in silence when the inbox moves to another folder', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ path: 'Courrier', name: 'Courrier', uidNext: 4000 }))
    await settle()

    expect(mocks.claimNotification).not.toHaveBeenCalled()
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()

    await tick(inbox({ path: 'Courrier', name: 'Courrier', uidNext: 4001 }))

    await waitFor(() => expect(mocks.playNewMailSound).toHaveBeenCalledTimes(1))
  })

  // Clicking must land on the message, not merely raise the window — the notification named
  // that message, so it has to be what opens.
  it('opens the named message when its notification is clicked', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))
    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalled())

    const onClick = mocks.showDesktopNotification.mock.calls[0][2] as () => void
    onClick()

    expect(mocks.navigate).toHaveBeenCalledWith('/mail?folder=INBOX&uid=11')
  })

  it('only raises the window when several arrived', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([12, 11, 9]))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 12 }))
    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalled())

    ;(mocks.showDesktopNotification.mock.calls[0][2] as () => void)()

    expect(mocks.navigate).not.toHaveBeenCalled()
  })

  it('still notifies when the fetch fails, counting instead of naming', async () => {
    mocks.getMailMessages.mockRejectedValue(new Error('refused'))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      '1 new message', expect.any(String), expect.any(Function)))
  })

  // Logout, or an account switch, mid-fetch: the bubble would arrive after the session it
  // belongs to.
  it('says nothing once the effect that started the fetch is gone', async () => {
    const fetch = heldFetch()
    const { tick, unmount } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))
    await fetch.started()
    unmount()
    await fetch.release(pageOf([11, 9]))
    await settle()

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  // The cleanup runs on any dependency change, not only unmount, and the claim is already
  // banked: staying silent here would lose the arrival in every tab at once.
  it('still notifies when a dependency change aborts the fetch, counting instead of naming',
    async () => {
      const fetch = heldFetch()
      const { tick } = await renderWithBaseline(bothOn)

      await tick(inbox({ uidNext: 11 }))
      await fetch.started()
      // Same uidNext, so no second decision — only the effect re-running and aborting.
      await tick(inbox({ uidNext: 11, unread: 3 }))

      await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
        '1 new message', expect.any(String), expect.any(Function)))
      expect(mocks.playNewMailSound).toHaveBeenCalledTimes(1)
    })

  // Off, the poll stops and the unobserved tree is eventually collected; the uidNext that comes
  // back hours later is a backlog, and announcing it would also claim it for every tab.
  it('re-baselines in silence when notifications are turned back on', async () => {
    const { tick } = await renderWithBaseline(bothOn)
    const setPreferences = (preferences: Record<string, string>) => act(() => {
      client.setQueryData(['preferences'], { 'mail.pageSize': '30', ...preferences })
    })

    await setPreferences(bothOff)
    await act(() => { client.removeQueries({ queryKey: mailKeys.folders('primary') }) })
    await settle()
    await setPreferences(bothOn)
    await tick(inbox({ uidNext: 500, total: 495 }))
    await settle()

    expect(mocks.claimNotification).not.toHaveBeenCalled()
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()

    await tick(inbox({ uidNext: 501, total: 496 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      '1 new message', expect.any(String), expect.any(Function)))
    expect(mocks.claimNotification).toHaveBeenCalledWith(100, 501)
  })
})

// The shell mounts this hook on every route, so a user who asked for nothing must not be put on
// a poll they get no value from.
describe('useMailNotifications, from the shell', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.claimNotification.mockReturnValue(true)
    mocks.getMailFolders.mockResolvedValue([inbox()])
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('issues no folder request when both settings are off', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...bothOff })

    renderHook(() => useMailNotifications(), { wrapper })
    await settle()
    await settle()

    expect(mocks.getMailFolders).not.toHaveBeenCalled()
  })

  it.each([['the sound', soundOn], ['the desktop bubble', desktopOn]])(
    'polls the folders when %s is on', async (_label, preferences) => {
      mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })

      renderHook(() => useMailNotifications(), { wrapper })

      await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalledTimes(1))
    })
})
