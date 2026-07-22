import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import { StrictMode, type ReactNode } from 'react'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { mailKeys } from '../queries'
import { settle } from '../../../test-utils'
import { findCachedSummary, useMarkSeenOnOpen } from './useMarkSeenOnOpen'

const mocks = vi.hoisted(() => ({ setMessageFlags: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
/** What main.tsx renders into. Its double-invoke of mount effects is the one route to a
    duplicate fire that production actually takes, so every test here runs under it. */
function wrapper({ children }: { children: ReactNode }) {
  return <StrictMode><QueryClientProvider client={client}>{children}</QueryClientProvider></StrictMode>
}

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
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
    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'seen', true)

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

    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [42], 'seen', true)
  })

  it('re-arms when the uid changes', async () => {
    seedPage([summary(1), summary(2)])

    const host = await renderHost({ folderPath: 'INBOX', uid: 1, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(1)

    await host.rerender({ uid: 2, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(2)
    expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [2], 'seen', true)

    // Returning to a message opened earlier this session re-arms: marked unread meanwhile, it
    // is marked read again.
    await act(async () => {
      client.setQueryData(pagesKey, pageOf([summary(1), summary(2, { seen: true })]))
    })
    await host.rerender({ uid: 1, detailLoaded: true })
    expect(mocks.setMessageFlags).toHaveBeenCalledTimes(3)
    expect(mocks.setMessageFlags).toHaveBeenLastCalledWith('INBOX', [1], 'seen', true)
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
