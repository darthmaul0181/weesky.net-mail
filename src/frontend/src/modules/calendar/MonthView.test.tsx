import { fireEvent, screen, within } from '@testing-library/react'
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

/** A chip's box pinned so a click can land above or below it — jsdom lays nothing out, so the
    rectangles are the test's own. */
function pin(el: Element, top: number, bottom: number) {
  Object.defineProperty(el, 'getBoundingClientRect', {
    value: () => ({ top, bottom, left: 0, right: 100, width: 100, height: bottom - top }),
  })
}
const hourOf = (iso: string) => new Date(iso).getUTCHours()

const grid = (month = /September 2026/) => screen.getByRole('grid', { name: month })
// The digit guard, or "1 February 2026" also names the cell of the 11th.
const cell = (date: string) =>
  screen.getByRole('gridcell', { name: new RegExp(`(^|[^0-9])${date}`) })
const cellOf16 = () => cell('16 September 2026')
const press = (key: string) => fireEvent.keyDown(document.activeElement as HTMLElement, { key })

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

  // The week grid lights both slices of an evening crossing midnight; the month draws the
  // evening once, on its start day.
  it('draws an evening crossing midnight in one cell', () => {
    month([occurrenceOf({
      eventId: 'p1', summary: 'Party',
      startUtc: '2026-09-16T20:00:00Z', endUtc: '2026-09-17T00:00:00Z',
    })])

    expect(screen.getAllByText('Party')).toHaveLength(1)
  })

  // A month cell names a day and no hour: a click on its empty part opens nine to ten there.
  it('opens an hour at nine on a click on an empty cell', async () => {
    const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
    month([], { createAt })
    await userEvent.click(cellOf16())
    expect(createAt).toHaveBeenCalledTimes(1)
    const [start, end, allDay] = createAt.mock.calls[0]!
    expect(hourOf(start.toISOString())).toBe(7)   // 09:00 in Europe/Brussels, UTC+2
    expect(end.getTime() - start.getTime()).toBe(3_600_000)
    expect(allDay).toBe(false)
  })

  // The chips are stacked in order, so where the click lands among them is the one hint there is.
  it('reads the hour off the chip the click landed under or over', async () => {
    const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
    month([dated('e1', 'Stand-up', 10)], { createAt })
    const cell = cellOf16()
    pin(cell.querySelector('.event-chip')!, 40, 60)

    fireEvent.click(cell, { clientY: 80 })
    expect(hourOf(createAt.mock.calls[0]![0].toISOString())).toBe(9)   // 11:00, after 10-11
    fireEvent.click(cell, { clientY: 20 })
    expect(hourOf(createAt.mock.calls[1]![0].toISOString())).toBe(7)   // 09:00, ending at 10
  })

  it('leaves a click on a chip to the chip, and spends one on an empty cell closing a bubble', async () => {
    const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
    const onOpen = vi.fn()
    renderInCalendar(
      <MonthView onOpen={onOpen} onOpenEditor={noop} previewOpen />,
      { visible: [dated('e1', 'Stand-up', 10)], anchor: '2026-09-16', view: 'month', createAt })
    await userEvent.click(screen.getByRole('button', { name: /Stand-up/ }))
    expect(onOpen).toHaveBeenCalledTimes(1)
    await userEvent.click(cellOf16())
    expect(createAt).not.toHaveBeenCalled()
  })

  /* A month is a true two-dimensional grid — seven days across, six weeks down — and its cell
     holds several widgets rather than one, so it is the pattern's cell-entry case. */
  describe('as a grid', () => {
    it('is a grid of weeks, each carrying its week number as a row header', () => {
      month([])
      const rows = within(grid()).getAllByRole('row')

      expect(rows).toHaveLength(6)
      expect(within(rows[0]!).getByRole('rowheader')).toHaveTextContent('36')
      expect(within(rows[2]!).getByRole('rowheader')).toHaveTextContent('38')
      expect(within(rows[2]!).getAllByRole('gridcell')).toHaveLength(7)
    })

    // The day a cell stands for is carried by its position on screen and by nothing else.
    it('names every day cell with its full date', () => {
      month([])

      expect(within(grid()).getAllByRole('gridcell')).toHaveLength(42)
      expect(cell('16 September 2026')).toHaveClass('is-today')
      expect(cell('31 August 2026')).toHaveClass('is-outside')
    })

    /* With a name on the cell a reader hears the cell and not what it holds, so a Tuesday with
       three events and an empty one sound identical — and nothing says there is an F2 to press.
       The count is what the cell draws plus what it could not: the reason to go in. */
    it("counts the day's events in its name, the hidden ones included", () => {
      month([dated('a', 'One', 9), dated('b', 'Two', 10), dated('c', 'Three', 11),
        dated('d', 'Four', 12)])

      expect(cellOf16()).toHaveAccessibleName('Wednesday, 16 September 2026, 4 events')
      expect(cell('17 September 2026')).toHaveAccessibleName('Thursday, 17 September 2026')
    })

    it('names one event in the singular', () => {
      month([dated('a', 'One', 9)])

      expect(cellOf16()).toHaveAccessibleName('Wednesday, 16 September 2026, 1 event')
    })

    it('offers one tab stop for the month, on today', () => {
      month([dated('a', 'One', 9)])

      const stops = [...grid().querySelectorAll('[tabindex="0"]')]
      expect(stops).toEqual([cellOf16()])
      expect(cellOf16()).toHaveAttribute('aria-current', 'date')
    })

    /* A month that is not today's holds no current cell at all, and its first is an outside day
       of the month before — 28 September in October 2026, where Enter would create. */
    it('puts that stop on the day the URL is about when today is elsewhere', () => {
      month([], { anchor: '2026-10-15' })

      const stops = [...grid(/October 2026/).querySelectorAll('[tabindex="0"]')]
      expect(stops).toEqual([cell('15 October 2026')])
      expect(cell('15 October 2026')).toHaveAttribute('aria-selected', 'true')
    })

    /* The two months the brief names, and the grid's own shape is what they turn on: February
       2026 opens on a Sunday, so its first row is six days of January, and August 2026 genuinely
       spans six weeks — the 31st is in the last row, where ArrowDown has nowhere left to go. */
    it('keeps its six rows and clamps at their edges whatever the month', () => {
      month([], { anchor: '2026-02-15' })
      expect(within(grid(/February 2026/)).getAllByRole('row')).toHaveLength(6)
      expect(cell('1 February 2026')).not.toHaveClass('is-outside')
      expect(cell('26 January 2026')).toHaveClass('is-outside')

      cell('26 January 2026').focus()
      press('ArrowUp')
      expect(cell('26 January 2026')).toHaveFocus()
      press('ArrowLeft')
      expect(cell('26 January 2026')).toHaveFocus()
    })

    it('reaches the last day of a month that spans six weeks', () => {
      month([], { anchor: '2026-08-15' })
      expect(cell('31 August 2026')).not.toHaveClass('is-outside')

      cell('31 August 2026').focus()
      press('ArrowDown')

      expect(cell('31 August 2026')).toHaveFocus()
    })

    it('walks day to day with the arrow keys, and week to week vertically', () => {
      month([])
      cell('16 September 2026').focus()

      press('ArrowRight')
      expect(cell('17 September 2026')).toHaveFocus()
      press('ArrowDown')
      expect(cell('24 September 2026')).toHaveFocus()
      press('ArrowLeft')
      expect(cell('23 September 2026')).toHaveFocus()
      press('ArrowUp')
      expect(cell('16 September 2026')).toHaveFocus()
    })

    /* Enter creates rather than entering the cell: creating is the cell's primary action and has
       to mean the same thing on an empty day as on a full one. F2 is the documented way in. The
       hour is the click path's own rule, which invents nothing a key cannot supply. */
    it('creates an event on the focused day with Enter, at nine on an empty one', () => {
      const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
      month([dated('a', 'One', 9)], { createAt })
      cell('17 September 2026').focus()

      press('Enter')

      expect(createAt).toHaveBeenCalledTimes(1)
      const [start, end, allDay] = createAt.mock.calls[0]!
      expect(start.toISOString()).toBe('2026-09-17T07:00:00.000Z')   // 09:00 in Brussels
      expect(end.getTime() - start.getTime()).toBe(3_600_000)
      expect(allDay).toBe(false)
    })

    // The same rule a click below the last chip lands on: after it, rounded up to the quarter.
    it("creates after the day's last chip when it holds one", () => {
      const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
      month([dated('a', 'One', 9), dated('b', 'Two', 10)], { createAt })
      cellOf16().focus()

      press('Enter')

      expect(createAt.mock.calls[0]![0].toISOString()).toBe('2026-09-16T09:00:00.000Z')  // 11:00
    })

    /* The click's own rule, which Enter has to keep: the bubble holds no trap, so Shift+Tab
       comes back to the grid with it still standing, and Enter drew a draft on top of it. */
    it('refuses Enter while a bubble stands rather than creating under it', () => {
      const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
      month([dated('a', 'One', 9)], { createAt }, { previewOpen: true })
      cell('17 September 2026').focus()

      press('Enter')

      expect(createAt).not.toHaveBeenCalled()
    })

    // Enter on a chip is the chip's: without the guard it opens the bubble AND a draft.
    it('leaves Enter on a chip inside the cell to the chip', () => {
      const createAt = vi.fn<(start: Date, end: Date, allDay: boolean) => void>()
      month([dated('a', 'One', 9)], { createAt })
      cellOf16().focus()
      press('F2')

      press('Enter')

      expect(createAt).not.toHaveBeenCalled()
    })

    it("reaches the day's chips with F2 and comes back with Escape", () => {
      month([dated('a', 'One', 9), dated('b', 'Two', 10)])
      const day = cell('16 September 2026')
      day.focus()

      press('F2')
      expect(screen.getByRole('button', { name: /One/ })).toHaveFocus()
      press('ArrowDown')
      expect(screen.getByRole('button', { name: /Two/ })).toHaveFocus()

      press('Escape')
      expect(day).toHaveFocus()
      press('ArrowRight')
      expect(cell('17 September 2026')).toHaveFocus()
    })

    // The count is a widget of the cell like the chips, and the last one it draws.
    it('reaches the "+N more" count from the keyboard too', () => {
      month([dated('a', 'One', 9), dated('b', 'Two', 10), dated('c', 'Three', 11),
        dated('d', 'Four', 12)])
      cell('16 September 2026').focus()

      press('F2')
      press('ArrowDown')
      press('ArrowDown')
      press('ArrowDown')

      expect(screen.getByRole('button', { name: '+1 more' })).toHaveFocus()
    })
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
