import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { utcOfLocalMidnight } from './plainDate'
import type { Calendar } from './calendarTypes'
import {
  api, ApiError, BROWSER_TZ, CALENDARS, floating, installLayoutMocks, user, mount, occurrence,
  params, renderAt,
} from './calendarLayoutHarness'
import { mockViewport, settle } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

installLayoutMocks()

/** Today as the browser's own zone reads it — what the layout falls back to. */
function today() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: BROWSER_TZ }).format(new Date())
}

describe('CalendarLayout — navigating and loading', () => {
  it('names the view and the day it opens on', async () => {
    const router = renderAt()
    await waitFor(() => expect(params(router).get('view')).toBe('week'))
    expect(params(router).get('date')).toBe(today())
  })

  describe('across midnight', () => {
    // Five seconds before Thursday 17 September, in the zone the layout reads.
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      vi.setSystemTime(utcOfLocalMidnight('2026-09-17', BROWSER_TZ).getTime() - 5_000)
    })
    afterEach(() => { vi.useRealTimers() })

    const todayHead = () => document.querySelector('.day-column.is-today')?.getAttribute('data-day')

    // The date the layout wrote itself was never chosen: it moves on with the day, in place.
    it('moves a view left on today on to the new day', async () => {
      const router = renderAt()
      await waitFor(() => expect(params(router).get('date')).toBe('2026-09-16'))

      await act(async () => { await vi.advanceTimersByTimeAsync(60_000) })

      await waitFor(() => expect(params(router).get('date')).toBe('2026-09-17'))
      expect(router.state.historyAction).toBe('REPLACE')
    })

    it('leaves a day the user went to, and moves only the highlight', async () => {
      const router = renderAt('/calendar?view=week&date=2026-09-15')
      await waitFor(() => expect(todayHead()).toBe('2026-09-16'))

      await act(async () => { await vi.advanceTimersByTimeAsync(60_000) })

      await waitFor(() => expect(todayHead()).toBe('2026-09-17'))
      expect(params(router).get('date')).toBe('2026-09-15')
    })
  })

  describe('first day of the week', () => {
    const firstColumn = () => document.querySelector('.day-column')?.getAttribute('data-day')

    it('starts the week on Monday by default', async () => {
      renderAt('/calendar?view=week&date=2026-09-16')
      await waitFor(() => expect(firstColumn()).toBe('2026-09-14'))
    })

    it('starts the week on Sunday when the setting says so', async () => {
      api.getPreferences.mockResolvedValue({ 'calendar.firstDayOfWeek': 'sunday' })
      renderAt('/calendar?view=week&date=2026-09-16')
      await waitFor(() => expect(firstColumn()).toBe('2026-09-13'))
      expect(api.getOccurrences).toHaveBeenCalledTimes(1)
    })

    // Drawn on Monday first, a Sunday account would watch its week jump a column on every load.
    it('waits for the setting before asking for or drawing a week', async () => {
      api.getPreferences.mockReturnValue(new Promise(() => {}))
      renderAt('/calendar?view=week&date=2026-09-16')
      await settle()
      expect(api.getOccurrences).not.toHaveBeenCalled()
      expect(firstColumn()).toBeUndefined()
    })

    it('falls back to Monday when the setting cannot be read', async () => {
      api.getPreferences.mockRejectedValue(new ApiError('nope', 500))
      renderAt('/calendar?view=week&date=2026-09-16')
      await waitFor(() => expect(firstColumn()).toBe('2026-09-14'))
    })
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
    await user.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-21'))
    await user.click(screen.getByRole('button', { name: 'Previous period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-14'))
  })

  it('comes back to today', async () => {
    const router = renderAt('/calendar?view=week&date=2020-01-06')
    await user.click(await screen.findByRole('button', { name: 'Today' }))
    await waitFor(() => expect(params(router).get('date')).toBe(today()))
  })

  // The zone decides which day a floating instance falls on, so it travels with every request.
  it('asks for the calendars in the zone the browser is in', async () => {
    renderAt()
    await waitFor(() => expect(api.getCalendars).toHaveBeenCalledWith(BROWSER_TZ, 'en'))
  })

  it('remembers the view that was chosen', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-14')
    await user.click(await screen.findByRole('radio', { name: 'Month' }))
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
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() =>
      expect(screen.queryByText('The window holds too many occurrences; narrow it')).toBeNull())
  })

  // TanStack keeps the data of a query whose refetch failed: a grid already drawn is still the
  // best answer on hand, and swapping it for the error band would throw away what the user sees.
  it('keeps the grid when a background read of the window fails', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    const { client } = mount('/calendar?view=week&date=2026-09-16')
    expect(await screen.findByRole('button', { name: /Stand-up/ })).toBeInTheDocument()

    const reads = api.getOccurrences.mock.calls.length
    api.getOccurrences.mockRejectedValue(new ApiError('boom', 500))
    await client.refetchQueries()
    await settle()
    expect(api.getOccurrences.mock.calls.length).toBeGreaterThan(reads)
    expect(screen.getByRole('button', { name: /Stand-up/ })).toBeInTheDocument()
    expect(document.querySelector('.calendar-error')).toBeNull()
  })

  // A refetch that fails keeps the list it had, so the sidebar has nothing to apologise for.
  it('keeps the calendar list quiet when a background read of it fails', async () => {
    const { client } = mount('/calendar?view=week&date=2026-09-16')
    expect(await screen.findByLabelText('Work')).toBeInTheDocument()

    const reads = api.getCalendars.mock.calls.length
    api.getCalendars.mockRejectedValue(new ApiError('boom', 500))
    await client.refetchQueries()
    await settle()
    expect(api.getCalendars.mock.calls.length).toBeGreaterThan(reads)
    expect(document.querySelector('.calendar-sidebar-error')).toBeNull()
    expect(screen.getByLabelText('Work')).toBeInTheDocument()
  })

  // Drawn before the list answers, every chip would wear the default colour and a hidden
  // calendar's events would show, then vanish.
  it('waits for the calendar list before drawing the grid', async () => {
    let answer: (value: { calendars: Calendar[] }) => void = () => {}
    api.getCalendars.mockReturnValue(new Promise(resolve => { answer = resolve }))
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await waitFor(() => expect(api.getOccurrences).toHaveBeenCalled())
    await settle()
    expect(screen.queryByRole('button', { name: /Stand-up/ })).toBeNull()

    answer({ calendars: CALENDARS })
    expect(await screen.findByRole('button', { name: /Stand-up/ })).toBeInTheDocument()
  })

  // The sidebar says the list was refused; the grid has its occurrences and draws them.
  it('draws the grid when the calendar list is refused', async () => {
    api.getCalendars.mockRejectedValue(new ApiError('nope', 500))
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    expect(await screen.findByRole('button', { name: /Stand-up/ })).toBeInTheDocument()
    expect(document.querySelector('.calendar-sidebar-error')).not.toBeNull()
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

    await user.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-10-17'))
    await waitFor(() => expect(askedFrom()).not.toBe(first))
  })

  // A chevron re-keys five of the six rows, so the hook recovers the lost stop next door and
  // landed on row 0's Monday, an outside day of the month just left, where Enter then created.
  it('lands the keyboard on the anchor after a month step', async () => {
    const router = renderAt('/calendar?view=month&date=2026-09-16')
    await screen.findByRole('button', { name: 'Next period' })

    await user.click(screen.getByRole('button', { name: 'Next period' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-10-16'))

    const stops = document.querySelectorAll('.month-view [tabindex="0"]')
    expect(stops).toHaveLength(1)
    expect(stops[0]).toHaveAttribute('aria-selected', 'true')
    expect(stops[0]).not.toHaveClass('is-outside')
    expect(stops[0]!.querySelector('.month-day-number')).toHaveTextContent('16')
  })

  // The other door onto the anchor, and the one a month key would have missed: picking a day in the
  // mini-month moves `aria-selected` alone, which no mutation the hook observes carries.
  it('lands the keyboard on the anchor after a same-month pick', async () => {
    const router = renderAt('/calendar?view=month&date=2026-09-16')
    await screen.findByRole('button', { name: 'Next period' })

    const mini = document.querySelector('.mini-month') as HTMLElement
    await user.click(within(mini).getByRole('button', { name: 'September 24, 2026' }))
    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-24'))

    const stops = document.querySelectorAll('.month-view [tabindex="0"]')
    expect(stops).toHaveLength(1)
    expect(stops[0]!.querySelector('.month-day-number')).toHaveTextContent('24')
  })

  it('draws the view the parameters name', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=month&date=2026-09-16')
    expect(await screen.findByText('Stand-up')).toBeInTheDocument()
    expect(document.querySelectorAll('.month-week-number')).toHaveLength(6)
  })
})

