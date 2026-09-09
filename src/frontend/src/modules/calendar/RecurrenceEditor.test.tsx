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
    expect(screen.getByLabelText('Repeat every')).toHaveValue(3)
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

  // "Every day" names no weekday, and the seven boxes say so by being all lit and asleep
  // rather than gone: a row that vanishes makes the block jump under the pointer.
  it('draws the seven days lit and disabled under every day', () => {
    draw({ frequency: 'DAILY', interval: 1, byDay: [], end: 'Never' })
    const days = screen.getAllByRole('checkbox')
    expect(days).toHaveLength(7)
    days.forEach(one => {
      expect(one).toBeChecked()
      expect(one).toBeDisabled()
    })
  })

  it('keeps the days chosen when the unit moves off weekly, and drops them for daily', async () => {
    const onChange = draw({ ...WEEKLY, byDay: ['MO', 'WE'] })
    await userEvent.selectOptions(screen.getByLabelText('Unit'), 'MONTHLY')
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ frequency: 'MONTHLY', byDay: ['MO', 'WE'] }))
    await userEvent.selectOptions(screen.getByLabelText('Unit'), 'DAILY')
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ frequency: 'DAILY', byDay: [] }))
  })

  // A rule that names no day repeats on nothing: leaving daily lands on the start's own weekday.
  it('falls back on the start weekday when a rule leaves daily with no day', async () => {
    const onChange = draw(MONTHLY)
    await userEvent.selectOptions(screen.getByLabelText('Unit'), 'WEEKLY')
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ frequency: 'WEEKLY', byDay: ['FR'] }))
  })

  it('ends after a count', async () => {
    const onChange = draw()
    await userEvent.click(screen.getByRole('radio', { name: 'After' }))
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ end: 'Count', count: 10, until: undefined }))
  })

  it('ends on a date', async () => {
    const onChange = draw({ ...WEEKLY, end: 'Until', until: '2026-12-20' })
    expect(screen.getByLabelText('Repeat until')).toHaveValue('2026-12-20')
    fireEvent.change(screen.getByLabelText('Repeat until'), { target: { value: '2027-01-31' } })
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ end: 'Until', until: '2027-01-31' }))
  })

  it('keeps the counter and the date drawn, disabled, while another choice is active', () => {
    draw({ ...WEEKLY, end: 'Count', count: 4 })
    expect(screen.getByLabelText('Number of times')).toBeEnabled()
    expect(screen.getByLabelText('Repeat until')).toBeDisabled()
  })

  it('remembers the date it was given rather than falling back on the start', async () => {
    const onChange = draw({ ...WEEKLY, end: 'Count', count: 4, until: '2026-12-20' })
    await userEvent.click(screen.getByRole('radio', { name: 'on' }))
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ end: 'Until', until: '2026-12-20', count: undefined }))
  })

  it('draws the day boxes for every unit, live once the unit is not daily', () => {
    draw({ frequency: 'YEARLY', interval: 1, byDay: ['MO'], end: 'Never' })
    expect(screen.getAllByRole('checkbox')).toHaveLength(7)
    expect(screen.getByRole('checkbox', { name: 'Monday' })).toBeEnabled()
    expect(screen.getByRole('checkbox', { name: 'Monday' })).toBeChecked()
  })
})
