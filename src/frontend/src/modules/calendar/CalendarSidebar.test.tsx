import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import CalendarSidebar from './CalendarSidebar'
import type { Calendar } from './calendarTypes'
import type { WeekRules } from './calendarLocale'

const RULES: WeekRules = { firstDay: 1, minimalDays: 4 }

function calendar(id: string, displayName: string, isDefault = false): Calendar {
  return {
    id, davName: id, displayName, description: '', color: '#3b82c4', order: 0,
    timeZone: 'Europe/Brussels', isVisible: true, isDefault,
  }
}

const CALENDARS = [calendar('a', 'Personal', true), calendar('b', 'Work')]

function draw(props: Partial<Parameters<typeof CalendarSidebar>[0]> = {}) {
  const handlers = {
    onPickDay: vi.fn(), onNewEvent: vi.fn(), onNewCalendar: vi.fn(), onRename: vi.fn(),
    onRecolour: vi.fn(), onImport: vi.fn(), onExport: vi.fn(), onDelete: vi.fn(),
    onToggleVisible: vi.fn(),
  }
  const view = render(
    <CalendarSidebar calendars={CALENDARS} anchor="2026-09-14" today="2026-09-06" rules={RULES}
      locale="en-GB" loading={false} failed={false} {...handlers} {...props} />,
  )
  return { ...handlers, ...view }
}

/** Opens one calendar's kebab. The name is in its label but not alone: the box beside it is
    already called after the calendar. */
async function openMenu(name: string) {
  await userEvent.click(screen.getByRole('button', { name: `Actions for ${name}` }))
}

describe('CalendarSidebar', () => {
  it('hands a cleared box back as a hidden calendar', async () => {
    const { onToggleVisible } = draw()
    await userEvent.click(screen.getByLabelText('Work'))
    expect(onToggleVisible).toHaveBeenCalledWith(CALENDARS[1], false)
  })

  // The collection no deletion may take: the entry stays listed, greyed, carrying its reason —
  // a row that is a different shape from its neighbours reads as a rendering fault.
  it('greys Delete on the default calendar and says why', async () => {
    draw()
    await openMenu('Personal')
    const remove = screen.getByRole('menuitem', { name: 'Delete…' })
    expect(remove).toBeDisabled()
    expect(remove).toHaveAttribute('title', 'The default calendar cannot be deleted')

    await openMenu('Work')
    expect(screen.getByRole('menuitem', { name: 'Delete…' })).toBeEnabled()
  })

  it('opens the four calendar actions from the row', async () => {
    const { onRename, onRecolour, onImport, onExport } = draw()
    for (const [label, spy] of [
      ['Rename…', onRename], ['Colour…', onRecolour],
      ['Import…', onImport], ['Export', onExport],
    ] as const) {
      await openMenu('Work')
      await userEvent.click(screen.getByRole('menuitem', { name: label }))
      expect(spy).toHaveBeenCalledWith(CALENDARS[1])
    }
  })

  it('opens a create from the heading and from the primary action', async () => {
    const { onNewCalendar, onNewEvent } = draw()
    await userEvent.click(screen.getByRole('button', { name: 'New calendar' }))
    expect(onNewCalendar).toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'New event' }))
    expect(onNewEvent).toHaveBeenCalled()
  })

  it('passes a picked day up from the mini-month', async () => {
    const { onPickDay } = draw()
    await userEvent.click(screen.getByRole('button', { name: '17 September 2026' }))
    expect(onPickDay).toHaveBeenCalledWith('2026-09-17')
  })

  // A refused list is said out loud: an empty section would claim the account holds none.
  it('says so when the list was refused', () => {
    draw({ calendars: [], failed: true })
    expect(screen.getByText('Could not load the calendar')).toBeInTheDocument()
  })
})
