import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
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

async function renderWithBaseline(preferences: Record<string, string>, first = inbox()) {
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })
  mocks.getMailFolders.mockResolvedValue([first])

  const rendered = renderHook(() => useMailNotifications(), { wrapper })
  await waitFor(() =>
    expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

  return {
    ...rendered,
    tick: (next: MailFolderNode) =>
      act(() => { client.setQueryData(mailKeys.folders('primary'), [next]) }),
  }
}

const soundOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'false' }
const bothOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'true' }
const bothOff = { 'mail.notifySound': 'false', 'mail.notifyDesktop': 'false' }

describe('useMailNotifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.claimNotification.mockReturnValue(true)
    mocks.getMailMessages.mockResolvedValue(pageOf([11, 9]))
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('says nothing on the baseline observation', async () => {
    await renderWithBaseline(bothOn)

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
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

  // A deletion moves total, a read-flip moves unread; neither is new mail.
  it.each([
    ['a deletion elsewhere', inbox({ total: 4 })],
    ['a read-flip elsewhere', inbox({ unread: 1 })],
  ])('stays silent on %s', async (_label, next) => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(next)

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('stays silent with both settings off, and issues no fetch', async () => {
    const { tick } = await renderWithBaseline(bothOff)

    await tick(inbox({ uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  // The second tab must not beep: the bubble dedupes through its tag, the sound cannot.
  it('stays silent when another tab claimed the arrival', async () => {
    mocks.claimNotification.mockReturnValue(false)
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('watches the inbox by role, not by name', async () => {
    const archive = inbox({ path: 'Archive', name: 'Archive', specialUse: null })
    const { tick } = await renderWithBaseline(bothOn, archive)

    await tick(inbox({ path: 'Archive', name: 'Archive', specialUse: null, uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
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
})
