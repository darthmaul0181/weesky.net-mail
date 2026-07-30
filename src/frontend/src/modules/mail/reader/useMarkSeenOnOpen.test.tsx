import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import { StrictMode, type ReactNode } from 'react'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { mailKeys, useSetFlags } from '../queries'
import { settle } from '../../../test-utils'
import { findCachedSummary, useMarkSeenOnOpen } from './useMarkSeenOnOpen'

const mocks = vi.hoisted(() => ({ setMessageFlags: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' }),
}))

let client: QueryClient
/** What main.tsx renders into. Its double-invoke of mount effects is the one route to a
    duplicate fire that production actually takes, so every test here runs under it. */
function wrapper({ children }: { children: ReactNode }) {
  return <StrictMode><QueryClientProvider client={client}>{children}</QueryClientProvider></StrictMode>
}

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', to: [], date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  priority: 'normal',
  ...over,
})

const pageOf = (messages: MailMessageSummary[]): MailFolderPage => ({
  folderPath: 'INBOX', uidValidity: 1, total: 20, page: 0, pageSize: 50, messages,
})

const pagesKey = mailKeys.messages('primary', 'INBOX', 0, 50)
const streamKey = mailKeys.messageStream('primary', 'INBOX', 100)

function seedPage(messages: MailMessageSummary[]) {
  client.setQueryData(pagesKey, pageOf(messages))
}

function seedStream(...blocks: MailMessageSummary[][]) {
  const stream: InfiniteData<MailFolderPage> = {
    pages: blocks.map(pageOf), pageParams: blocks.map((_, index) => index),
  }
  client.setQueryData(streamKey, stream)
}

interface HostProps { folderPath: string | null; uid: number | null; detailLoaded: boolean }
function Host({ folderPath, uid, detailLoaded }: HostProps) {
  useMarkSeenOnOpen(folderPath, uid, detailLoaded)
  return <span>open</span>
}

/** The reader's Mark as unread, the one write the reconcile has to give way to. */
function Toggle() {
  const { mutate } = useSetFlags()
  const unread = () => mutate({ folderPath: 'INBOX', uids: [42], flag: 'seen', value: false })
  return <button type="button" onClick={unread}>mark unread</button>
}

async function markUnread() {
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'mark unread' })) })
  await settle()
}

async function renderHost(props: HostProps) {
  const view = render(<Host {...props} />, { wrapper })
  await settle()
  return {
    async rerender(next: Partial<HostProps>) {
      view.rerender(<Host {...props} {...next} />)
      await settle()
    },
  }
}

