import { screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, type Mock, vi } from 'vitest'
import { calendarOf, occurrenceOf } from './calendarTestHarness'
import {
  api, BROWSER_TZ, calendar, CALENDARS, detail, installLayoutMocks, user, renderAt,
} from './calendarLayoutHarness'
import { mockViewport, pickOption, optionsOf } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))
vi.mock('../../lib/downloadBlob', () => ({ downloadBlob: vi.fn() }))

installLayoutMocks()

const { downloadBlob } = await import('../../lib/downloadBlob') as unknown as {
  downloadBlob: Mock<(...args: unknown[]) => unknown>
}

describe('CalendarLayout — the calendars', () => {
  it('hides a calendar from its own box', async () => {
    api.setCalendarVisible.mockResolvedValue(null)
    renderAt()
    await user.click(await screen.findByLabelText('Work'))
    await waitFor(() =>
      expect(api.setCalendarVisible).toHaveBeenCalledWith('b', false))
  })

  it('creates a calendar from the heading', async () => {
    api.createCalendar.mockResolvedValue(calendar('c', 'Trips'))
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'New calendar' }))
    await user.type(screen.getByLabelText('Name'), 'Trips')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    const hexColour: unknown = expect.stringMatching(/^#[0-9a-f]{6}$/i)
    await waitFor(() => expect(api.createCalendar).toHaveBeenCalledWith(
      { displayName: 'Trips', color: hexColour }, BROWSER_TZ))
  })

  it('renames a calendar from its own row', async () => {
    api.updateCalendar.mockResolvedValue(null)
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Rename…' }))
    const name = screen.getByLabelText('Name')
    expect(name).toHaveValue('Work')
    expect(name).toHaveFocus()
    await user.clear(name)
    await user.type(name, 'Office')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateCalendar).toHaveBeenCalledWith(
      'b', { displayName: 'Office', color: '#3b82c4' }))
  })

  it('hands an export to the browser', async () => {
    api.exportCalendar.mockResolvedValue({ blob: new Blob(['x']), fileName: 'work.ics' })
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Export' }))
    await waitFor(() => expect(api.exportCalendar).toHaveBeenCalledWith('b'))
    expect(downloadBlob).toHaveBeenCalledWith(expect.any(Blob), 'work.ics')
  })

  it('pours a file into a calendar and reports what it did', async () => {
    api.importCalendar.mockResolvedValue({
      created: 5, replaced: 0, ignoredTodos: 0, ignoredJournals: 0, failed: 1, totalErrors: 1,
      errors: [{ line: 3, reason: 'No DTSTART' }],
    })
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Import…' }))

    const file = new File(['BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n'], 'w.ics',
      { type: 'text/calendar' })
    await user.upload(screen.getByLabelText('File'), file)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Import' })).toBeEnabled())
    await user.click(screen.getByRole('button', { name: 'Import' }))

    await waitFor(() => expect(api.importCalendar).toHaveBeenCalledWith('b', file))
    expect(await screen.findByText('Import report')).toBeInTheDocument()
    expect(screen.getByText('Entry 3 — No DTSTART')).toBeInTheDocument()
  })

  // The row's own menu is what opened the confirm, and the row leaves with the calendar: the
  // module's one region takes the focus, as it does for the bubble's delete and for the editor.
  it('deletes a calendar behind the shared confirm, handing focus to the grid column as its row goes', async () => {
    let rows = CALENDARS
    api.getCalendars.mockImplementation(async () => ({ calendars: rows }))
    api.deleteCalendar.mockImplementation(async () => {
      rows = rows.filter(one => one.id !== 'b')
      return null
    })
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Delete…' }))

    await user.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Actions for Work' })).toBeNull())
    expect(api.deleteCalendar).toHaveBeenCalledWith('b')
    expect(document.querySelector('.calendar-main')).toHaveFocus()
  })

  // The same hand-back with a real network between the two round trips: the confirm closes on the
  // DELETE while the row leaves only when the calendar refetch lands, a whole macrotask later.
  it('hands focus to the grid column when the refetch lands after the confirm closed', async () => {
    let rows = CALENDARS
    api.getCalendars.mockImplementation(
      () => new Promise(resolve => setTimeout(() => resolve({ calendars: rows }), 30)))
    api.deleteCalendar.mockImplementation(async () => {
      rows = rows.filter(one => one.id !== 'b')
      return null
    })
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Delete…' }))

    await user.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.queryByText('Confirm deletion')).toBeNull())
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Actions for Work' })).toBeNull())
    expect(document.querySelector('.calendar-main')).toHaveFocus()
  })
})