describe('CalendarLayout — searching', () => {
  // The one search here left to the pause rather than to Enter: nothing is asked while the keys
  // may still be coming, and the question goes out 300ms after the last one.
  it('puts the results in the view\'s place once the typing pauses', async () => {
    api.searchEvents.mockResolvedValue({ occurrences: [occurrence('e9', 'Retro')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    const box = await screen.findByRole('searchbox', { name: 'Search events' })

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    try {
      fireEvent.change(box, { target: { value: 'retro' } })
      act(() => { vi.advanceTimersByTime(299) })
      expect(api.searchEvents).not.toHaveBeenCalled()
      act(() => { vi.advanceTimersByTime(1) })
      expect(api.searchEvents).toHaveBeenCalledWith('retro')
      await act(async () => { await vi.advanceTimersByTimeAsync(10) })
    } finally {
      vi.useRealTimers()
    }

    expect(await screen.findByText('Retro')).toBeInTheDocument()
    expect(await screen.findByText('1 event found')).toBeInTheDocument()
    expect(document.querySelector('.week-body')).toBeNull()
  })

  // Under two characters nothing is asked of the server: every prefix of every word would be a
  // round trip, and "0 events found" would be said about a search that never ran.
  it('asks nothing on a single letter', async () => {
    renderAt('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('searchbox', { name: 'Search events' })
    await user.type(screen.getByRole('searchbox', { name: 'Search events' }), 'r{Enter}')

    expect(await screen.findByText('Type at least 2 characters to search')).toBeInTheDocument()
    await settle()
    expect(api.searchEvents).not.toHaveBeenCalled()

    // The same Enter on a second letter does ask, before any pause: the silence above was the rule's.
    await user.type(screen.getByRole('searchbox', { name: 'Search events' }), 'e{Enter}')
    await settle()
    expect(api.searchEvents).toHaveBeenCalledWith('re')
  })

  // The click goes to that day in the *current view*, so the box is emptied on the way — the
  // results used to stay up and go on covering the grid they had just moved.
  it('gives the grid back at the day a search hit sits on', async () => {
    api.searchEvents.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    const router = renderAt('/calendar?view=week&date=2026-08-03')

    await user.type(
      await screen.findByRole('searchbox', { name: 'Search events' }), 'dentist{Enter}')
    await user.click(await screen.findByRole('button', { name: /Dentist/ }))

    await waitFor(() => expect(params(router).get('date')).toBe('2026-09-16'))
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('')
    expect(document.querySelector('.week-body')).not.toBeNull()
    expect(await screen.findByRole('dialog', { name: 'Dentist' })).toBeInTheDocument()
  })

  it('gives the grid back when the search is cleared', async () => {
    renderAt('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('searchbox', { name: 'Search events' })
    await user.type(screen.getByRole('searchbox', { name: 'Search events' }), 'retro{Enter}')
    await screen.findByText('0 events found')

    await user.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(document.querySelector('.week-body')).not.toBeNull())
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('')
  })
})
