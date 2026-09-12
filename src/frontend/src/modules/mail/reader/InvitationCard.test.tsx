import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { Calendar } from '../../calendar/calendarTypes'
import type { MailInvitation } from '../api/mailTypes'
import InvitationCard from './InvitationCard'

const mocks = vi.hoisted(() => ({
  respondInvitation: vi.fn(),
  getCalendars: vi.fn(),
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
  api: { respondInvitation: mocks.respondInvitation, getCalendars: mocks.getCalendars },
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
    expect(await screen.findByText('Added to your calendar. The reply could not be sent.'))
      .toBeInTheDocument()

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

    expect(await screen.findByText('The answer could not be recorded.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reload the message' })).toBeNull()
  })

  it('the context sentence when the file already carries an answer', async () => {
    renderCard({ ...base, filePartStat: 'ACCEPTED' })

    expect(screen.getByText('The organiser recorded you as having accepted.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    await waitFor(() => expect(mocks.getCalendars).toHaveBeenCalled())
  })

  it('an invitation filed without an answer names its calendar alone', async () => {
    renderCard({ ...base, addressedTo: undefined, inCalendar: 'Current', calendarId: 'c1' })

    expect(await screen.findByText('In your calendar · Personnel')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Decline' })).toBeNull()
  })
})
