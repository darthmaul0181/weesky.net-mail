import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ReminderList from './ReminderList'
import { pickOption } from '../../test-utils'

function draw(reminders: number[], allDay = false, foreignAlarms: string[] = [],
  onChange = vi.fn()) {
  render(<ReminderList reminders={reminders} allDay={allDay} foreignAlarms={foreignAlarms}
    onChange={onChange} />)
  return onChange
}

describe('ReminderList', () => {
  it('reads each reminder back as a sentence', async () => {
    draw([15, 1440])
    const bells = screen.getAllByRole('combobox')
    expect(bells).toHaveLength(2)
    expect(bells[0]).toHaveTextContent('15 minutes before')
    expect(bells[1]).toHaveTextContent('1 day before')

    await userEvent.click(bells[0]!)
    expect(screen.getByRole('option', { name: '15 minutes before', selected: true }))
      .toBeInTheDocument()
    await userEvent.click(bells[0]!)
    await userEvent.click(bells[1]!)
    expect(screen.getByRole('option', { name: '1 day before', selected: true }))
      .toBeInTheDocument()
  })

  // A whole day has no hour, so the ladder is the moments the phones offer instead of distances.
  it('offers the all-day ladder when the event has no hour', async () => {
    draw([900], true)
    await userEvent.click(screen.getByRole('combobox'))
    expect(screen.getByRole('option', { name: 'The day before at 09:00', selected: true }))
      .toBeInTheDocument()
  })

  // A value a phone wrote that neither ladder holds must still be readable and keepable.
  it('keeps a value the ladder does not hold', async () => {
    draw([7])
    const box = screen.getByRole('combobox')
    expect(box).toHaveTextContent('7 minutes before')
    await userEvent.click(box)
    expect(screen.getByRole('option', { name: '7 minutes before' })).toBeInTheDocument()
  })

  it('adds a reminder', async () => {
    const onChange = draw([])
    await userEvent.click(screen.getByRole('button', { name: 'Add a reminder' }))
    expect(onChange).toHaveBeenCalledWith([15])
  })

  it('offers no more than five', () => {
    draw([0, 5, 10, 15, 30])
    expect(screen.queryByRole('button', { name: 'Add a reminder' })).toBeNull()
  })

  it('removes the one whose ✕ was pressed', async () => {
    const onChange = draw([15, 60, 1440])
    await userEvent.click(screen.getAllByRole('button', { name: 'Remove this reminder' })[1]!)
    expect(onChange).toHaveBeenCalledWith([15, 1440])
  })

  it('changes one without touching its neighbours', async () => {
    const onChange = draw([15, 60])
    await pickOption(screen.getAllByRole('combobox')[0]!, '30 minutes before')
    expect(onChange).toHaveBeenCalledWith([30, 60])
  })

  // The alarms the bell cannot show are printed rather than hidden: a save keeps them, and a list
  // that said nothing would read as the event having lost them.
  it('names the alarms this screen cannot change', () => {
    draw([15], false, ['EMAIL, 1 day before'])
    expect(screen.getByText('Reminders this screen cannot change')).toBeInTheDocument()
    expect(screen.getByText('EMAIL, 1 day before')).toBeInTheDocument()
  })

  it('says nothing about foreign alarms when there are none', () => {
    draw([15])
    expect(screen.queryByText('Reminders this screen cannot change')).toBeNull()
  })
})
