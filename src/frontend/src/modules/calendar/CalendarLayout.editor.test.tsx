import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { OccurrenceListResponse } from './calendarTypes'
import {
  api, ApiError, detail, floating, installLayoutMocks, user, REPEAT, renderAt,
  UPDATED,
} from './calendarLayoutHarness'
import { pickOption, settle } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

installLayoutMocks()

/** `nextHour()`'s own arithmetic, read back as the `'HH:mm'` clock the Start/End time inputs
    show — the top of the next hour, on the wall clock this machine (and the layout) reads. */
function nextHourClock(): string {
  const now = new Date()
  now.setMinutes(0, 0, 0)
  const next = new Date(now.getTime() + 3_600_000)
  return String(next.getHours()).padStart(2, '0') + ':00'
}

function clockPlusOneHour(clock: string): string {
  const hour = (Number(clock.slice(0, 2)) + 1) % 24
  return String(hour).padStart(2, '0') + ':00'
}

describe('CalendarLayout — sowing and saving the editor', () => {
  it('reads the event and sows the editor from the occurrence in the window', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail())
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await waitFor(() => expect(api.getEvent).toHaveBeenCalledWith('e1'))
    expect(await screen.findByLabelText('Title')).toHaveValue('Dentist')
    expect(screen.getByLabelText('Start date')).toHaveValue('2026-09-16')
    expect(screen.getByLabelText('Start time')).toHaveValue('09:00')
  })

  // The picker never narrows the series behind the user's back: the scope is a question, and the
  // narrow answer carries the instance it was asked about.
  it('asks the scope before saving a recurring event, then writes the narrow one', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Save a recurring event')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'This occurrence only' }))

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({
        scope: 'This', instanceId: '2026-09-16T09:00:00', ifHash: 'h1',
      })))
  })

  // Moving an event to another calendar moves the whole file, so the narrow scopes are refused —
  // and refused visibly, greyed with a reason, rather than by a button that is not drawn.
  it('offers All alone once the calendar has changed', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await pickOption(await screen.findByLabelText('Calendar'), 'Work')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByText('Save a recurring event')
    expect(screen.getByRole('button', { name: 'This occurrence only' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'All occurrences' })).toBeEnabled()
  })

  // An event is recurring when the occurrence opened carries a RECURRENCE-ID, never because the
  // picker has just been set — the series has no other occurrence to reach yet.
  it('saves a repeat just added to a lone event without asking anything', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByLabelText('Repeats'))
    await pickOption(screen.getByLabelText('Unit'), 'month')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All' })))
    expect(api.updateEvent.mock.calls[0]![1].repeat).toMatchObject({ frequency: 'MONTHLY' })
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  it('saves a plain event without asking anything, and remembers its calendar', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue(UPDATED)
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All', ifHash: 'h1' })))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
    expect(localStorage.getItem('calendar.lastUsed')).toBe('a')
  })

  it('says how many invitations went out with the save', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.updateEvent.mockResolvedValue({ scheduling: { sent: 2 } })
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Saved · invitations sent: 2')).toBeInTheDocument()
    expect(screen.queryByText('Event saved')).toBeNull()
  })

  it('says only that it saved when no invitation went out', async () => {
    api.createEvent.mockResolvedValue({ id: 'e2', scheduling: { sent: 0 } })
    renderAt('/calendar/new?view=week&date=2026-09-16&start=2026-09-16T09:00:00.000Z'
      + '&end=2026-09-16T10:00:00.000Z&allDay=0')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Event saved')).toBeInTheDocument()
  })

  // The invitation hook may write the file a second time (SEQUENCE) before the save answers, so
  // the hash the closed editor was sown with is stale: an editor reopened before the event has
  // been read again must wait for that read rather than sow from the version the save replaced.
  it('sows a reopened editor from the version its save wrote, never the one it replaced', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValueOnce(detail())
    let land = () => {}
    api.getEvent.mockImplementation(() => new Promise(resolve => {
      land = () => resolve({ ...detail(), icsHash: 'h2' })
    }))
    api.updateEvent.mockResolvedValue({ scheduling: { sent: 1 } })
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
    expect(api.updateEvent).toHaveBeenLastCalledWith('e1', expect.objectContaining({ ifHash: 'h1' }))

    await router.navigate('/calendar/e1/edit?view=week&date=2026-09-16')
    await settle()
    land()
    await user.click(await screen.findByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledTimes(2))
    expect(api.updateEvent).toHaveBeenLastCalledWith('e1', expect.objectContaining({ ifHash: 'h2' }))
  })

  it('creates an event from the new route', async () => {
    api.createEvent.mockResolvedValue(detail())
    const router = renderAt(
      '/calendar/new?view=week&date=2026-09-16&start=2026-09-16T09:00:00.000Z'
      + '&end=2026-09-16T10:00:00.000Z&allDay=0')

    await user.type(await screen.findByLabelText('Title'), 'Retro')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.createEvent).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'Retro', calendarId: 'a' })))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // An unparsable start/end (a hand-edited link, a stale bookmark) must fall back rather than
  // hand an Invalid Date into the form; and a parsed end at or before that fallback start is not a
  // real duration, so it must not be inherited either.
  it.each([
    ['opens the editor with fallback times when start/end are unparsable', 'y'],
    ['falls back to start + 1h when the parsed end is not after the fallback start',
      '2000-01-01T00:00:00.000Z'],
  ])('%s', async (_name, end) => {
    const expectedStart = nextHourClock()
    renderAt(`/calendar/new?view=week&date=2026-09-16&start=x&end=${end}`)

    expect(await screen.findByLabelText('Title')).toBeInTheDocument()
    expect(screen.getByLabelText('Start time')).toHaveValue(expectedStart)
    expect(screen.getByLabelText('End time')).toHaveValue(clockPlusOneHour(expectedStart))
  })

  // A stale write keeps the form: bouncing back to a grid that kept nothing is how somebody loses
  // what they typed without being told why. The way out is a Reload, not a second Save — a retry
  // carrying a fresher hash would silently overwrite what the other client wrote.
  it('keeps the form standing when the event changed elsewhere, and offers a reload', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockRejectedValue(new ApiError('conflict', 409))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist')

    const fresh = detail({ summary: 'Dentiste' })
    api.getEvent.mockResolvedValue({ ...fresh, icsHash: 'h2' })
    await waitFor(() => expect(api.getEvent.mock.calls.length).toBeGreaterThan(1))
    api.updateEvent.mockResolvedValue(UPDATED)
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    await waitFor(() => expect(screen.getByLabelText('Title')).toHaveValue('Dentiste'))

    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenLastCalledWith('e1',
      expect.objectContaining({ ifHash: 'h2' })))
  })

  // The hash is the version the form claims to have read, so it is frozen at the sowing: the
  // failed write's own invalidation brings a fresher detail back, and a bare retry that picked it
  // up would overwrite the other client's change while telling nobody.
  it('retries a stale write with the hash it was sown from, not a fresher one', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValueOnce(detail())
    api.getEvent.mockResolvedValue({ ...detail(), icsHash: 'h2' })
    api.updateEvent.mockRejectedValueOnce(new ApiError('conflict', 409))
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    await screen.findByText(/changed elsewhere/)
    // The refused write invalidated the event; the detail on screen is now h2.
    await waitFor(() => expect(api.getEvent.mock.calls.length).toBeGreaterThan(1))

    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenLastCalledWith('e1',
      expect.objectContaining({ ifHash: 'h1' })))
  })

  // `occurrenceFound` is recomputed every render, so a window coming back without the instance
  // being edited used to unmount the keyed editor and throw away what was typed.
  it('keeps an open editor through a window that no longer holds its occurrence', async () => {
    api.getOccurrences.mockResolvedValueOnce({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getOccurrences.mockResolvedValue({ occurrences: [] })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.setCalendarVisible.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.type(await screen.findByLabelText('Title'), '!')
    // The sidebar is still mounted behind the dialogue; its write invalidates the whole module.
    await user.click(screen.getByLabelText('Work'))

    await waitFor(() => expect(api.getOccurrences.mock.calls.length).toBeGreaterThan(1))
    await settle()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist!')
  })

  // An event answers faster than a whole window, so without the wait the form is sown once from
  // the master's hours and never corrected — and a narrow save then leaves with no instance.
  it('waits for the window rather than sowing from the master', async () => {
    let release: (value: OccurrenceListResponse) => void = () => {}
    api.getOccurrences.mockReturnValue(new Promise(resolve => { release = resolve }))
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await waitFor(() => expect(api.getEvent).toHaveBeenCalledWith('e1'))
    expect(screen.queryByLabelText('Title')).toBeNull()

    release({ occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')] })
    expect(await screen.findByLabelText('Start date')).toHaveValue('2026-09-16')
    expect(screen.getByLabelText('Start time')).toHaveValue('09:00')
  })

  // The editor reached from a search hit is sown from the occurrence: it used to show the
  // master's hours and save without the instance it was told to spare.
  it('sows the editor from the occurrence a search hit led to', async () => {
    const one = floating('e1', 'Dentist', '2026-09-16T09:00:00')
    api.searchEvents.mockResolvedValue({ occurrences: [one] })
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar?view=week&date=2026-09-16')

    await user.type(
      await screen.findByRole('searchbox', { name: 'Search events' }), 'dentist{Enter}')
    await user.click(await screen.findByRole('button', { name: /Dentist/ }))
    await user.click(await screen.findByRole('button', { name: 'Edit' }))

    expect(await screen.findByLabelText('Start date')).toHaveValue('2026-09-16')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await user.click(await screen.findByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'This', instanceId: '2026-09-16T09:00:00' })))
  })

  // The brief's fallback: one day around the instance, asked for only once the loaded window and
  // the search have both come up empty.
  it('fetches the instance own day when nothing already loaded holds it', async () => {
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=month&date=2026-09-16&instance=2026-09-16T09:00:00')

    await waitFor(() => expect(api.getOccurrences.mock.calls.length).toBeGreaterThan(1))
    const last = api.getOccurrences.mock.calls[api.getOccurrences.mock.calls.length - 1]!
    // A month spans six weeks; the fallback spans a day plus its two beats.
    expect(Date.parse(last[1]) - Date.parse(last[0])).toBeLessThan(4 * 24 * 3600_000)
  })

  // Nothing resolved the instance, so there is no occurrence and no question: the form was sown
  // from the master and the save writes the master back, rather than offering two answers whose
  // instance it has not got.
  it('saves the whole series when nothing resolved the instance', async () => {
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.click(await screen.findByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All' })))
    expect(api.updateEvent.mock.calls[0]![1].instanceId).toBeUndefined()
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  // One sentence, and the layout is where it is composed: the modal is handed a finished string.
  it('asks the scope question once, naming the series', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText(
      '“Dentist” repeats: Every 6 months. Which occurrences should take the change?'))
      .toBeInTheDocument()
  })
})
