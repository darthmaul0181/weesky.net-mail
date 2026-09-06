import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { calendarOf, occurrenceOf, renderInCalendar } from './calendarTestHarness'
import EventPreview from './EventPreview'
import type { Occurrence } from './calendarTypes'

/** jsdom lays nothing out, so the anchor states its own rectangle — which is all the placement
    reads anyway. */
const anchors: HTMLElement[] = []
const WIDTH = window.innerWidth

// The window's width and the anchors are global state; a file that left either behind would make
// every case after it depend on the order they run in.
afterEach(() => {
  window.innerWidth = WIDTH
  anchors.splice(0).forEach(node => node.remove())
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
  renderInCalendar(
    <EventPreview occurrence={occurrenceOf(fields)} calendar={calendarOf('a', '#3b82c4', 'Personal')}
      anchor={anchor} rect={anchor.getBoundingClientRect()}
      onClose={handlers.onClose ?? noop} onEdit={handlers.onEdit ?? noop}
      onDelete={handlers.onDelete ?? noop} />)
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

  it('reads a recurrence back', () => {
    expect(draw({ ...DENTIST, recurrenceText: 'Every 6 months' }))
      .toHaveTextContent('Every 6 months')
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
})
