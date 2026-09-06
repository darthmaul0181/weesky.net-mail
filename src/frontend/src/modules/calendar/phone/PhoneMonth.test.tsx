import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { renderInCalendar } from '../calendarTestHarness'
import PhoneMonth from './PhoneMonth'

const DOTS = new Map([['2026-09-16', ['#a1a1a1', '#b2b2b2', '#c3c3c3', '#d4d4d4']]])

describe('PhoneMonth', () => {
  it('draws the six rows of seven the desktop month draws', () => {
    const { container } = renderInCalendar(
      <PhoneMonth anchor="2026-09-16" selected="2026-09-16" dotsByDay={new Map()}
        onPick={() => {}} />)
    expect(container.querySelectorAll('.phone-month-cell')).toHaveLength(42)
    expect(container.querySelectorAll('.phone-month-week')).toHaveLength(6)
  })

  // Three is what a 48px cell holds legibly; a fourth would be drawn outside it.
  it('never draws more than three dots on a day', () => {
    const { container } = renderInCalendar(
      <PhoneMonth anchor="2026-09-16" selected="2026-09-16" dotsByDay={DOTS}
        onPick={() => {}} />)
    const cell = container.querySelector('[data-day="2026-09-16"]') as HTMLElement
    expect(cell.querySelectorAll('.phone-month-dot')).toHaveLength(3)
    expect(container.querySelectorAll('.phone-month-dot')).toHaveLength(3)
  })

  it('marks the selected day and answers a tap with the day it was on', async () => {
    const onPick = vi.fn()
    const { container } = renderInCalendar(
      <PhoneMonth anchor="2026-09-16" selected="2026-09-16" dotsByDay={new Map()}
        onPick={onPick} />)
    expect(container.querySelector('[data-day="2026-09-16"]')).toHaveClass('is-selected')

    await userEvent.click(container.querySelector('[data-day="2026-09-21"]') as HTMLElement)
    expect(onPick).toHaveBeenCalledWith('2026-09-21')
  })

  // The grid follows the anchor's month, and the days either side of it are drawn muted rather
  // than left blank: a hole in a calendar reads as a rendering fault.
  it('draws the days around the month as outside it', () => {
    const { container } = renderInCalendar(
      <PhoneMonth anchor="2026-09-16" selected="2026-09-16" dotsByDay={new Map()}
        onPick={() => {}} />)
    expect(container.querySelector('[data-day="2026-08-31"]')).toHaveClass('is-outside')
    expect(container.querySelector('[data-day="2026-09-01"]')).not.toHaveClass('is-outside')
    expect(screen.getByRole('button', { name: '16 September 2026' })).toBeInTheDocument()
  })
})
