import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { renderInCalendar } from './calendarTestHarness'
import type { RecurrenceWrite } from './calendarTypes'
import RecurrenceEditor from './RecurrenceEditor'

const WEEKLY: RecurrenceWrite = { frequency: 'WEEKLY', interval: 1, byDay: ['MO'], end: 'Never' }

/** The editor is controlled, so the test has to hold the rule the way the form does — otherwise
    the second gesture of a two-step case acts on the first one's screen. */
function Controlled({ start, onChange }: {
  start: RecurrenceWrite; onChange: (rule: RecurrenceWrite) => void
}) {
  const [rule, setRule] = useState(start)
  return (
    <RecurrenceEditor value={rule} startDate="2026-09-25"
      onChange={next => { setRule(next); onChange(next) }} />
  )
}

function draw(value: RecurrenceWrite = WEEKLY, onChange = vi.fn()) {
  renderInCalendar(<Controlled start={value} onChange={onChange} />)
  return onChange
}

const MONTHLY: RecurrenceWrite = { frequency: 'MONTHLY', interval: 1, byDay: [], end: 'Never' }

describe('RecurrenceEditor', () => {
  it('shows the interval and the unit', () => {
    draw({ ...WEEKLY, interval: 3 })
    expect(screen.getByLabelText('Every')).toHaveValue(3)
    expect(screen.getByLabelText('Unit')).toHaveValue('WEEKLY')
  })

  it('offers the seven days on a weekly rule, in the region\'s own order', () => {
    draw()
    const days = screen.getAllByRole('checkbox')
    expect(days).toHaveLength(7)
    expect(days[0]).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Monday' })).toBeChecked()
  })

  it('adds a day to a weekly rule', async () => {
    const onChange = draw()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Wednesday' }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ byDay: ['MO', 'WE'] }))
  })

  // The whole point of the monthly branch: "the last Friday" is a position and a weekday, never
  // a day number, and the rule has to come out complete.
  it('writes a monthly rule on the last Friday', async () => {
    const onChange = draw(MONTHLY)
    await userEvent.click(screen.getByRole('radio', { name: 'Weekday of the month' }))
    await userEvent.selectOptions(screen.getByLabelText('Position'), 'last')
    await userEvent.selectOptions(screen.getByLabelText('Weekday'), 'FR')

    expect(onChange).toHaveBeenLastCalledWith({
      frequency: 'MONTHLY', interval: 1, byDay: [], end: 'Never',
      bySetPos: -1, bySetPosDay: 'FR', byMonthDay: undefined,
    })
  })

  it('writes a monthly rule on a day number', async () => {
    const onChange = draw({ ...MONTHLY, bySetPos: 1, bySetPosDay: 'FR' })
    await userEvent.click(screen.getByRole('radio', { name: 'Day of the month' }))
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({
      byMonthDay: 25, bySetPos: undefined, bySetPosDay: undefined,
    }))
  })

  it('ends after a count', async () => {
    const onChange = draw()
    await userEvent.click(screen.getByRole('radio', { name: 'After' }))
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ end: 'Count', count: 10, until: undefined }))
  })

  it('ends on a date', async () => {
    const onChange = draw({ ...WEEKLY, end: 'Until', until: '2026-12-20' })
    expect(screen.getByLabelText('End date')).toHaveValue('2026-12-20')
    fireEvent.change(screen.getByLabelText('End date'), { target: { value: '2027-01-31' } })
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ end: 'Until', until: '2027-01-31' }))
  })

  it('drops the day boxes when the rule is not weekly', () => {
    draw({ frequency: 'YEARLY', interval: 1, byDay: [], end: 'Never' })
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('radio', { name: 'Day of the month' })).toBeNull()
  })
})
