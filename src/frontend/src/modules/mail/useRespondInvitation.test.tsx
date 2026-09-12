import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type {
  InvitationResponse, MailFolderNode, MailFolderPage, MailInvitation, MailMessageSummary,
} from './api/mailTypes'
import { mailKeys, useRespondInvitation } from './queries'

const mocks = vi.hoisted(() => ({ respondInvitation: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', to: [], date: '2026-09-12T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  priority: 'normal',
  ...over,
})

const pageOf = (messages: MailMessageSummary[]): MailFolderPage =>
  ({ folderPath: 'INBOX', uidValidity: 1, total: 20, page: 0, pageSize: 50, messages })

const node = (path: string, total: number, unread: number): MailFolderNode => ({
  path, name: path, specialUse: null, selectable: true, subscribed: true,
  total, unread, uidValidity: 1, uidNext: 100, highestModSeq: null, children: [],
})

const pagesKey = mailKeys.messages('primary', 'INBOX', 0, 50)
const streamKey = mailKeys.messageStream('primary', 'INBOX', 100)
const foldersKey = mailKeys.folders('primary')

const invitation = { part: '2', uid: 'u' } as MailInvitation
const answerOf = (trashed: boolean): InvitationResponse =>
  ({ invitation, replySent: true, trashed })

const args = {
  folder: 'INBOX', uid: 7, part: '2', answer: 'Declined' as const,
  language: 'en', timeZone: 'Europe/Brussels',
}

/** uid 7 sits in the page AND in stream block 0 — the row must count once, not twice. */
function seed() {
  client.setQueryData(pagesKey, pageOf([summary(7), summary(8, { seen: true })]))
  client.setQueryData(streamKey, {
    pages: [pageOf([summary(7), summary(9)])], pageParams: [0],
  } satisfies InfiniteData<MailFolderPage>)
  client.setQueryData(foldersKey, [node('INBOX', 20, 5)])
}

const page = () => client.getQueryData<MailFolderPage>(pagesKey)!
const stream = () => client.getQueryData<InfiniteData<MailFolderPage>>(streamKey)!
const inbox = () => client.getQueryData<MailFolderNode[]>(foldersKey)![0]

describe('useRespondInvitation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
  })

  it('a decline the server trashed takes the row out of every list cache and moves the counts', async () => {
    seed()
    mocks.respondInvitation.mockResolvedValue(answerOf(true))
    const { result } = renderHook(() => useRespondInvitation(), { wrapper })

    await act(() => result.current.mutateAsync(args))

    await waitFor(() => expect(page().messages.map(m => m.uid)).toEqual([8]))
    expect(stream().pages[0].messages.map(m => m.uid)).toEqual([9])
    // Counted once across the two caches: one row left the folder, and it was unread.
    expect(inbox().total).toBe(19)
    expect(inbox().unread).toBe(4)
  })

  it('a decline whose mail stayed touches no list cache', async () => {
    seed()
    mocks.respondInvitation.mockResolvedValue(answerOf(false))
    const { result } = renderHook(() => useRespondInvitation(), { wrapper })

    await act(() => result.current.mutateAsync(args))

    expect(page().messages.map(m => m.uid)).toEqual([7, 8])
    expect(stream().pages[0].messages.map(m => m.uid)).toEqual([7, 9])
    expect(inbox().total).toBe(20)
    expect(inbox().unread).toBe(5)
  })
})
