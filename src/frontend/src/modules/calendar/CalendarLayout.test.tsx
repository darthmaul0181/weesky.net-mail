import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CalendarLayout from './CalendarLayout'
import type { Calendar } from './calendarTypes'
import {
  firePointer, installPointerEvents, mockViewport, resetViewport, settle,
} from '../../test-utils'

afterEach(resetViewport)

vi.mock('../../api.js', () => ({
  api: {
    getCalendars: vi.fn(), createCalendar: vi.fn(), updateCalendar: vi.fn(),
    setCalendarVisible: vi.fn(), deleteCalendar: vi.fn(), exportCalendar: vi.fn(),
    importCalendar: vi.fn(), importCalendarAsNew: vi.fn(),
    getOccurrences: vi.fn(), searchEvents: vi.fn(), getEvent: vi.fn(),
    createEvent: vi.fn(), updateEvent: vi.fn(), deleteEvent: vi.fn(),
  },
  // The very class the layout imports from the mocked module, so `instanceof ApiError` holds
  // against what these tests throw; a locally-declared twin fails that check.
  ApiError: class ApiError extends Error {
    status: number
    constructor(message: string, status: number) {
      super(message)
      this.name = 'ApiError'
      this.status = status
    }
  },
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))
vi.mock('../../lib/downloadBlob', () => ({ downloadBlob: vi.fn() }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'getCalendars' | 'createCalendar' | 'updateCalendar' | 'setCalendarVisible'
    | 'deleteCalendar' | 'exportCalendar' | 'importCalendar' | 'importCalendarAsNew'
    | 'getOccurrences' | 'searchEvents' | 'getEvent' | 'createEvent' | 'updateEvent'
    | 'deleteEvent', ReturnType<typeof vi.fn>>
}
const { ApiError } = await import('../../api.js') as unknown as {
  ApiError: new (message: string, status: number) => Error
}
const { downloadBlob } = await import('../../lib/downloadBlob') as unknown as {
  downloadBlob: ReturnType<typeof vi.fn>
}

const BROWSER_TZ = Intl.DateTimeFormat().resolvedOptions().timeZone

function calendar(id: string, displayName: string, isDefault = false): Calendar {
  return {
    id, davName: id, displayName, description: '', color: '#3b82c4', order: 0,
    timeZone: BROWSER_TZ, isVisible: true, isDefault,
  }
}

const CALENDARS = [calendar('a', 'Personal', true), calendar('b', 'Work')]

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
  api.getCalendars.mockResolvedValue({ calendars: CALENDARS })
  api.getOccurrences.mockResolvedValue({ occurrences: [] })
  api.searchEvents.mockResolvedValue({ occurrences: [] })
})

function occurrence(eventId: string, summary: string) {
  return {
    eventId, calendarId: 'a', uid: eventId, instanceId: '', isOverride: false, isAllDay: false,
    isFloating: false, transparency: 'OPAQUE', hasAlarm: false, summary,
    startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
  }
}

/** Floating on purpose: a wall clock is read as it is written, so the seeded hour is the same
    number on a Brussels laptop and on a UTC runner. */
function floating(eventId: string, summary: string, instanceId = '') {
  return {
    ...occurrence(eventId, summary), isFloating: true, instanceId,
    startUtc: undefined, endUtc: undefined,
    localStart: '2026-09-16T09:00:00', localEnd: '2026-09-16T10:00:00',
  }
}

const REPEAT = { frequency: 'MONTHLY', interval: 6, byDay: [] as string[], end: 'Never' }

function detail(fields: Record<string, unknown> = {}) {
  return {
    id: 'e1', calendarId: 'a', uid: 'u1', icsHash: 'h1',
    fields: {
      calendarId: 'a', summary: 'Dentist', isAllDay: false,
      // Deliberately another day: what the editor shows must come from the occurrence.
      start: '2026-01-05T14:00:00', end: '2026-01-05T15:00:00', timeZone: BROWSER_TZ,
      reminderMinutesBefore: [], availability: 'Busy', visibility: 'Default', ...fields,
    },
    attendees: [], repeatIsExact: true, foreignAlarms: [],
  }
}

