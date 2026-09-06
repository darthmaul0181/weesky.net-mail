import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import MiniMonth from './MiniMonth'
import type { WeekRules } from './calendarLocale'

const RULES: WeekRules = { firstDay: 1, minimalDays: 4 }

function renderMonth(props: Partial<Parameters<typeof MiniMonth>[0]> = {}) {
  const onPick = vi.fn()
  const view = render(
    <MiniMonth anchor="2026-09-14" today="2026-09-06" rules={RULES} locale="en-GB"
      onPick={onPick} {...props} />,
  )
  return { onPick, ...view }
}

describe('MiniMonth', () => {
  // Six rows always: a grid whose height moved between September and October would shift every
  // row under the cursor. The week number is the first cell of each.
  it('draws six weeks, each opening on its week number', () => {
    const { container } = renderMonth()
    const weeks = container.querySelectorAll('.mini-month-week')
    expect(weeks).toHaveLength(6)
    expect(weeks[0].querySelector('.mini-week-number')?.textContent).toBe('36')
    expect(weeks[5].querySelector('.mini-week-number')?.textContent).toBe('41')
  })

  it('hands the picked day back whole', async () => {
    const { onPick } = renderMonth()
    await userEvent.click(screen.getByRole('button', { name: '17 September 2026' }))
    expect(onPick).toHaveBeenCalledWith('2026-09-17')
  })

  it('fills the anchor and rings today', () => {
    const { container } = renderMonth()
    expect(container.querySelector('.mini-day.is-anchor')?.textContent).toBe('14')
    expect(container.querySelector('.mini-day.is-today')?.textContent).toBe('6')
  })

  // The days the grid borrows from the months on either side are drawn, not blanked: a grid with
  // holes in it reads as a rendering fault.
  it('marks the borrowed days as outside the month', () => {
    const { container } = renderMonth()
    expect(container.querySelectorAll('.mini-day.is-outside').length).toBeGreaterThan(0)
  })

  it('walks a month at a time without moving the anchor', async () => {
    const { container, onPick } = renderMonth()
    await userEvent.click(screen.getByRole('button', { name: 'Next month' }))
    expect(container.querySelector('.mini-month-title')?.textContent).toBe('October 2026')
    expect(onPick).not.toHaveBeenCalled()
  })
})
