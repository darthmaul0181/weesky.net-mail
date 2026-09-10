import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import CalendarToolbar from './CalendarToolbar'

function draw(props: Partial<Parameters<typeof CalendarToolbar>[0]> = {}) {
  const handlers = {
    onOpenDrawer: vi.fn(), onQuery: vi.fn(), onCommitQuery: vi.fn(), onToday: vi.fn(),
    onStep: vi.fn(), onView: vi.fn(),
  }
  const view = render(
    <CalendarToolbar view="week" title="14 – 20 September 2026" weekNumber={38} query=""
      phone={false} inDrawer={false} {...handlers} {...props} />,
  )
  return { ...handlers, ...view }
}

describe('CalendarToolbar', () => {
  it('names the period and the week under it', () => {
    const { container } = draw()
    expect(container.querySelector('.calendar-title')?.textContent).toBe('14 – 20 September 2026')
    expect(container.querySelector('.calendar-subtitle')?.textContent).toBe('Week 38')
  })

  // A month spans five or six weeks and a list thirty-one days: neither has one to name.
  it('drops the week line where a week number means nothing', () => {
    const { container } = draw({ view: 'month', weekNumber: null })
    expect(container.querySelector('.calendar-subtitle')).toBeNull()
  })

  it('steps a period each way', async () => {
    const { onStep } = draw()
    await userEvent.click(screen.getByRole('button', { name: 'Previous period' }))
    expect(onStep).toHaveBeenCalledWith(-1)
    await userEvent.click(screen.getByRole('button', { name: 'Next period' }))
    expect(onStep).toHaveBeenCalledWith(1)
  })

  it('returns to today', async () => {
    const { onToday } = draw()
    await userEvent.click(screen.getByRole('button', { name: 'Today' }))
    expect(onToday).toHaveBeenCalled()
  })

  // Its own name, never the module's: the editor's calendar picker is also called "Calendar",
  // and a screen reader announcing both the same way names neither.
  it('names the view group after what it chooses', () => {
    draw()
    expect(screen.getByRole('radiogroup', { name: 'View' })).toBeInTheDocument()
  })

  it('offers the four views and lights the live one', async () => {
    const { onView } = draw()
    const views = screen.getAllByRole('radio').map(input => (input as HTMLInputElement).value)
    expect(views).toEqual(['day', 'week', 'month', 'list'])
    expect(screen.getByRole('radio', { name: 'Week' })).toBeChecked()
    await userEvent.click(screen.getByRole('radio', { name: 'Month' }))
    expect(onView).toHaveBeenCalledWith('month')
  })

  // Enter is the way past the 300ms the layout otherwise waits for: a searcher who has
  // finished typing should not have to wait to find out.
  it('commits the query on Enter', async () => {
    const { onCommitQuery } = draw({ query: 'retro' })
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search events' }), '{Enter}')
    expect(onCommitQuery).toHaveBeenCalled()
  })

  // A 360px band has no room for a fourth segment and a 30ch box: the phone searches from the
  // list (task 7) and reads a week as seven separate days.
  it('drops the week segment and the search box on a phone', () => {
    draw({ view: 'day', phone: true, weekNumber: 38 })
    expect(screen.getAllByRole('radio').map(input => (input as HTMLInputElement).value))
      .toEqual(['month', 'day', 'list'])
    expect(screen.queryByPlaceholderText('Search events')).toBeNull()
  })

  it('carries the drawer handle only where the sidebar is a drawer', async () => {
    const { onOpenDrawer } = draw({ inDrawer: true })
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    expect(onOpenDrawer).toHaveBeenCalled()
  })
})
