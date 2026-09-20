import { describe, it, expect, vi, beforeEach } from 'vitest'
import { StrictMode } from 'react'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { Calendar } from '../../calendar/calendarTypes'
import type { MailInvitation, ReplyStatus } from '../api/mailTypes'
import { useMessage } from '../queries'
import InvitationCard from './InvitationCard'

const mocks = vi.hoisted(() => ({
  respondInvitation: vi.fn(),
  applyInvitationReply: vi.fn(),
  getCalendars: vi.fn(),
  getMailMessage: vi.fn(),
  // The class queries.ts imports from the mocked module, so `instanceof ApiError` holds against
  // what these tests throw. A locally-declared twin fails that check silently.
  ApiError: class ApiError extends Error {
    status: number
    code: string | null
    constructor(message: string, status: number, code: string | null = null) {
      super(message)
      this.name = 'ApiError'
      this.status = status
      this.code = code
    }
  },
}))

vi.mock('../../../api.js', () => ({
  api: {
    respondInvitation: mocks.respondInvitation,
    applyInvitationReply: mocks.applyInvitationReply,
    getCalendars: mocks.getCalendars,
    getMailMessage: mocks.getMailMessage,
  },
  ApiError: mocks.ApiError,
}))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: 'primary', email: 'alice@weesky.be' },
    activeAccountId: 'primary',
    identity: { displayName: 'Alice', email: 'alice@weesky.be' },
  }),
}))

const base: MailInvitation = {
  method: 'Request', uid: 'u', sequence: 0, isAllDay: false, repeats: false,
  attendees: [{ email: 'alice@weesky.be', name: 'Alice Martin' }],
  organizer: { email: 'marc@x.be', name: 'Marc Dupont' },
  location: 'Chez Marc',
  addressedTo: 'alice@weesky.be', filePartStat: 'NEEDS-ACTION', inCalendar: 'Absent',
  occurrenceOnly: false, part: '2', unreadable: false,
  summary: 'Dîner chez Marc', start: '2026-10-10T17:30:00Z', end: '2026-10-10T20:30:00Z',
}

function calendar(partial: Partial<Calendar>): Calendar {
  return {
    id: 'c1', davName: 'default', displayName: 'Personnel', description: '', color: '#3450a3',
    order: 0, timeZone: 'Europe/Brussels', isVisible: true, isDefault: true, ...partial,
  }
}

const calendars = [calendar({})]
const twoCalendars = [...calendars, calendar({ id: 'c2', davName: 'work', displayName: 'Travail', isDefault: false })]

const onTrashed = vi.fn()

function renderCard(invitation: MailInvitation = base) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <InvitationCard invitation={invitation} folderPath="INBOX" uid={7} onTrashed={onTrashed} />
    </QueryClientProvider>,
  )
  return { client, view }
}

const answered = (partStat: string, presence: MailInvitation['inCalendar'] = 'Current') =>
  ({ ...base, inCalendar: presence, savedPartStat: partStat, calendarId: 'c1' })

// A guest's REPLY: no address of the user's is invited, and the file names the guest alone.
const request: MailInvitation = { ...base }
delete request.addressedTo
delete request.filePartStat
const reply = (status: ReplyStatus, applied = false, partStat = 'ACCEPTED'): MailInvitation => ({
  ...request, method: 'Reply', attendees: [{ email: 'marc@example.org', name: 'Marc' }],
  inCalendar: status === 'UnknownUid' ? 'Absent' : 'Current', calendarId: 'c1',
  reply: { email: 'marc@example.org', name: 'Marc', partStat, status, applied },
})

