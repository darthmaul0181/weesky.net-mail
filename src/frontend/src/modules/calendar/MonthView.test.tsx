import { screen } from '@testing-library/react'
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

function month(visible: Occurrence[], overrides = {}) {
  return renderInCalendar(
    <MonthView onOpen={noop} onOpenEditor={noop} />,
    { visible, anchor: '2026-09-16', view: 'month', ...overrides })
}

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
