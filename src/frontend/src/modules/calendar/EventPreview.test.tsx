import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { calendarOf, occurrenceOf, renderInCalendar } from './calendarTestHarness'
import EventPreview from './EventPreview'
import type { EventDetail, Occurrence } from './calendarTypes'

vi.mock('../../api.js', () => ({
  api: { getEvent: vi.fn() },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'getEvent', ReturnType<typeof vi.fn>>
}

function detailOf(fields: Partial<EventDetail> & { id: string }): EventDetail {
  return {
    calendarId: 'a', uid: fields.id, icsHash: 'h1',
    fields: {
      calendarId: 'a', isAllDay: false, reminderMinutesBefore: [],
      availability: 'Busy', visibility: 'Default',
    },
    attendees: [], repeatIsExact: true, foreignAlarms: [],
    ...fields,
  }
}

/** jsdom lays nothing out, so the anchor states its own rectangle — which is all the placement
    reads anyway. */
const anchors: HTMLElement[] = []
const WIDTH = window.innerWidth

// The preview now always fetches the detail (Task 7); a case with nothing to say about it still
// needs an answer, or the query settles on `undefined`, which react-query refuses to hold.
beforeEach(() => {
  api.getEvent.mockResolvedValue(detailOf({ id: 'e1' }))
})

// The window's width and the anchors are global state; a file that left either behind would make
// every case after it depend on the order they run in.
afterEach(() => {
  window.innerWidth = WIDTH
  anchors.splice(0).forEach(node => node.remove())
  vi.clearAllMocks()
})

function anchorAt(left: number, right: number): HTMLElement {
  const element = document.createElement('button')
  document.body.append(element)
  anchors.push(element)
  element.getBoundingClientRect = () => ({
    left, right, top: 100, bottom: 130, width: right - left, height: 30, x: left, y: 100,
    toJSON: () => ({}),
  }) as DOMRect
  return element
}

const DENTIST = {
  eventId: 'e1', summary: 'Dentist', location: 'Rue Haute 12',
  startUtc: '2026-09-14T07:00:00Z', endUtc: '2026-09-14T08:00:00Z',
}

function draw(fields: Partial<Occurrence> & { eventId: string } = DENTIST,
  anchor = anchorAt(200, 300), handlers: Partial<{
    onClose: () => void; onEdit: () => void; onDelete: () => void
  }> = {}) {
  const noop = () => {}
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  renderInCalendar(
    <QueryClientProvider client={client}>
      <EventPreview occurrence={occurrenceOf(fields)} calendar={calendarOf('a', '#3b82c4', 'Personal')}
        anchor={anchor} rect={anchor.getBoundingClientRect()}
        onClose={handlers.onClose ?? noop} onEdit={handlers.onEdit ?? noop}
        onDelete={handlers.onDelete ?? noop} />
    </QueryClientProvider>)
  return document.querySelector('.event-preview') as HTMLElement
}

describe('EventPreview', () => {
  it('names the event, its day, its hours and its calendar', () => {
    const bubble = draw()
    expect(bubble).toHaveTextContent('Dentist')
    // The comma between the weekday and the day is ICU's, not a product contract — the same
    // reason UpcomingList asserts its heading by prefix.
    expect(bubble).toHaveTextContent(/Monday.*14.*September/)
    expect(bubble).toHaveTextContent('09:00')
    expect(bubble).toHaveTextContent('10:00')
    expect(bubble).toHaveTextContent('Rue Haute 12')
    expect(bubble).toHaveTextContent('Personal')
  })

  it('names an event with no title', () => {
    expect(draw({ eventId: 'e1', startUtc: '2026-09-14T07:00:00Z', endUtc: '2026-09-14T08:00:00Z' }))
      .toHaveTextContent('(No title)')
  })

  // The occurrence carries the flag and not the minutes: the editor is where the ladder lives.
  it('says a reminder is set without inventing its minutes', () => {
    expect(draw({ ...DENTIST, hasAlarm: true })).toHaveTextContent('Reminder set')
    cleanup()
    expect(draw({ ...DENTIST, hasAlarm: false })).not.toHaveTextContent('Reminder set')
  })

  // The bubble names an exact rule with the same sentence the editor shows — it fetches the
  // detail itself, `ContactCard`'s pattern, since `Occurrence` carries no structured rule.
  it('names an exact rule with the editor’s own sentence', async () => {
    api.getEvent.mockResolvedValue(detailOf({
      id: 'e1', repeatIsExact: true,
      fields: {
        calendarId: 'a', isAllDay: false, reminderMinutesBefore: [],
        availability: 'Busy', visibility: 'Default',
        repeat: { frequency: 'WEEKLY', interval: 1, byDay: ['MO', 'WE'], end: 'Never' },
      },
    }))
    const bubble = draw({ ...DENTIST, recurrenceText: 'FREQ=WEEKLY;BYDAY=MO,WE' })
    await waitFor(() => expect(bubble).toHaveTextContent('Every week on Monday and Wednesday'))
    expect(bubble.textContent).not.toMatch(/FREQ=/)
  })

  // `repeatIsExact: false` means the stored rule is richer than the structure can hold — the
  // generic label stands in, never the raw text the picker could not draw from.
  it('names a non-exact rule with the generic label, never the raw rule', async () => {
    api.getEvent.mockResolvedValue(detailOf({
      id: 'e1', repeatIsExact: false,
      fields: {
        calendarId: 'a', isAllDay: false, reminderMinutesBefore: [],
        availability: 'Busy', visibility: 'Default',
        repeat: { frequency: 'WEEKLY', interval: 1, byDay: ['TU', 'WE'], end: 'Until', until: '20261027T170000Z' },
      },
    }))
    const bubble = draw({
      ...DENTIST, recurrenceText: 'FREQ=WEEKLY;UNTIL=20261027T170000Z;BYDAY=TU,W',
    })
    await waitFor(() => expect(bubble).toHaveTextContent('Repeats'))
    expect(bubble.textContent).not.toMatch(/FREQ=/)
  })

  // The regression guard for this whole fix: while the detail is still loading (or fails to
  // load), the raw rule must not appear either — the generic label covers that gap too.
  it('never shows the raw rule while the detail has not answered', () => {
    api.getEvent.mockReturnValue(new Promise(() => {})) // never resolves
    const bubble = draw({ ...DENTIST, recurrenceText: 'FREQ=WEEKLY;UNTIL=20261027T170000Z;BYDAY=TU,W' })
    expect(bubble).toHaveTextContent('Repeats')
    expect(bubble.textContent).not.toMatch(/FREQ=/)
  })

  // The range is the whole line: an event with no hour has nothing to add to it.
  it('spans the days of an all-day event, and says nothing else', () => {
    const bubble = draw({
      eventId: 'e2', summary: 'Trip', isAllDay: true,
      startDate: '2026-09-14', endDateExclusive: '2026-09-17',
    })
    const when = bubble.querySelector('.event-preview-when') as HTMLElement
    expect(when.textContent).toMatch(/Monday.*14.*Wednesday.*16.*September/)
    expect(when.textContent).not.toMatch(/·/)
    expect(when.textContent).not.toMatch(/All day/)
  })

  it('says All day on a single whole day', () => {
    const bubble = draw({
      eventId: 'e2', summary: 'Leave', isAllDay: true,
      startDate: '2026-09-14', endDateExclusive: '2026-09-15',
    })
    expect(bubble.querySelector('.event-preview-when')).toHaveTextContent(/September · All day/)
  })

  // The guard is not to throw; the label must still say what the event is. A dated event whose
  // clocks the server did not send is not a whole day, and must not be called one.
  it('draws a dated event with unreadable clocks without calling it All day', () => {
    const when = draw({ eventId: 'e3', summary: 'Half a clock', localStart: '2026-09-14T09:00:00' })
      .querySelector('.event-preview-when') as HTMLElement
    expect(when.textContent).toMatch(/Monday.*14.*September/)
    expect(when.textContent).not.toMatch(/All day/)
  })

  it('opens to the right of its chip', () => {
    window.innerWidth = 1200
    expect(draw(DENTIST, anchorAt(200, 300))).toHaveStyle({ left: '308px' })
  })

  // 300 of bubble plus the 8px gap: past the right edge it opens on the other side instead.
  it('flips to the left when it would run off the screen', () => {
    window.innerWidth = 600
    expect(draw(DENTIST, anchorAt(400, 500))).toHaveStyle({ left: '92px' })
  })

  it('closes on Escape', async () => {
    const onClose = vi.fn()
    draw(DENTIST, anchorAt(200, 300), { onClose })
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })

  it('closes on a click outside itself', async () => {
    const onClose = vi.fn()
    draw(DENTIST, anchorAt(200, 300), { onClose })
    await userEvent.click(document.body)
    expect(onClose).toHaveBeenCalled()
  })

  it('stays open on a click inside itself', async () => {
    const onClose = vi.fn()
    const bubble = draw(DENTIST, anchorAt(200, 300), { onClose })
    await userEvent.click(bubble.querySelector('.event-preview-title') as HTMLElement)
    expect(onClose).not.toHaveBeenCalled()
  })

  it('hands Edit and Delete on', async () => {
    const onEdit = vi.fn()
    const onDelete = vi.fn()
    draw(DENTIST, anchorAt(200, 300), { onEdit, onDelete })
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    expect(onEdit).toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onDelete).toHaveBeenCalled()
  })

  it('is a dialog named after the event', () => {
    draw()
    expect(screen.getByRole('dialog', { name: 'Dentist' })).toBeInTheDocument()
  })

  it('a received event says who organises it and names the attendees, without their state',
    async () => {
      api.getEvent.mockResolvedValue(detailOf({
        id: 'e1', attendees: [
          { email: 'marc@example.org', name: 'Marc Dupont', isOrganizer: true },
          { email: 'alice@weesky.be', name: 'Alice', partStat: 'ACCEPTED', isOrganizer: false },
          { email: 'jean@example.net', partStat: 'NEEDS-ACTION', isOrganizer: false },
        ],
      }))
      draw()
      expect(await screen.findByText('Organised by Marc Dupont')).toBeInTheDocument()
      expect(screen.getByText('Alice, jean@example.net')).toBeInTheDocument()
      expect(screen.queryByText('ACCEPTED')).toBeNull()
    })

  it('an event without attendees shows neither line', async () => {
    api.getEvent.mockResolvedValue(detailOf({ id: 'e1' }))
    const bubble = draw()
    // Not just "called" — the promise must actually have resolved and the re-render landed,
    // or the assertion below would pass just as trivially on the still-pending state.
    await waitFor(() => expect(api.getEvent).toHaveResolvedTimes(1))
    expect(bubble.textContent).not.toMatch(/Organised by/)
    expect(bubble.querySelector('.event-preview-attendees')).toBeNull()
  })

  // Décision 7: an override's own ATTENDEE lines (its `recurrenceId` set) describe one instance's
  // answer, not the series — the bubble shows the master's organizer and guests regardless.
  it('reads the master\'s attendees, never an override\'s', async () => {
    api.getEvent.mockResolvedValue(detailOf({
      id: 'e1', attendees: [
        { email: 'marc@example.org', name: 'Marc Dupont', isOrganizer: true },
        {
          email: 'marc@example.org', name: 'Marc Dupont', isOrganizer: true,
          recurrenceId: '20260914T070000Z',
        },
        { email: 'alice@weesky.be', name: 'Alice', isOrganizer: false },
        {
          email: 'alice@weesky.be', name: 'Alice', partStat: 'DECLINED', isOrganizer: false,
          recurrenceId: '20260914T070000Z',
        },
      ],
    }))
    draw()
    expect(await screen.findByText('Organised by Marc Dupont')).toBeInTheDocument()
    expect(screen.getByText('Alice')).toBeInTheDocument()
  })
})