const routes = [
  { path: '/calendar', element: <CalendarLayout /> },
  { path: '/calendar/new', element: <CalendarLayout /> },
  { path: '/calendar/:id/edit', element: <CalendarLayout /> },
]

function renderAt(path = '/calendar') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

const params = (router: ReturnType<typeof renderAt>) =>
  new URLSearchParams(router.state.location.search)

/** Today as the browser's own zone reads it — what the layout falls back to. */
function today() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: BROWSER_TZ }).format(new Date())
}

describe('CalendarLayout', () => {
  it('names the view and the day it opens on', async () => {
    const router = renderAt()
    await waitFor(() => expect(params(router).get('view')).toBe('week'))
    expect(params(router).get('date')).toBe(today())
  })

  // Seven columns in 360px is six unreadable ones and a scroll: a phone reads a week as days.
  it('falls back from the week view on a phone', async () => {
    mockViewport('phone')
    const router = renderAt('/calendar?view=week&date=2026-09-14')
    await waitFor(() => expect(params(router).get('view')).toBe('day'))
    expect(params(router).get('date')).toBe('2026-09-14')
  })

  it('steps a week at a time from the chevrons', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-14')
    await screen.findByRole('button', { name: 'Next period' })
    await userEvent.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-21'))
    await userEvent.click(screen.getByRole('button', { name: 'Previous period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-14'))
  })

  // Same day of the month, clamped: 31 January plus a month is the last day of February, never
  // the third of March.
  it('steps a month at a time without overshooting a short one', async () => {
    const router = renderAt('/calendar?view=month&date=2026-01-31')
    await screen.findByRole('button', { name: 'Next period' })
    await userEvent.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-02-28'))
  })

  it('comes back to today', async () => {
    const router = renderAt('/calendar?view=week&date=2020-01-06')
    await userEvent.click(await screen.findByRole('button', { name: 'Today' }))
    await waitFor(() => expect(params(router).get('date')).toBe(today()))
  })

  // The zone decides which day a floating instance falls on, so it travels with every request.
  it('asks for the calendars in the zone the browser is in', async () => {
    renderAt()
    await waitFor(() => expect(api.getCalendars).toHaveBeenCalledWith(BROWSER_TZ))
  })

  it('remembers the view that was chosen', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-14')
    await userEvent.click(await screen.findByRole('radio', { name: 'Month' }))
    await waitFor(() => expect(params(router).get('view')).toBe('month'))
    expect(localStorage.getItem('calendar.view')).toBe('month')
  })

  it('opens on the remembered view', async () => {
    localStorage.setItem('calendar.view', 'list')
    const router = renderAt()
    await waitFor(() => expect(params(router).get('view')).toBe('list'))
  })

  // A refused window is the one failure with something to do about it: the band says what the
  // server said and offers the retry rather than leaving an empty grid.
  it('says a refused window out loud and retries it', async () => {
    api.getOccurrences.mockRejectedValueOnce(
      new ApiError('The window holds too many occurrences; narrow it', 400))
    renderAt('/calendar?view=month&date=2026-09-14')

    const band = await screen.findByText('The window holds too many occurrences; narrow it')
    expect(band.closest('.calendar-error')).not.toBeNull()

    api.getOccurrences.mockResolvedValue({ occurrences: [] })
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() =>
      expect(screen.queryByText('The window holds too many occurrences; narrow it')).toBeNull())
  })

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
    api.updateEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Save a recurring event')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'This occurrence only' }))

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

    await userEvent.selectOptions(await screen.findByLabelText('Calendar'), 'b')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByText('Save a recurring event')
    expect(screen.getByRole('button', { name: 'This occurrence only' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'All occurrences' })).toBeEnabled()
  })

  // Décision 8: an event is recurring when the occurrence opened carries a RECURRENCE-ID, never
  // because the picker has just been set — the series has no other occurrence to reach yet.
  it('saves a repeat just added to a lone event without asking anything', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.selectOptions(await screen.findByLabelText('Repeat'), 'monthly')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All' })))
    expect(api.updateEvent.mock.calls[0][1].repeat).toMatchObject({ frequency: 'MONTHLY' })
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  it('saves a plain event without asking anything, and remembers its calendar', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue(null)
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All', ifHash: 'h1' })))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
    expect(localStorage.getItem('calendar.lastUsed')).toBe('a')
  })

  it('creates an event from the new route', async () => {
    api.createEvent.mockResolvedValue(detail())
    const router = renderAt(
      '/calendar/new?view=week&date=2026-09-16&start=2026-09-16T09:00:00.000Z'
      + '&end=2026-09-16T10:00:00.000Z&allDay=0')

    await userEvent.type(await screen.findByLabelText('Title'), 'Retro')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.createEvent).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'Retro', calendarId: 'a' })))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // A stale write keeps the form: bouncing back to a grid that kept nothing is how somebody loses
  // what they typed without being told why. The way out is a Reload, not a second Save — a retry
  // carrying a fresher hash would silently overwrite what the other client wrote.
  it('keeps the form standing when the event changed elsewhere, and offers a reload', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockRejectedValue(new ApiError('conflict', 409))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist')

    const fresh = detail({ summary: 'Dentiste' })
    api.getEvent.mockResolvedValue({ ...fresh, icsHash: 'h2' })
    await waitFor(() => expect(api.getEvent.mock.calls.length).toBeGreaterThan(1))
    api.updateEvent.mockResolvedValue(null)
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))
    await waitFor(() => expect(screen.getByLabelText('Title')).toHaveValue('Dentiste'))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
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
    api.updateEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    await screen.findByText(/changed elsewhere/)
    // The refused write invalidated the event; the detail on screen is now h2.
    await waitFor(() => expect(api.getEvent.mock.calls.length).toBeGreaterThan(1))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
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

    await userEvent.type(await screen.findByLabelText('Title'), '!')
    // The sidebar is still mounted behind the dialogue; its write invalidates the whole module.
    await userEvent.click(screen.getByLabelText('Work'))

    await waitFor(() => expect(api.getOccurrences.mock.calls.length).toBeGreaterThan(1))
    await settle()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist!')
  })

  // An event answers faster than a whole window, so without the wait the form is sown once from
  // the master's hours and never corrected — and a narrow save then leaves with no instance.
  it('waits for the window rather than sowing from the master', async () => {
    let release: (value: { occurrences: unknown[] }) => void = () => {}
    api.getOccurrences.mockReturnValue(new Promise(resolve => { release = resolve }))
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await waitFor(() => expect(api.getEvent).toHaveBeenCalledWith('e1'))
    expect(screen.queryByLabelText('Title')).toBeNull()

    release({ occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')] })
    expect(await screen.findByLabelText('Start date')).toHaveValue('2026-09-16')
    expect(screen.getByLabelText('Start time')).toHaveValue('09:00')
  })

  // Décision 11: the click goes to that day in the *current view*, so the box is emptied on the
  // way — the results used to stay up and go on covering the grid they had just moved.
  it('gives the grid back at the day a search hit sits on', async () => {
    api.searchEvents.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    const router = renderAt('/calendar?view=week&date=2026-08-03')

    await userEvent.type(
      await screen.findByRole('searchbox', { name: 'Search events' }), 'dentist')
    await userEvent.click(await screen.findByRole('button', { name: /Dentist/ }))

    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-16'))
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('')
    expect(document.querySelector('.week-body')).not.toBeNull()
    expect(await screen.findByRole('dialog', { name: 'Dentist' })).toBeInTheDocument()
  })

  // The editor reached from a search hit is sown from the occurrence: it used to show the
  // master's hours and save without the instance it was told to spare.
  it('sows the editor from the occurrence a search hit led to', async () => {
    const one = floating('e1', 'Dentist', '2026-09-16T09:00:00')
    api.searchEvents.mockResolvedValue({ occurrences: [one] })
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(null)
    renderAt('/calendar?view=week&date=2026-09-16')

    await userEvent.type(
      await screen.findByRole('searchbox', { name: 'Search events' }), 'dentist')
    await userEvent.click(await screen.findByRole('button', { name: /Dentist/ }))
    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))

    expect(await screen.findByLabelText('Start date')).toHaveValue('2026-09-16')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(await screen.findByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'This', instanceId: '2026-09-16T09:00:00' })))
  })

  // The brief's fallback: one day around the instance, asked for only once the loaded window and
  // the search have both come up empty.
  it('fetches the instance own day when nothing already loaded holds it', async () => {
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=month&date=2026-09-16&instance=2026-09-16T09:00:00')

    await waitFor(() => expect(api.getOccurrences.mock.calls.length).toBeGreaterThan(1))
    const last = api.getOccurrences.mock.calls[api.getOccurrences.mock.calls.length - 1]
    // A month spans six weeks; the fallback spans a day plus its two beats.
    expect(Date.parse(last[1]) - Date.parse(last[0])).toBeLessThan(4 * 24 * 3600_000)
  })

  // Nothing resolved the instance, so there is no occurrence and no question: the form was sown
  // from the master and the save writes the master back, rather than offering two answers whose
  // instance it has not got.
  it('saves the whole series when nothing resolved the instance', async () => {
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledWith('e1',
      expect.objectContaining({ scope: 'All' })))
    expect(api.updateEvent.mock.calls[0][1].instanceId).toBeUndefined()
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  // One sentence, and the layout is where it is composed: the modal is handed a finished string.
  it('asks the scope question once, naming the series', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText(
      '“Dentist” repeats: Every 6 months. Which occurrences should take the change?'))
      .toBeInTheDocument()
  })

  it('deletes a plain event behind the shared confirm', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.deleteEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    const box = (await screen.findByText('Delete “Dentist”?')).closest('.modal') as HTMLElement
    await userEvent.click(within(box).getByRole('button', { name: 'Delete' }))
    await waitFor(() =>
      expect(api.deleteEvent).toHaveBeenCalledWith('e1', 'All', undefined))
  })

  it('asks the scope before deleting a recurring event', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.deleteEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    expect(await screen.findByText('Delete a recurring event')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.deleteEvent)
      .toHaveBeenCalledWith('e1', 'This', '2026-09-16T09:00:00'))
  })

  // An obsolete bookmark is a target that no longer exists, never an invitation to create one.
  it('says so and goes back when the event is gone', async () => {
    api.getEvent.mockRejectedValue(new ApiError('Not found', 404))
    const router = renderAt('/calendar/gone/edit?view=week&date=2026-09-16')
    expect(await screen.findByText('This event no longer exists')).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  it('asks before dropping what was typed, and closes a clean form outright', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await userEvent.type(await screen.findByLabelText('Title'), '!')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(await screen.findByText('Discard changes?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  it('mounts the editor container on its own routes', async () => {
    renderAt('/calendar/new?view=week&date=2026-09-14')
    expect(await screen.findByTestId('calendar-editor')).toBeInTheDocument()
  })

  it('draws no editor container on the grid route', async () => {
    renderAt('/calendar?view=week&date=2026-09-14')
    await screen.findByRole('button', { name: 'Today' })
    expect(screen.queryByTestId('calendar-editor')).toBeNull()
  })

  it('hides a calendar from its own box', async () => {
    api.setCalendarVisible.mockResolvedValue(null)
    renderAt()
    await userEvent.click(await screen.findByLabelText('Work'))
    await waitFor(() =>
      expect(api.setCalendarVisible).toHaveBeenCalledWith('b', false))
  })

  it('creates a calendar from the heading', async () => {
    api.createCalendar.mockResolvedValue(calendar('c', 'Trips'))
    renderAt()
    await userEvent.click(await screen.findByRole('button', { name: 'New calendar' }))
    await userEvent.type(screen.getByLabelText('Name'), 'Trips')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.createCalendar).toHaveBeenCalledWith(
      { displayName: 'Trips', color: expect.stringMatching(/^#[0-9a-f]{6}$/i) }, BROWSER_TZ))
  })

  it('renames a calendar from its own row', async () => {
    api.updateCalendar.mockResolvedValue(null)
    renderAt()
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Rename…' }))
    const name = screen.getByLabelText('Name')
    expect(name).toHaveValue('Work')
    expect(name).toHaveFocus()
    await userEvent.clear(name)
    await userEvent.type(name, 'Office')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateCalendar).toHaveBeenCalledWith(
      'b', { displayName: 'Office', color: '#3b82c4' }))
  })

  it('hands an export to the browser', async () => {
    api.exportCalendar.mockResolvedValue({ blob: new Blob(['x']), fileName: 'work.ics' })
    renderAt()
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Export' }))
    await waitFor(() => expect(api.exportCalendar).toHaveBeenCalledWith('b'))
    expect(downloadBlob).toHaveBeenCalledWith(expect.any(Blob), 'work.ics')
  })

  it('pours a file into a calendar and reports what it did', async () => {
    api.importCalendar.mockResolvedValue({
      created: 5, replaced: 0, ignoredTodos: 0, ignoredJournals: 0, failed: 1, totalErrors: 1,
      errors: [{ line: 3, reason: 'No DTSTART' }],
    })
    renderAt()
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Import…' }))

    const file = new File(['BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n'], 'w.ics',
      { type: 'text/calendar' })
    await userEvent.upload(screen.getByLabelText('File'), file)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Import' })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: 'Import' }))

    await waitFor(() => expect(api.importCalendar).toHaveBeenCalledWith('b', file))
    expect(await screen.findByText('Import report')).toBeInTheDocument()
    expect(screen.getByText('Entry 3 — No DTSTART')).toBeInTheDocument()
  })

  it('deletes a calendar behind the shared confirm', async () => {
    api.deleteCalendar.mockResolvedValue(null)
    renderAt()
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(api.deleteCalendar).toHaveBeenCalledWith('b'))
  })

  // The chevrons, Today and the mini-month all move the anchor; a list reading the clock
  // instead left all three dead on one of the four views.
  it('moves the upcoming list with its anchor', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [] })
    const router = renderAt('/calendar?view=list&date=2026-09-16')
    await screen.findByRole('button', { name: 'Next period' })
    const askedFrom = () => {
      const calls = api.getOccurrences.mock.calls
      return calls[calls.length - 1]?.[0]
    }
    await waitFor(() => expect(api.getOccurrences).toHaveBeenCalled())
    const first = askedFrom()

    await userEvent.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-10-16'))
    await waitFor(() => expect(askedFrom()).not.toBe(first))
  })

  it('draws the view the parameters name', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=month&date=2026-09-16')
    expect(await screen.findByText('Stand-up')).toBeInTheDocument()
    expect(document.querySelectorAll('.month-week-number')).toHaveLength(6)
  })

  it('puts the results in the view\'s place while a search stands', async () => {
    api.searchEvents.mockResolvedValue({ occurrences: [occurrence('e9', 'Retro')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('searchbox', { name: 'Search events' })
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search events' }), 'retro')

    expect(await screen.findByText('Retro')).toBeInTheDocument()
    expect(await screen.findByText('1 event found')).toBeInTheDocument()
    expect(api.searchEvents).toHaveBeenCalledWith('retro')
    expect(document.querySelector('.week-body')).toBeNull()
  })

  // Under two characters nothing is asked of the server: every prefix of every word would be a
  // round trip, and "0 events found" would be said about a search that never ran.
  it('asks nothing on a single letter', async () => {
    renderAt('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('searchbox', { name: 'Search events' })
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search events' }), 'r')

    expect(await screen.findByText('Type at least 2 characters to search')).toBeInTheDocument()
    await settle()
    expect(api.searchEvents).not.toHaveBeenCalled()
  })

  it('opens the bubble on a chip', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: /Stand-up/ }))
    expect(await screen.findByRole('dialog', { name: 'Stand-up' })).toBeInTheDocument()
  })

  // A 360px screen has nowhere to hang a 300px bubble: the tap is the editor.
  // Décisions 3 and 10: the bubble is anchored to an event that is highlighted, and every chip
  // of that occurrence carries the highlight.
  it('lights the chip the open bubble hangs off', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: /Stand-up/ }))

    await screen.findByRole('dialog', { name: 'Stand-up' })
    expect(screen.getByRole('button', { name: /Stand-up/ })).toHaveClass('is-selected')
  })

  // A load that never lands must not be a room whose only door is the browser's Back button.
  it('offers a way out of the editor while it is still loading', async () => {
    api.getCalendars.mockRejectedValue(new ApiError('nope', 500))
    const router = renderAt('/calendar/new?view=week&date=2026-09-16')

    const editor = await screen.findByTestId('calendar-editor')
    await userEvent.click(within(editor).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  it('goes straight to the editor from a chip on a phone', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar?view=day&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/e1/edit'))
    expect(screen.queryByRole('dialog', { name: 'Stand-up' })).toBeNull()
  })

  it('gives the grid back when the search is cleared', async () => {
    renderAt('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('searchbox', { name: 'Search events' })
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search events' }), 'retro')
    await screen.findByText('0 events found')

    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(document.querySelector('.week-body')).not.toBeNull())
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('')
  })
})