describe('useMarkSeenOnOpen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.setMessageFlags.mockResolvedValue(undefined)
    client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
  })

  it('fires once when the detail arrives on an unread message', async () => {
    seedPage([summary(1), summary(2)])

    const host = await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: false })
    expect(mocks.setMessageFlags).not.toHaveBeenCalled()

    await host.rerender({ detailLoaded: true })

    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'seen', true, { accountId: 'primary' })

    // A plain re-render is not a second opening.
    await host.rerender({ detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
  })

  it('fires once on a mount whose effect StrictMode double-invokes', async () => {
    seedPage([summary(2)])

    // Mounted straight into the fired state, which is where main.tsx's double-invoke bites and
    // where nothing but the arming ref stands between one write and two.
    await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })

    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
  })

  it('does not fire on an already-read message', async () => {
    seedPage([summary(2, { seen: true })])

    await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })

    expect(mocks.setMessageFlags).not.toHaveBeenCalled()
  })

  it('does not fire again after Mark as unread while the uid is unchanged', async () => {
    seedPage([summary(2)])

    const host = await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

    // What "Mark as unread" leaves behind: the cached summary is unread again, and the reader
    // is still open on the same message.
    await act(async () => { client.setQueryData(pagesKey, pageOf([summary(2, { seen: false })])) })
    await settle()
    await host.rerender({ detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

    // A refetch of the detail drops and restores it, re-running the effect on the same uid.
    await host.rerender({ detailLoaded: false })
    await host.rerender({ detailLoaded: true })

    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
  })

  it('does not fire after Mark as unread on a message that opened already read', async () => {
    // The arming is consumed by the opening even though nothing was written, so a message the
    // user opens read and then marks unread stays unread. Looking the cache up first and
    // returning before arming would flip it straight back.
    seedPage([summary(2, { seen: true })])

    const host = await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })
    expect(mocks.setMessageFlags).not.toHaveBeenCalled()

    await act(async () => { client.setQueryData(pagesKey, pageOf([summary(2, { seen: false })])) })
    await host.rerender({ detailLoaded: false })
    await host.rerender({ detailLoaded: true })

    expect(mocks.setMessageFlags).not.toHaveBeenCalled()
  })

  it('re-arms when the uid goes through null and back to the same message', async () => {
    // Right/bottom pane: the reader stays mounted at uid null while the user switches folders.
    seedPage([summary(2)])

    const host = await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

    await host.rerender({ uid: null, detailLoaded: false })
    await act(async () => { client.setQueryData(pagesKey, pageOf([summary(2, { seen: false })])) })
    await host.rerender({ uid: 2, detailLoaded: true })

    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)
  })

  it('fires on a deep link with no cached summary', async () => {
    expect(findCachedSummary(client, 'primary', 'INBOX', 42)).toBeUndefined()

    await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })

    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [42], 'seen', true, { accountId: 'primary' })
  })

  // A deep link races the listing against the detail. When the detail wins, the STORE goes out
  // against no cache at all, and the listing then lands carrying the server's pre-STORE flags —
  // the row would read unread until the next poll tick.
  describe('a listing landing after the STORE', () => {
    it('marks again when it still says unread', async () => {
      await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })
      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

      await act(async () => { seedPage([summary(42), summary(43)]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)
      expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [42], 'seen', true, { accountId: 'primary' })
      expect(findCachedSummary(client, 'primary', 'INBOX', 42)?.seen).toBe(true)
      // The row that was never ours is left exactly as the server sent it.
      expect(findCachedSummary(client, 'primary', 'INBOX', 43)?.seen).toBe(false)
    })

    it('marks again when it lands as a stream block', async () => {
      await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })

      await act(async () => { seedStream([summary(1)], [summary(42)]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)
      expect(findCachedSummary(client, 'primary', 'INBOX', 42)?.seen).toBe(true)
    })

    it('leaves a listing that already says read alone', async () => {
      await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })

      await act(async () => { seedPage([summary(42, { seen: true })]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
    })

    // The listing is a snapshot of a folder this message may not even be in — a cross-folder
    // search result opens the reader on another folder entirely. One look, then it stops.
    it('reconciles once and stops watching the cache', async () => {
      await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })

      await act(async () => { seedPage([summary(1)]) })
      await settle()
      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

      // The uid turning up in a later refetch is not a second opening.
      await act(async () => { seedPage([summary(1), summary(42)]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
    })

    // Marking it unread inside that window is an explicit decision about the same message; the
    // reconcile must not undo it when the pre-STORE listing arrives and agrees with the user.
    it('gives way to a Mark as unread issued while the listing was in flight', async () => {
      render(<><Host folderPath="INBOX" uid={42} detailLoaded /><Toggle /></>, { wrapper })
      await settle()
      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

      await markUnread()
      await act(async () => { seedPage([summary(42)]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [42], 'seen', false, { accountId: 'primary' })
      expect(findCachedSummary(client, 'primary', 'INBOX', 42)?.seen).toBe(false)
    })

    // The same write, one opening earlier: mutations sit in the cache for gcTime, so a guard that
    // only asks whether one exists would leave this row unread until the next poll tick.
    it('ignores a Mark as unread issued before it armed', async () => {
      render(<Toggle />, { wrapper })
      await markUnread()
      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
      // The clock has to move between the two, as it does between two openings.
      await act(async () => { await new Promise(resolve => setTimeout(resolve, 5)) })

      await renderHost({ folderPath: 'INBOX', uid: 42, detailLoaded: true })
      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)

      await act(async () => { seedPage([summary(42)]) })
      await settle()

      expect(mocks.setMessageFlags).toHaveBeenCalledTimes(3)
      expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [42], 'seen', true, { accountId: 'primary' })
      expect(findCachedSummary(client, 'primary', 'INBOX', 42)?.seen).toBe(true)
    })
  })

  it('re-arms when the uid changes', async () => {
    seedPage([summary(1), summary(2)])

    const host = await renderHost({ folderPath: 'INBOX', uid: 1, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

    await host.rerender({ uid: 2, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)
    expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [2], 'seen', true, { accountId: 'primary' })

    // Returning to a message opened earlier this session re-arms: marked unread meanwhile, it
    // is marked read again.
    await act(async () => {
      client.setQueryData(pagesKey, pageOf([summary(1), summary(2, { seen: true })]))
    })
    await host.rerender({ uid: 1, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(3)
    expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [1], 'seen', true, { accountId: 'primary' })
  })

  it('stays silent on failure', async () => {
    seedPage([summary(2)])
    mocks.setMessageFlags.mockRejectedValue(new Error('boom'))

    await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: true })
    await settle()

    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)
    // The rejection is absorbed: the tree survives and the cache is back to unread, with no
    // error handler wired anywhere in the chain.
    expect(document.body.textContent).toBe('open')
    expect(client.getQueryData<MailFolderPage>(pagesKey)!.messages[0].seen).toBe(false)
  })

  it('does nothing without a folder, a uid or a loaded detail', async () => {
    seedPage([summary(2)])

    await renderHost({ folderPath: null, uid: 2, detailLoaded: true })
    await renderHost({ folderPath: 'INBOX', uid: null, detailLoaded: true })
    await renderHost({ folderPath: 'INBOX', uid: 2, detailLoaded: false })

    expect(mocks.setMessageFlags).not.toHaveBeenCalled()
  })
})

describe('findCachedSummary', () => {
  beforeEach(() => {
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('finds a message in the paged cache', () => {
    seedPage([summary(1), summary(2, { seen: true })])

    expect(findCachedSummary(client, 'primary', 'INBOX', 2)?.seen).toBe(true)
  })

  it('finds a message in a later stream block', () => {
    seedStream([summary(1)], [summary(5, { flagged: true })])

    expect(findCachedSummary(client, 'primary', 'INBOX', 5)?.flagged).toBe(true)
  })

  it('answers undefined for an unknown uid or another folder', () => {
    seedPage([summary(1)])
    seedStream([summary(1)])

    expect(findCachedSummary(client, 'primary', 'INBOX', 999)).toBeUndefined()
    expect(findCachedSummary(client, 'primary', 'Archive', 1)).toBeUndefined()
  })
})