describe('CalendarLayout — the birthdays calendar', () => {
  const BIRTHDAYS = calendarOf('z', '#be185d', 'Birthdays', {
    kind: 'birthdays', birthdayReminder: 'same_day', timeZone: BROWSER_TZ,
  })
  const ALICE = occurrenceOf({
    eventId: 'b1', calendarId: 'z', summary: '🎂 Alice Martin', isAllDay: true,
    contactId: 'k', birthYear: 1986, startDate: '2026-09-16', endDateExclusive: '2026-09-17',
    transparency: 'TRANSPARENT', hasAlarm: true,
  })
  const BIRTHDAY_DETAIL = { ...detail({ calendarId: 'z', summary: '🎂 Alice Martin' }), id: 'b1', calendarId: 'z' }
  const location = (router: ReturnType<typeof renderAt>) =>
    router.state.location.pathname + router.state.location.search

  beforeEach(() => {
    api.getCalendars.mockResolvedValue({ calendars: [...CALENDARS, BIRTHDAYS] })
    api.getOccurrences.mockResolvedValue({ occurrences: [ALICE] })
  })

  it('opens the card from the bubble', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Alice Martin/ }))
    const bubble = await screen.findByRole('dialog', { name: '🎂 Alice Martin' })
    await user.click(within(bubble).getByRole('button', { name: 'Open card' }))
    await waitFor(() => expect(location(router)).toBe('/contacts?id=k'))
    expect(api.getEvent).not.toHaveBeenCalled()
  })

  // What opens the editor for any other event opens the card for a birthday.
  it('opens the card, not the editor, on a double click', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await user.dblClick(await screen.findByRole('button', { name: /Alice Martin/ }))
    await waitFor(() => expect(location(router)).toBe('/contacts?id=k'))
  })

  it('opens the reading screen, not the editor, on a phone', async () => {
    mockViewport('phone')
    const router = renderAt('/calendar?view=day&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Alice Martin/ }))
    const shown = await screen.findByRole('dialog', { name: 'Birthday' })
    expect(router.state.location.pathname).toBe('/calendar')
    expect(shown).toHaveTextContent('40 years old')

    await user.click(within(shown).getByRole('button', { name: 'Open card' }))
    await waitFor(() => expect(location(router)).toBe('/contacts?id=k'))
  })

  it('closes the reading screen on its ✕', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Alice Martin/ }))
    const shown = await screen.findByRole('dialog', { name: 'Birthday' })
    await user.click(within(shown).getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog', { name: 'Birthday' })).toBeNull()
  })

  // A hand-typed edit URL: the card on a desktop, the grid on a phone, never the editor.
  it('sends its edit URL to the card', async () => {
    api.getEvent.mockResolvedValue(BIRTHDAY_DETAIL)
    const router = renderAt('/calendar/b1/edit?view=week&date=2026-09-16')
    await waitFor(() => expect(location(router)).toBe('/contacts?id=k'))
    expect(screen.queryByLabelText('Calendar')).toBeNull()
  })

  it('sends its edit URL back to the grid on a phone', async () => {
    mockViewport('phone')
    api.getEvent.mockResolvedValue(BIRTHDAY_DETAIL)
    const router = renderAt('/calendar/b1/edit?view=day&date=2026-09-16')
    await waitFor(() => expect(location(router)).toBe('/calendar?view=day&date=2026-09-16'))
    expect(screen.queryByLabelText('Calendar')).toBeNull()
  })

  it('is never offered to a new event', async () => {
    renderAt('/calendar/new?view=week&date=2026-09-16')
    const box = await screen.findByLabelText('Calendar')
    expect(await optionsOf(box)).toEqual(['Personal', 'Work'])
  })

  it('is never offered as an import target', async () => {
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Import…' }))
    const box = await screen.findByRole('combobox', { name: 'An existing calendar' })
    expect(await optionsOf(box)).toEqual(['Personal', 'Work'])
  })

  it('saves its reminder from Settings', async () => {
    api.updateCalendar.mockResolvedValue(null)
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Birthdays' }))
    await user.click(screen.getByRole('menuitem', { name: 'Settings…' }))
    await pickOption(await screen.findByRole('combobox', { name: 'Reminder' }), 'A week before')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateCalendar).toHaveBeenCalledWith('z', {
      displayName: 'Birthdays', color: '#be185d', birthdayReminder: 'week_before',
    }))
  })

  // Nothing is lost — the dates stay on the cards — so the action is primary, not danger.
  it('turns birthdays off behind a primary confirm', async () => {
    api.setBirthdays.mockResolvedValue(null)
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Actions for Birthdays' }))
    await user.click(screen.getByRole('menuitem', { name: 'Disable…' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Turn off birthdays?' })
    const disable = within(confirm).getByRole('button', { name: 'Disable' })
    expect(disable).toHaveClass('btn-primary')
    expect(disable).not.toHaveClass('btn-danger-solid')

    await user.click(disable)
    await waitFor(() => expect(api.setBirthdays).toHaveBeenCalledWith(false, BROWSER_TZ, 'en'))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
  })
})