describe('CalendarLayout — the grid gestures', () => {
  beforeEach(installPointerEvents)

  /** A press on the chip, a travel of `byPx` down the column, and the release. jsdom lays nothing
      out, so every box is at zero: the vertical travel is the whole of the gesture and the drop
      crosses no column. */
  async function dragChip(name: RegExp, byPx: number) {
    const chip = await screen.findByRole('button', { name })
    fireEvent.pointerDown(chip, { clientX: 100, clientY: 500, button: 0, pointerId: 1 })
    firePointer('pointermove', 100, 500 + byPx)
    firePointer('pointerup', 100, 500 + byPx)
    return chip
  }

  it('moves a lone event without asking anything', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue({})
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalled())
    const [id, body] = api.updateEvent.mock.calls[0]
    expect(id).toBe('e1')
    expect(body).toMatchObject({
      scope: 'All', ifHash: 'h1', start: '2026-09-16T10:00:00', end: '2026-09-16T11:00:00',
    })
    expect(body.instanceId).toBeUndefined()
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  it('asks the scope before moving one occurrence of a series', async () => {
    const one = floating('e1', 'Dentist', '2026-09-16T09:00:00')
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue({})
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Dentist/, 56)

    await userEvent.click(await screen.findByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalled())
    expect(api.updateEvent.mock.calls[0][1]).toMatchObject({
      scope: 'This', instanceId: '2026-09-16T09:00:00', start: '2026-09-16T10:00:00',
    })
  })

  it('abandons the drop when the scope question is closed', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Dentist/, 56)
    await userEvent.click(await screen.findByRole('button', { name: 'Close' }))

    await settle()
    expect(api.updateEvent).not.toHaveBeenCalled()
  })

  // The block has to stay where the finger left it: a round trip of its own would snap it back
  // to its old slot for the width of the request, which reads as the drop having failed.
  it('shows the block where it was dropped, and puts it back on a refusal', async () => {
    let refuse = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockReturnValue(new Promise((_resolve, reject) => {
      refuse = () => reject(new ApiError('nope', 500))
    }))
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)
    // Re-read rather than kept: the chip's key carries the minute it starts at, so the block is
    // a new node the instant the cache says it moved.
    await waitFor(() => expect(screen.getByRole('button', { name: /Stand-up/ }))
      .toHaveStyle({ top: '560px' }))

    refuse()
    await waitFor(() => expect(screen.getByRole('button', { name: /Stand-up/ }))
      .toHaveStyle({ top: '504px' }))
  })

  it('sends nothing when the block was dropped where it was', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 0)

    await settle()
    expect(api.updateEvent).not.toHaveBeenCalled()
    expect(api.getEvent).not.toHaveBeenCalled()
  })

  // "Move it, then a quarter of an hour more" is one gesture in the user's head and two drops in
  // ours: sent together they carry the same ifHash and the second comes back 409.
  it('queues a second drop behind the first, on the version the first wrote', async () => {
    let hash = 'h1'
    let land = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockImplementation(() => Promise.resolve({ ...detail(), icsHash: hash }))
    api.updateEvent.mockImplementationOnce(() => new Promise(resolve => {
      land = () => { hash = 'h2'; resolve({}) }
    })).mockResolvedValue({})
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledTimes(1))
    await dragChip(/Stand-up/, 14)
    await settle()
    expect(api.updateEvent).toHaveBeenCalledTimes(1)

    land()
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledTimes(2))
    expect(api.updateEvent.mock.calls[1][1]).toMatchObject({
      ifHash: 'h2', start: '2026-09-16T10:15:00',
    })
  })

  // Google swallows the first click too: a bubble standing over the grid is dismissed by it and
  // nothing else, where an editor opening underneath would be a second answer nobody asked for.
  it('spends a click on an empty column closing an open bubble', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await screen.findByRole('dialog', { name: 'Stand-up' })

    const column = document.querySelectorAll('.day-column')[2] as HTMLElement
    fireEvent.pointerDown(column, { clientX: 10, clientY: 520, button: 0, pointerId: 1 })
    fireEvent.mouseDown(column, { clientX: 10, clientY: 520 })
    firePointer('pointerup', 10, 520)

    await settle()
    expect(router.state.location.pathname).toBe('/calendar')
    expect(screen.queryByRole('dialog', { name: 'Stand-up' })).toBeNull()
  })

  it('opens an hour on the slot a click on an empty column names', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.day-column')).not.toBeNull())
    const column = document.querySelectorAll('.day-column')[2] as HTMLElement

    fireEvent.pointerDown(column, { clientX: 10, clientY: 520, button: 0, pointerId: 1 })
    firePointer('pointerup', 10, 520)

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/new'))
    const search = params(router)
    expect(search.get('allDay')).toBe('0')
    expect(new Date(search.get('end') ?? '').getTime()
      - new Date(search.get('start') ?? '').getTime()).toBe(3_600_000)
  })
})

