import { fireEvent, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { firePointer, installPointerEvents } from '../../test-utils'
import WeekView from './WeekView'
import { occurrenceOf, renderInCalendar, TZ } from './calendarTestHarness'
import type { Occurrence } from './calendarTypes'
import { todayIn } from './plainDate'

const noop = () => {}
const WEEK = ['2026-09-14', '2026-09-15', '2026-09-16', '2026-09-17', '2026-09-18',
  '2026-09-19', '2026-09-20']

/** September 2026 is CEST, so 09:00 in Brussels is 07:00Z — stated rather than derived from
    the machine's own zone, which is what a runner in UTC would otherwise answer. */
const DENTIST = occurrenceOf({
  eventId: 'a1', summary: 'Dentist',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})
const REVIEW = occurrenceOf({
  eventId: 'b1', summary: 'Review',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})
const LEAVE = occurrenceOf({
  eventId: 'c1', summary: 'Leave', isAllDay: true,
  startDate: '2026-09-17', endDateExclusive: '2026-09-18',
})
const PARTY = occurrenceOf({
  eventId: 'd1', summary: 'Party',
  startUtc: '2026-09-16T20:00:00Z', endUtc: '2026-09-17T00:00:00Z',
})
const TRIP = occurrenceOf({
  eventId: 'e1', summary: 'Trip',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-18T09:00:00Z',
})

function week(
  visible: Occurrence[], days = WEEK, today = '2026-09-16', gestures = false,
  selectedKey?: string,
) {
  return renderInCalendar(
    <WeekView days={days} gestures={gestures} selectedKey={selectedKey}
      onOpen={noop} onOpenEditor={noop} />,
    { visible, today })
}

beforeEach(installPointerEvents)

describe('WeekView', () => {
  // Decision 3: the two pieces carry one occurrence key, so the bubble's highlight reaches both
  // columns rather than the half the pointer happened to be on.
  it('lights both slices of an evening crossing midnight', () => {
    week([PARTY], WEEK, '2026-09-16', false, 'd1#')

    expect(document.querySelectorAll('.event-chip.is-selected')).toHaveLength(2)
  })

  it('lights the same pair when one of them is hovered', () => {
    week([PARTY])
    fireEvent.pointerOver(screen.getAllByRole('button', { name: /Party/ })[0])

    expect(document.querySelectorAll('.event-chip.is-hovered')).toHaveLength(2)
  })

  it('opens on the week its days name', () => {
    week([])
    expect(screen.getByText('W38')).toBeInTheDocument()
    expect(document.querySelectorAll('.day-column')).toHaveLength(7)
  })

  it('places an hour-long block on its own minute', () => {
    week([DENTIST])
    expect(screen.getByText('Dentist').closest('button')).toHaveStyle({
      top: '504px', height: '56px', left: 'calc(0 * 100% / 1)', width: 'calc(100% / 1)',
    })
  })

  it('shares the column between two blocks starting together', () => {
    week([DENTIST, REVIEW])
    expect(screen.getByText('Dentist').closest('button'))
      .toHaveStyle({ width: 'calc(100% / 2)' })
    expect(screen.getByText('Review').closest('button'))
      .toHaveStyle({ width: 'calc(100% / 2)' })
  })

  it('puts a whole day in the all-day band', () => {
    week([LEAVE])
    expect(screen.getByText('Leave').closest('.allday-band')).toBeInTheDocument()
  })

  it('cuts an evening running past midnight into one chip per day, under one key', () => {
    week([PARTY])
    const chips = screen.getAllByText('Party').map(node => node.closest('button'))
    expect(chips).toHaveLength(2)
    for (const chip of chips) expect(chip).toHaveAttribute('data-key', 'd1#')
  })

  it('bands a dated event lasting more than a day, keeping the hour it starts at', () => {
    week([TRIP])
    const chip = screen.getByText('Trip').closest('button')
    expect(chip?.closest('.allday-band')).toBeInTheDocument()
    expect(chip).toHaveTextContent('09:00')
  })

  it('draws no current-time line on a week that is not this one', () => {
    week([])
    expect(document.querySelector('.now-line')).toBeNull()
  })

  it('draws the current-time line on the day that is today', () => {
    const today = todayIn(TZ)
    week([], [today], today)
    expect(document.querySelector('.now-line')).not.toBeNull()
    expect(document.querySelector('.day-column.is-today')).not.toBeNull()
  })

  // The -8px lift that puts a label astride its own rule falls above `scrollTop: 0` for the
  // first hour, and a scroll container cannot be scrolled above its own start: midnight would
  // be drawn cut in half.
  it('draws no label on the midnight rule', () => {
    week([])
    const labels = [...document.querySelectorAll('.week-hour')].map(one => one.textContent)
    expect(labels).toHaveLength(24)
    expect(labels[0]).toBe('')
    expect(labels[1]).toBe('01:00')
  })

  it('is the day view when it is handed a single day', () => {
    week([DENTIST], ['2026-09-16'])
    expect(document.querySelectorAll('.day-column')).toHaveLength(1)
    expect(screen.getByText('Dentist')).toBeInTheDocument()
  })

  it('offers the resize grip only where gestures are live', () => {
    const off = week([DENTIST])
    expect(document.querySelector('.event-resize-handle')).toBeNull()
    off.unmount()

    week([DENTIST], WEEK, '2026-09-16', true)
    expect(screen.getByText('Dentist').closest('button')
      ?.querySelector('.event-resize-handle')).not.toBeNull()
  })

  // An evening running past midnight is two chips in two columns under one key: without the
  // slice being told which end it is, both would move and both would be stretched, and one event
  // would be drawn twice on its way to one place.
  it('moves the head of an event cut across midnight and stretches only its tail', () => {
    week([PARTY], WEEK, '2026-09-16', true)
    const [head, tail] = screen.getAllByText('Party').map(node => node.closest('button'))
    expect(head?.querySelector('.event-resize-handle')).toBeNull()
    expect(tail?.querySelector('.event-resize-handle')).not.toBeNull()

    fireEvent.pointerDown(head as HTMLElement, { clientX: 100, clientY: 500, button: 0 })
    firePointer('pointermove', 100, 556)

    expect(head).toHaveClass('is-dragging')
    expect(head).toHaveStyle({ transform: 'translate(0%, 56px)' })
    expect(tail).not.toHaveClass('is-dragging')
    expect(tail?.style.transform).toBe('')
    firePointer('pointerup', 100, 556)
  })

  // A bandeau has no hours to stretch through, so it has no foot to take hold of.
  it('gives a band chip no grip', () => {
    week([LEAVE], WEEK, '2026-09-16', true)
    expect(screen.getByText('Leave').closest('button')
      ?.querySelector('.event-resize-handle')).toBeNull()
  })
})