describe('InvitationCard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getCalendars.mockResolvedValue({ calendars })
  })

  it('state 1: title, badge, four rows, three buttons', async () => {
    renderCard()

    expect(screen.getByText('Dîner chez Marc')).toBeInTheDocument()
    expect(screen.getByText('Invitation')).toBeInTheDocument()
    expect(screen.getByText('When')).toBeInTheDocument()
    expect(screen.getByText('Where')).toBeInTheDocument()
    expect(screen.getByText('Chez Marc')).toBeInTheDocument()
    expect(screen.getByText('Organiser')).toBeInTheDocument()
    expect(screen.getByText('Marc Dupont')).toBeInTheDocument()
    expect(screen.getByText('Attendees')).toBeInTheDocument()
    expect(screen.getByText('Alice Martin')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Tentative' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Decline' })).toBeInTheDocument()
    // One calendar is no choice at all.
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
    expect(screen.queryByLabelText('Calendar')).toBeNull()
  })

  it('draws no row the invitation has nothing for', () => {
    renderCard({ ...base, location: undefined, organizer: undefined, attendees: [] })

    expect(screen.getByText('When')).toBeInTheDocument()
    expect(screen.queryByText('Where')).toBeNull()
    expect(screen.queryByText('Organiser')).toBeNull()
    expect(screen.queryByText('Attendees')).toBeNull()
  })

  it('names a repeating event as one, and falls back to the calendar no-title word', () => {
    renderCard({ ...base, summary: undefined, repeats: true })

    expect(screen.getByText('(No title)')).toBeInTheDocument()
    expect(screen.getByText(/Repeats/)).toBeInTheDocument()
  })

  it('shows the calendar selector only with several calendars, and sends the chosen one', async () => {
    mocks.getCalendars.mockResolvedValue({ calendars: twoCalendars })
    mocks.respondInvitation.mockResolvedValue({
      invitation: { ...answered('ACCEPTED'), calendarId: 'c2' }, replySent: true, trashed: false,
    })
    renderCard()

    // The editor's own picker: a menu, not a native select.
    fireEvent.click(await screen.findByLabelText('Calendar'))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Travail' }))
    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))

    await waitFor(() => expect(mocks.respondInvitation).toHaveBeenCalledWith(
      expect.objectContaining({
        folder: 'INBOX', uid: 7, part: '2', answer: 'Accepted', calendarId: 'c2', language: 'en',
      }),
      expect.anything()))
  })

  it('a click disables the buttons, then redraws state 2 from the answer', async () => {
    let resolve!: (value: unknown) => void
    mocks.respondInvitation.mockReturnValue(new Promise(r => { resolve = r }))
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
    expect(screen.getByRole('button', { name: 'Accept' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Decline' })).toBeDisabled()

    resolve({ invitation: answered('ACCEPTED'), replySent: true, trashed: false })

    // The calendar's name is bold, so the sentence spans two nodes: match the whole span's text.
    await waitFor(() => expect(document.querySelector('.invitation-card-answer')?.textContent)
      .toBe('✓You accepted · in Personnel'))
    // The two other answers stay, discreet.
    expect(screen.getByRole('button', { name: 'Tentative' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Decline' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull()
  })

  it('a decline that trashed the mail calls onTrashed; one that did not stays', async () => {
    mocks.respondInvitation.mockResolvedValue({
      invitation: { ...base, inCalendar: 'Absent' }, replySent: true, trashed: true,
    })
    const { view } = renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Decline' }))
    await waitFor(() => expect(onTrashed).toHaveBeenCalledTimes(1))
    view.unmount()

    onTrashed.mockClear()
    mocks.respondInvitation.mockResolvedValue({
      invitation: { ...base, inCalendar: 'Absent' }, replySent: true, trashed: false,
    })
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Decline' }))
    await waitFor(() => expect(mocks.respondInvitation).toHaveBeenCalledTimes(2))
    expect(onTrashed).not.toHaveBeenCalled()
    // The card is back on its three buttons, which on its own says nothing about what happened.
    expect(await screen.findByText('Declined. The message stays here.')).toBeInTheDocument()
  })

  it('a refusal the server named is said in the reader\'s own words', async () => {
    mocks.respondInvitation.mockRejectedValue(
      new mocks.ApiError('invitation_stale', 409, 'invitation_stale'))
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))

    expect(await screen.findByText(
      'Your calendar holds a newer version of this event. Reload the message and answer again.'))
      .toBeInTheDocument()
  })

  it('state 3: a forwarded invitation offers Add to calendar alone', async () => {
    renderCard({ ...base, addressedTo: undefined, filePartStat: undefined })

    expect(screen.getByText(/This invitation was not addressed to you/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to calendar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Decline' })).toBeNull()
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
  })

  it('state 4: an update shows the three answers, the previous one pressed', async () => {
    renderCard(answered('ACCEPTED', 'Outdated'))

    expect(screen.getByText('Update')).toBeInTheDocument()
    expect(screen.getByText(/The organiser changed this event/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Accept' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Tentative' })).not.toHaveAttribute('aria-pressed')
    expect(screen.getByRole('button', { name: 'Decline' })).not.toHaveAttribute('aria-pressed')
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
  })

  it('state 5: a cancellation offers Remove from my calendar', async () => {
    renderCard({ ...base, method: 'Cancel', inCalendar: 'Cancelled', calendarId: 'c1' })

    expect(screen.getByText('Cancelled')).toBeInTheDocument()
    expect(screen.getByText('The organiser cancelled this event.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove from my calendar' })).toBeInTheDocument()
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
  })

  it('state 6: each dead end states why, and offers nothing', async () => {
    const cases: [MailInvitation, string][] = [
      [{ ...base, occurrenceOnly: true }, 'This invitation only concerns one date of the series. Edit the event in your calendar.'],
      [{ ...base, inCalendar: 'Newer' }, 'Your calendar holds a newer version of this event.'],
      [{ ...base, method: 'Cancel', inCalendar: 'Absent' }, 'This event is not in your calendar.'],
    ]

    for (const [invitation, sentence] of cases) {
      const { view } = renderCard(invitation)
      expect(screen.getByText(sentence)).toBeInTheDocument()
      expect(screen.queryByRole('button')).toBeNull()
      await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
      view.unmount()
    }
  })

  it('state 7: an unreadable block states the fact and nothing else', () => {
    renderCard({ ...base, unreadable: true, reason: 'not an invitation' })

    expect(screen.getByText('Unreadable invitation')).toBeInTheDocument()
    expect(screen.getByText(/It stays available as an attachment/)).toBeInTheDocument()
    expect(screen.queryByText('When')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('a send failure shows Resend, which replays the same answer', async () => {
    mocks.respondInvitation.mockResolvedValueOnce({
      invitation: answered('ACCEPTED'), replySent: false, replyError: 'smtp', trashed: false,
    })
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
    const message = await screen.findByText('Added to your calendar. The reply could not be sent.')
    expect(message).toBeInTheDocument()
    expect(message.closest('p')).toHaveAttribute('role', 'alert')

    mocks.respondInvitation.mockResolvedValueOnce({
      invitation: answered('ACCEPTED'), replySent: true, trashed: false,
    })
    fireEvent.click(screen.getByRole('button', { name: 'Resend' }))

    await waitFor(() => expect(mocks.respondInvitation).toHaveBeenLastCalledWith(
      expect.objectContaining({ answer: 'Accepted' }), expect.anything()))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Resend' })).toBeNull())
  })

  it('a declined answer whose reply failed says so without claiming a calendar entry', async () => {
    mocks.respondInvitation.mockResolvedValue({
      invitation: { ...base, inCalendar: 'Absent' }, replySent: false, replyError: 'smtp', trashed: false,
    })
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Decline' }))

    expect(await screen.findByText('The reply could not be sent.')).toBeInTheDocument()
    expect(onTrashed).not.toHaveBeenCalled()
  })

  // The answer changed what the calendar holds: its grid, sidebar, open event and searches are
  // all read from one root key, and every one of them is stale the moment the write lands.
  it('an answer recorded refreshes the message and the whole calendar', async () => {
    mocks.respondInvitation.mockResolvedValue({ invitation: answered('TENTATIVE'), replySent: true, trashed: false })
    const { client } = renderCard()
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    fireEvent.click(screen.getByRole('button', { name: 'Tentative' }))

    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['calendar', 'primary'] }))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['mail', 'primary', 'message', 'INBOX', 7] })
  })

  it('a 404 offers to reload the message, which refreshes its query', async () => {
    mocks.respondInvitation.mockRejectedValue(new mocks.ApiError('Message not found', 404, null))
    const { client } = renderCard()
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
    const reload = await screen.findByRole('button', { name: 'Reload the message' })
    fireEvent.click(reload)

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['mail', 'primary', 'message', 'INBOX', 7] })
  })

  it('a refusal that is not a 404 states the failure and offers no reload', async () => {
    mocks.respondInvitation.mockRejectedValue(new mocks.ApiError('boom', 502, null))
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))

    const message = await screen.findByText('The answer could not be recorded.')
    expect(message).toBeInTheDocument()
    expect(message.closest('p')).toHaveAttribute('role', 'alert')
    expect(screen.queryByRole('button', { name: 'Reload the message' })).toBeNull()
  })

  it('the context sentence when the file already carries an answer', async () => {
    renderCard({ ...base, filePartStat: 'ACCEPTED' })

    expect(screen.getByText('The organiser recorded you as having accepted.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
  })

  it('a reply applies itself once, then says what the guest answered', async () => {
    let resolve!: (value: unknown) => void
    mocks.applyInvitationReply.mockReturnValue(new Promise(r => { resolve = r }))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    // StrictMode mounts twice in development: the one call has to survive that too.
    const card = (invitation: MailInvitation) => (
      <StrictMode>
        <QueryClientProvider client={client}>
          <InvitationCard invitation={invitation} folderPath="INBOX" uid={7} onTrashed={onTrashed} />
        </QueryClientProvider>
      </StrictMode>
    )
    const view = render(card(reply('Applicable')))

    // Until the calendar has taken it, the answer is said without the check that claims it.
    expect(screen.getByText('Marc accepted')).toBeInTheDocument()
    expect(document.querySelector('.invitation-card-check')).toBeNull()
    resolve({ invitation: reply('Applicable', true), applied: true })
    await waitFor(() => expect(document.querySelector('.invitation-card-check')).not.toBeNull())
    expect(mocks.applyInvitationReply).toHaveBeenCalledWith({ folder: 'INBOX', uid: 7, part: '2' }, expect.anything())

    // The reader's periodic refresh hands the card the same block, then a changed one.
    view.rerender(card(reply('Applicable')))
    view.rerender(card({ ...reply('Applicable', false, 'DECLINED'), sequence: 1 }))
    await new Promise(r => setTimeout(r, 0))
    expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(1)
    expect(screen.getByText('Marc accepted')).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByLabelText('Reply')).toBeInTheDocument()
  })

  it('an already applied reply calls nothing', async () => {
    renderCard(reply('Applicable', true, 'DECLINED'))
    expect(await screen.findByText('Marc declined')).toBeInTheDocument()
    expect(document.querySelector('.invitation-card-check')).not.toBeNull()
    expect(mocks.applyInvitationReply).not.toHaveBeenCalled()
  })

  it('the attendees row names the guest who answered, not the delegate the reply also carries', () => {
    renderCard({ ...reply('Applicable', true), attendees: [{ email: 'marc@example.org', name: 'Marc' }, { email: 'zoe@example.org', name: 'Zoé' }] })
    expect(screen.getByText('Marc')).toBeInTheDocument()
    expect(screen.queryByText(/Zoé/)).toBeNull()
  })

  it.each([
    ['Stale', 'TENTATIVE', 'Marc answered maybe · reply to an earlier version'],
    ['Superseded', 'TENTATIVE', 'Marc answered maybe · a later reply is already in the calendar'],
    ['UnknownUid', 'TENTATIVE', 'This event no longer exists'],
    ['NotOwner', 'TENTATIVE', 'This event was not invited from the webmail'],
    ['UnknownAttendee', 'TENTATIVE', 'Marc is not on the guest list'],
    ['OccurrenceOnly', 'TENTATIVE', 'Reply for a single date of the series, not carried into the calendar'],
    ['UnsupportedAnswer', 'DELEGATED', 'The reply from Marc cannot be carried into the calendar'],
  ] as const)('%s is read only', async (status, partStat, sentence) => {
    renderCard(reply(status, false, partStat))
    expect(await screen.findByText(sentence)).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
    expect(mocks.applyInvitationReply).not.toHaveBeenCalled()
  })

  it('a reply the calendar refused offers to retry', async () => {
    mocks.applyInvitationReply
      .mockResolvedValueOnce({ invitation: reply('Applicable'), applied: false, applyError: 'calendar_conflict' })
      .mockResolvedValueOnce({ invitation: reply('Applicable', true), applied: true })
    renderCard(reply('Applicable'))

    expect(await screen.findByText('Reply not recorded in the calendar.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(document.querySelector('.invitation-card-check')).not.toBeNull())
    expect(screen.getByText('Marc accepted')).toBeInTheDocument()
    expect(screen.queryByText('Reply not recorded in the calendar.')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull()
    expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(2)
  })

  it('a reply that could not reach the server offers to retry', async () => {
    mocks.applyInvitationReply.mockRejectedValueOnce(new mocks.ApiError('boom', 502, null))
    renderCard(reply('Applicable'))

    expect(await screen.findByText('Reply not recorded in the calendar.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  // The card beside the reader's own query on its message, as MessageReader mounts them: Reload
  // refetches that query, and only a refetch that answered may redraw the card.
  async function renderWithMessage(invitation: MailInvitation) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    function MessageQuery() { useMessage('INBOX', 7); return null }
    render(
      <QueryClientProvider client={client}>
        <MessageQuery />
        <InvitationCard invitation={invitation} folderPath="INBOX" uid={7} onTrashed={onTrashed} />
      </QueryClientProvider>,
    )
    await waitFor(() => expect(client.getQueryState(['mail', 'primary', 'message', 'INBOX', 7])?.status).toBe('success'))
    return client
  }

  // A 400 says the reply no longer applies: asking again would earn the same refusal, and the
  // block on screen is what is out of date. Reload redraws the card from the message as it now reads.
  it.each(['reply_not_applicable', 'reply_not_a_reply'])('%s offers Reload, which redraws the card from the message', async code => {
    mocks.applyInvitationReply.mockRejectedValueOnce(new mocks.ApiError(code, 400, code))
    mocks.getMailMessage
      .mockResolvedValueOnce({ uid: 7, invitation: reply('Applicable') })
      .mockResolvedValueOnce({ uid: 7, invitation: reply('Superseded') })
    await renderWithMessage(reply('Applicable'))

    expect(await screen.findByText('This reply no longer applies to the event in your calendar.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Reload the message' }))

    expect(await screen.findByText('Marc accepted · a later reply is already in the calendar')).toBeInTheDocument()
    expect(screen.queryByText('This reply no longer applies to the event in your calendar.')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
    expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(1)
  })

  it('a reload whose block still applies carries it into the calendar', async () => {
    mocks.applyInvitationReply
      .mockRejectedValueOnce(new mocks.ApiError('reply_not_applicable', 400, 'reply_not_applicable'))
      .mockResolvedValueOnce({ invitation: reply('Applicable', true), applied: true })
    mocks.getMailMessage.mockResolvedValue({ uid: 7, invitation: reply('Applicable') })
    await renderWithMessage(reply('Applicable'))

    fireEvent.click(await screen.findByRole('button', { name: 'Reload the message' }))

    await waitFor(() => expect(document.querySelector('.invitation-card-check')).not.toBeNull())
    expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(2)
  })

  // The cache still holds the block the refetch failed to confirm: nothing is redrawn from it, and
  // above all no write is asked for on its word.
  it('a reload whose refetch fails keeps the card as it was and asks for nothing', async () => {
    mocks.applyInvitationReply.mockRejectedValueOnce(
      new mocks.ApiError('reply_not_applicable', 400, 'reply_not_applicable'))
    mocks.getMailMessage
      .mockResolvedValueOnce({ uid: 7, invitation: reply('Applicable') })
      .mockRejectedValueOnce(new mocks.ApiError('boom', 502, null))
    await renderWithMessage(reply('Applicable'))

    fireEvent.click(await screen.findByRole('button', { name: 'Reload the message' }))

    await waitFor(() => expect(mocks.getMailMessage).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reload the message' })).toBeEnabled())
    expect(screen.getByText('This reply no longer applies to the event in your calendar.')).toBeInTheDocument()
    expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(1)
  })

  it('Reload is disabled while it runs, so a double click reloads once', async () => {
    mocks.applyInvitationReply.mockRejectedValueOnce(
      new mocks.ApiError('reply_not_applicable', 400, 'reply_not_applicable'))
    mocks.getMailMessage
      .mockResolvedValueOnce({ uid: 7, invitation: reply('Applicable') })
      .mockReturnValueOnce(new Promise(() => {}))
    await renderWithMessage(reply('Applicable'))

    const reload = await screen.findByRole('button', { name: 'Reload the message' })
    fireEvent.click(reload)
    fireEvent.click(reload)

    expect(reload).toBeDisabled()
    expect(mocks.getMailMessage).toHaveBeenCalledTimes(2)
  })

  it('a guest with no name is named by the address, in the sentence and on the Attendees row', async () => {
    const nameless: MailInvitation = {
      ...reply('Applicable', true), attendees: [{ email: 'marc@example.org' }],
      reply: { email: 'marc@example.org', partStat: 'ACCEPTED', status: 'Applicable', applied: true },
    }
    renderCard(nameless)

    expect(await screen.findByText('marc@example.org accepted')).toBeInTheDocument()
    expect(screen.getByText('marc@example.org')).toBeInTheDocument()
  })

  // The calendar took the answer; there is no address to mail it to, and no retry would change that.
  it.each([
    ['Accept', 'Added to your calendar. The invitation gives no address a reply can be sent to.'],
    ['Decline', 'Declined. The invitation gives no address a reply can be sent to.'],
  ])('%s on an invitation with no address to reply to offers no Resend', async (button, sentence) => {
    mocks.respondInvitation.mockResolvedValue({
      invitation: button === 'Accept' ? answered('ACCEPTED') : { ...base, inCalendar: 'Absent' },
      replySent: false, replyError: 'invitation_no_organizer', trashed: false,
    })
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: button }))

    expect(await screen.findByText(sentence)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Resend' })).toBeNull()
    expect(screen.queryByText(/could not be sent/)).toBeNull()
  })

  // The UID holds a character no reply may carry: recorded, and no retry would change that either.
  it.each([
    ['Accept', 'Added to your calendar. This invitation does not allow a reply to be sent.'],
    ['Decline', 'Declined. This invitation does not allow a reply to be sent.'],
  ])('%s on an invitation whose UID no reply can carry offers no Resend', async (button, sentence) => {
    mocks.respondInvitation.mockResolvedValue({
      invitation: button === 'Accept' ? answered('ACCEPTED') : { ...base, inCalendar: 'Absent' },
      replySent: false, replyError: 'invitation_uid_unwritable', trashed: false,
    })
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: button }))

    expect(await screen.findByText(sentence)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Resend' })).toBeNull()
    expect(screen.queryByText(/could not be sent/)).toBeNull()
  })

  it('an invitation filed without an answer names its calendar alone', async () => {
    renderCard({ ...base, addressedTo: undefined, inCalendar: 'Current', calendarId: 'c1' })

    expect(await screen.findByText('In your calendar · Personnel')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Decline' })).toBeNull()
  })
})