describe('CalendarLayout — the phone tier', () => {
  // ── The phone tier (task 7) ──────────────────────────────────────────────────────────────

  it('draws the month as a picker over the selected day’s list on a phone', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=month&date=2026-09-16')
    await screen.findByRole('button', { name: /Stand-up/ })
    expect(document.querySelector('.phone-month')).not.toBeNull()
    expect(document.querySelector('.month-view')).toBeNull()
    expect(document.querySelector('.upcoming-list')).not.toBeNull()
  })

  it('opens the editor from a row of that list, naming the occurrence', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Stand-up', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar?view=month&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/e1/edit'))
    expect(params(router).get('instance')).toBe('2026-09-16T09:00:00')
  })

  it('draws a week strip over a one-day grid on a phone', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.day-strip')).not.toBeNull())
    expect(document.querySelectorAll('.day-column')).toHaveLength(1)
  })

  // The toolbar has no room for a 30ch box beside three segments: the field is the list's.
  it('searches from the head of the list on a phone', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=list&date=2026-09-16')
    const field = await screen.findByRole('searchbox', { name: 'Search events' })
    expect(field).toHaveClass('phone-search')

    await userEvent.type(field, 'retro')
    await screen.findByText('0 events found')
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('retro')
  })

  it('offers the floating button over the grid', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.floating-action')).not.toBeNull())
  })

  it('has no floating button while the editor holds the screen', async () => {
    mockViewport('phone')
    renderAt('/calendar/new?view=day&date=2026-09-16')
    await screen.findByTestId('calendar-editor')
    expect(document.querySelector('.floating-action')).toBeNull()
  })

  it('opens the calendars in a drawer from the hamburger', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await userEvent.click(await screen.findByRole('button', { name: 'Open navigation' }))
    expect(document.querySelector('.context-drawer')).toHaveClass('is-open')
  })
})
