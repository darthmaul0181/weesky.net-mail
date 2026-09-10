import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import UpcomingList from './UpcomingList'
import { occurrenceOf, renderInCalendar } from './calendarTestHarness'
import type { Occurrence } from './calendarTypes'

const noop = () => {}
const DAYS = ['2026-09-16', '2026-09-17', '2026-09-18']

const COFFEE = occurrenceOf({
  eventId: 'a1', summary: 'Coffee', transparency: 'TRANSPARENT',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T07:30:00Z',
})
const STANDUP = occurrenceOf({
  eventId: 'b1', summary: 'Stand-up',
  startUtc: '2026-09-16T08:00:00Z', endUtc: '2026-09-16T08:30:00Z',
})
const TRIP = occurrenceOf({
  eventId: 'c1', summary: 'Trip',
  startUtc: '2026-09-17T07:00:00Z', endUtc: '2026-09-17T09:00:00Z',
})

function list(visible: Occurrence[]) {
  return renderInCalendar(
    <UpcomingList days={DAYS} onOpen={noop} onOpenEditor={noop} />,
    { visible, view: 'list', anchor: '2026-09-16', today: '2026-09-16' })
}

describe('UpcomingList', () => {
  it('names today and tomorrow, and skips a day holding nothing', () => {
    list([COFFEE, STANDUP, TRIP])
    // The date itself is Intl's, dash, comma and field order included: pinning it would make
    // the test the formatter's mirror rather than the product's contract.
    const headings = [...document.querySelectorAll('.upcoming-day')].map(one => one.textContent)
    expect(headings).toHaveLength(2)
    expect(headings[0]).toMatch(/^Today · .*16.*September/)
    expect(headings[1]).toMatch(/^Tomorrow · .*17.*September/)
  })

  it('runs a day in the order its events do', () => {
    list([STANDUP, COFFEE])
    const titles = [...document.querySelectorAll('.event-row-title')].map(one => one.textContent)
    expect(titles).toEqual(['Coffee', 'Stand-up'])
  })

  it('marks a free event as free rather than filling its bar', () => {
    list([COFFEE])
    expect(screen.getByText('Coffee').closest('.event-row')).toHaveClass('is-free')
  })

  it('says so when the month ahead holds nothing', () => {
    list([])
    expect(screen.getByText('Nothing planned in the coming month')).toBeInTheDocument()
  })
})
