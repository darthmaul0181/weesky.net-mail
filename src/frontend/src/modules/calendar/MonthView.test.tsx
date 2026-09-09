import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import MonthView from './MonthView'
import { occurrenceOf, renderInCalendar } from './calendarTestHarness'
import type { Occurrence } from './calendarTypes'

const noop = () => {}

function dated(id: string, summary: string, hour: number): Occurrence {
  const at = (h: number) => `2026-09-16T${String(h - 2).padStart(2, '0')}:00:00Z`
  return occurrenceOf({ eventId: id, summary, startUtc: at(hour), endUtc: at(hour + 1) })
}

function tentative(id: string, summary: string): Occurrence {
  return occurrenceOf({
    eventId: id, summary, status: 'TENTATIVE',
    startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
  })
}

function month(visible: Occurrence[], overrides = {}, props = {}) {
  return renderInCalendar(
    <MonthView onOpen={noop} onOpenEditor={noop} {...props} />,
    { visible, anchor: '2026-09-16', view: 'month', ...overrides })
}

/** The cell of 16 September, and a chip's box pinned so a click can land above or below it —
    jsdom lays nothing out, so the rectangles are the test's own. */
function cellOf16() {
  return [...document.querySelectorAll<HTMLElement>('.month-cell')]
    .find(cell => cell.querySelector('.month-day-number')?.textContent === '16'
      && !cell.classList.contains('is-outside'))!
}
function pin(el: Element, top: number, bottom: number) {
  Object.defineProperty(el, 'getBoundingClientRect', {
    value: () => ({ top, bottom, left: 0, right: 100, width: 100, height: bottom - top }),
  })
}
const hourOf = (iso: string) => new Date(iso).getUTCHours()

describe('MonthView', () => {
  it('always draws the six rows the grid holds', () => {
    month([])
    expect(document.querySelectorAll('.month-week-number')).toHaveLength(6)
    expect(document.querySelectorAll('.month-cell')).toHaveLength(42)
  })

  it('greys the days that belong to another month', () => {
    month([])
    expect(document.querySelector('.month-cell.is-outside')).not.toBeNull()
  })

  it('counts what a cell could not hold', async () => {
    month([dated('a', 'One', 9), dated('b', 'Two', 10), dated('c', 'Three', 11),
      dated('d', 'Four', 12)])

    expect(screen.getByText('One')).toBeInTheDocument()
    expect(screen.queryByText('Four')).toBeNull()
    expect(await screen.findByRole('button', { name: '+1 more' })).toBeInTheDocument()
  })

  // A month cell paints no fill, so the dot is the whole of the distinction: busy and tentative
  // drawn alike would leave three renderings of four legible.
  it('keeps a tentative event apart from a confirmed one', () => {
    month([dated('a', 'One', 9), tentative('b', 'Two')])

    expect(screen.getByText('One').closest('button')).toHaveClass('is-month', 'is-busy')
    const chip = screen.getByText('Two').closest('button')
    expect(chip).toHaveClass('is-month', 'is-tentative')
    expect(chip?.querySelector('.event-dot')).not.toBeNull()
  })

  // The band keeps its hatching: it has a fill to hatch, which a dated line has not.
  it('leaves a tentative whole day hatched rather than dotted', () => {
    month([occurrenceOf({
      eventId: 'c', summary: 'Leave', status: 'TENTATIVE', isAllDay: true,
      startDate: '2026-09-16', endDateExclusive: '2026-09-17',
    })])

    const chip = screen.getByText('Leave').closest('button')
    expect(chip).toHaveClass('is-band', 'is-tentative')
    expect(chip?.querySelector('.event-dot')).toBeNull()
  })

  // Decision 3 speaks of the week grid: the month draws the evening once, on its start day.
  it('draws an evening crossing midnight in one cell', () => {
    month([occurrenceOf({
      eventId: 'p1', summary: 'Party',
      startUtc: '2026-09-16T20:00:00Z', endUtc: '2026-09-17T00:00:00Z',
    })])

    expect(screen.getAllByText('Party')).toHaveLength(1)
  })

  // A month cell names a day and no hour: a click on its empty part opens nine to ten there.
  it('opens an hour at nine on a click on an empty cell', async () => {
    const createAt = vi.fn()
    month([], { createAt })
    await userEvent.click(cellOf16())
    expect(createAt).toHaveBeenCalledTimes(1)
    const [start, end, allDay] = createAt.mock.calls[0]
    expect(hourOf(start.toISOString())).toBe(7)   // 09:00 in Europe/Brussels, UTC+2
    expect(end.getTime() - start.getTime()).toBe(3_600_000)
    expect(allDay).toBe(false)
  })

  // The chips are stacked in order, so where the click lands among them is the one hint there is.
  it('reads the hour off the chip the click landed under or over', async () => {
    const createAt = vi.fn()
    month([dated('e1', 'Stand-up', 10)], { createAt })
    const cell = cellOf16()
    pin(cell.querySelector('.event-chip')!, 40, 60)

    fireEvent.click(cell, { clientY: 80 })
    expect(hourOf(createAt.mock.calls[0][0].toISOString())).toBe(9)   // 11:00, after 10-11
    fireEvent.click(cell, { clientY: 20 })
    expect(hourOf(createAt.mock.calls[1][0].toISOString())).toBe(7)   // 09:00, ending at 10
  })

  it('leaves a click on a chip to the chip, and spends one on an empty cell closing a bubble', async () => {
    const createAt = vi.fn()
    const onOpen = vi.fn()
    renderInCalendar(
      <MonthView onOpen={onOpen} onOpenEditor={noop} previewOpen />,
      { visible: [dated('e1', 'Stand-up', 10)], anchor: '2026-09-16', view: 'month', createAt })
    await userEvent.click(screen.getByRole('button', { name: /Stand-up/ }))
    expect(onOpen).toHaveBeenCalledTimes(1)
    await userEvent.click(cellOf16())
    expect(createAt).not.toHaveBeenCalled()
  })

  it('opens the day the count was clicked on', async () => {
    const setView = vi.fn()
    const setAnchor = vi.fn()
    const user = userEvent.setup()
    month([dated('a', 'One', 9), dated('b', 'Two', 10), dated('c', 'Three', 11),
      dated('d', 'Four', 12)], { setView, setAnchor })

    await user.click(screen.getByRole('button', { name: '+1 more' }))
    expect(setView).toHaveBeenCalledWith('day')
    expect(setAnchor).toHaveBeenCalledWith('2026-09-16')
  })
})
