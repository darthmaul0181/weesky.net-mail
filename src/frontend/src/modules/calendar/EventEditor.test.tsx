import { cleanup, fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { calendarOf, renderInCalendar, TZ } from './calendarTestHarness'
import type { EventDetail } from './calendarTypes'
import EventEditor, { type EventEditorProps } from './EventEditor'
import type { EventFormState } from './eventForm'

const CALENDARS = [calendarOf('a', '#3b82c4', 'Personal'), calendarOf('b', '#c4783b', 'Work')]

function form(overrides: Partial<EventFormState> = {}): EventFormState {
  return {
    calendarId: 'a', title: 'Dentist', isAllDay: false,
    startDate: '2026-09-14', startTime: '09:00', endDate: '2026-09-14', endTime: '10:00',
    timeZone: TZ, repeat: { kind: 'never' }, reminders: [15],
    location: 'Rue Haute 12', description: 'Bring the card',
    availability: 'Busy', visibility: 'Default', url: '',
    keepRepeat: false, foreignAlarms: [], ...overrides,
  }
}

function detailOf(overrides: Partial<EventDetail> = {}): EventDetail {
  return {
    id: 'e1', calendarId: 'a', uid: 'u1', icsHash: 'h1',
    fields: {
      calendarId: 'a', isAllDay: false, reminderMinutesBefore: [15],
      availability: 'Busy', visibility: 'Default',
    },
    attendees: [], repeatIsExact: true, foreignAlarms: [], ...overrides,
  }
}

function draw(props: Partial<EventEditorProps> = {}) {
  const onSave = vi.fn()
  const onDelete = vi.fn()
  const onClose = vi.fn()
  renderInCalendar(
    <EventEditor detail={null} occurrence={null} initial={form()} calendars={CALENDARS}
      saving={false} error={null} onReload={null} fullScreen={false}
      onSave={onSave} onDelete={onDelete} onClose={onClose} {...props} />)
  return { onSave, onDelete, onClose }
}

describe('EventEditor', () => {
  it('sows the form from the state it is handed', () => {
    draw()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist')
    expect(screen.getByLabelText('Calendar')).toHaveValue('a')
    expect(screen.getByLabelText('Start date')).toHaveValue('2026-09-14')
    expect(screen.getByLabelText('Start time')).toHaveValue('09:00')
    expect(screen.getByLabelText('End time')).toHaveValue('10:00')
    expect(screen.getByLabelText('Location')).toHaveValue('Rue Haute 12')
    expect(screen.getByLabelText('Description')).toHaveValue('Bring the card')
  })

  it('names the two modes', () => {
    draw()
    expect(screen.getByText('New event')).toBeInTheDocument()
    draw({ detail: detailOf() })
    expect(screen.getByText('Edit event')).toBeInTheDocument()
  })

  // A whole day has no hour to show, and a reminder counted in minutes before it would ring at
  // 23:45 the night before.
  it('drops the hours and moves the reminder onto the other ladder on All day', async () => {
    draw()
    await userEvent.click(screen.getByLabelText('All day'))
    expect(screen.queryByLabelText('Start time')).toBeNull()
    expect(screen.queryByLabelText('End time')).toBeNull()
    expect(screen.getByRole('option', { name: 'The day before at 18:00', selected: true }))
      .toBeInTheDocument()
  })

  // Moving the start of a meeting does not shorten it: the end follows by the duration it had.
  it('carries the end along when the start moves', () => {
    draw()
    fireEvent.change(screen.getByLabelText('Start date'), { target: { value: '2026-09-20' } })
    expect(screen.getByLabelText('End date')).toHaveValue('2026-09-20')
    expect(screen.getByLabelText('End time')).toHaveValue('10:00')
  })

  it('reads a repeat rule back under the picker', () => {
    draw({
      initial: form({
        repeat: {
          kind: 'custom',
          rule: { frequency: 'MONTHLY', interval: 6, byDay: [], end: 'Never' },
        },
      }),
    })
    expect(screen.getByLabelText('Repeat')).toHaveValue('custom')
    expect(screen.getByText('Every 6 months')).toBeInTheDocument()
  })

  // The screen never showed the stored rule, so it must not decide it — until the user says so.
  it('locks a rule this screen cannot draw, and Replace unlocks it', async () => {
    draw({
      detail: detailOf({ repeatIsExact: false }),
      initial: form({ keepRepeat: true, repeat: { kind: 'never' } }),
    })
    expect(screen.getByText(/repeats in a way this screen cannot show/)).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Repeat' })).toBeNull()
    // The row's label must name what is drawn, never a control that is not there.
    expect(screen.getByRole('group', { name: 'Repeat' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Replace' }))
    expect(screen.getByLabelText('Repeat')).toBeInTheDocument()
  })

  // A value that is not the default is a value somebody set: hiding it behind a chevron would
  // make the form say something the event does not.
  it('opens More options when something under it is not the default', () => {
    draw({ initial: form({ availability: 'Free' }) })
    expect(screen.getByLabelText('Web address')).toBeVisible()
  })

  it('keeps More options folded on a plain event', async () => {
    draw()
    expect(screen.queryByLabelText('Web address')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'More options' }))
    expect(screen.getByLabelText('Web address')).toBeInTheDocument()
  })

  it('lists the attendees it cannot yet write to', () => {
    draw({
      detail: detailOf({
        attendees: [
          { email: 'boss@weesky.be', name: 'Boss', isOrganizer: true },
          { email: 'me@weesky.be', partStat: 'ACCEPTED', isOrganizer: false },
        ],
      }),
    })
    expect(screen.getByText('Boss')).toBeInTheDocument()
    expect(screen.getByText('me@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Read only until invitations are supported')).toBeInTheDocument()
  })

  it('hands the whole form to the save, with no scope of its own', async () => {
    const { onSave } = draw()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Dentist', calendarId: 'a' }), null)
  })

  it('refuses an end that comes before its start', async () => {
    const { onSave } = draw()
    fireEvent.change(screen.getByLabelText('End time'), { target: { value: '08:00' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByText('The end comes before the start')).toBeInTheDocument()
    expect(onSave).not.toHaveBeenCalled()
  })

  it('shows the refusal the layout hands back', () => {
    draw({ error: 'Could not save the event' })
    expect(screen.getByText('Could not save the event')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reload' })).toBeNull()
  })

  // A stale write is the one refusal with something to do about it, so it gets a way out.
  it('offers a way out of a stale write', async () => {
    const onReload = vi.fn()
    renderInCalendar(
      <EventEditor detail={detailOf()} occurrence={null} initial={form()} calendars={CALENDARS}
        saving={false} error="This event changed elsewhere since you opened it."
        onReload={onReload} fullScreen={false}
        onSave={vi.fn()} onDelete={vi.fn()} onClose={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))
    expect(onReload).toHaveBeenCalled()
  })

  // Both ladders fold what they cannot express onto one default, so two bells would become two
  // identical lines.
  it('does not leave two identical reminders behind an All day flip', async () => {
    draw({ initial: form({ reminders: [5, 10] }) })
    await userEvent.click(screen.getByLabelText('All day'))
    expect(screen.getAllByRole('combobox', { name: 'Reminder' })).toHaveLength(1)
  })

  // The calendar stays live on a recurring event: the scope is asked afterwards, and it is the
  // scope dialog that offers All alone once the calendar has moved.
  it('leaves the calendar picker live on a recurring event', () => {
    draw({
      detail: detailOf({
        fields: {
          calendarId: 'a', isAllDay: false, reminderMinutesBefore: [],
          availability: 'Busy', visibility: 'Default',
          repeat: { frequency: 'MONTHLY', interval: 6, byDay: [], end: 'Never' },
        },
      }),
    })
    expect(screen.getByLabelText('Calendar')).toBeEnabled()
  })

  it('offers Delete on an event that exists, and never on a new one', () => {
    draw()
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull()
    draw({ detail: detailOf() })
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()
  })

  it('reports a clean form as clean and a touched one as dirty', async () => {
    const { onClose } = draw()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledWith(false)

    cleanup()
    const second = draw()
    await userEvent.type(screen.getByLabelText('Title'), '!')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(second.onClose).toHaveBeenCalledWith(true)
  })

  // The event's own zone, not the browser's: one written in New York and opened from Brussels
  // still says the hour its author chose, so the screen has to say whose hour it is.
  it('says which zone the hours are in when it is not the browser\'s', () => {
    draw({ initial: form({ timeZone: 'America/New_York' }) })
    expect(screen.getByText('Times in America/New_York')).toBeInTheDocument()
  })

  it('says nothing about the zone when it is the browser\'s own', () => {
    draw()
    expect(screen.queryByText(/^Times in/)).toBeNull()
  })

  it('carries the full-screen header on a phone', () => {
    draw({ fullScreen: true })
    const header = document.querySelector('.calendar-editor-head')
    expect(header).not.toBeNull()
    expect(header).toHaveTextContent('New event')
    expect(header?.querySelector('.btn-primary')).toHaveTextContent('Save')
  })
})
