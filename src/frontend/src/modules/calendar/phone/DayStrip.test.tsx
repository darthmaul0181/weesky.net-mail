import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { renderInCalendar } from '../calendarTestHarness'
import DayStrip from './DayStrip'

/** 16 September 2026 is a Wednesday; `RULES` opens the week on Monday. */
const WEEK = [
  '2026-09-14', '2026-09-15', '2026-09-16', '2026-09-17',
  '2026-09-18', '2026-09-19', '2026-09-20',
]

describe('DayStrip', () => {
  it('holds the seven days of the selected week in the order the region opens on', () => {
    const { container } = renderInCalendar(
      <DayStrip selected="2026-09-16" onPick={() => {}} />)
    const week = container.querySelector('[data-week="2026-09-14"]') as HTMLElement
    expect([...week.querySelectorAll('.day-strip-day')].map(one => one.getAttribute('data-day')))
      .toEqual(WEEK)
  })

  // The band scrolls to the neighbouring weeks rather than re-fetching: a swipe left is last
  // week, and the strip is the only control that says which week is on screen.
  it('carries the weeks either side of the selected one', () => {
    const { container } = renderInCalendar(
      <DayStrip selected="2026-09-16" onPick={() => {}} />)
    expect(container.querySelector('[data-week="2026-09-07"]')).not.toBeNull()
    expect(container.querySelector('[data-week="2026-09-21"]')).not.toBeNull()
  })

  it('marks the selected day and answers a tap with the day it was on', async () => {
    const onPick = vi.fn()
    const { container } = renderInCalendar(<DayStrip selected="2026-09-16" onPick={onPick} />)
    expect(container.querySelector('[data-day="2026-09-16"]')).toHaveClass('is-selected')

    await userEvent.click(container.querySelector('[data-day="2026-09-18"]') as HTMLElement)
    expect(onPick).toHaveBeenCalledWith('2026-09-18')
  })
})
