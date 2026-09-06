import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import EventChip, { type EventChipProps } from './EventChip'
import { calendarOf, occurrenceOf, renderInCalendar } from './calendarTestHarness'

const noop = () => {}

function draw(fields: Parameters<typeof occurrenceOf>[0], props: Partial<EventChipProps> = {},
  calendars = [calendarOf('a', '#3b82c4', 'Personal')]) {
  const result = renderInCalendar(
    <EventChip occurrence={occurrenceOf(fields)} color="#3b82c4" variant="column"
      onOpen={noop} onOpenEditor={noop} {...props} />, { calendars })
  return { button: screen.getByRole('button'), unmount: result.unmount }
}

const DATED = {
  eventId: 'e1', summary: 'Stand-up',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T09:00:00Z',
}

describe('EventChip', () => {
  it('draws a busy event', () => {
    expect(draw({ eventId: 'e1', summary: 'Stand-up' }).button).toHaveClass('is-busy')
  })

  it('draws a free event', () => {
    expect(draw({ eventId: 'e1', transparency: 'TRANSPARENT' }).button).toHaveClass('is-free')
  })

  it('draws a tentative event', () => {
    expect(draw({ eventId: 'e1', status: 'TENTATIVE' }).button).toHaveClass('is-tentative')
  })

  it('draws a cancelled event', () => {
    expect(draw({ eventId: 'e1', status: 'CANCELLED' }).button).toHaveClass('is-cancelled')
  })

  it('names an event that has no title', () => {
    expect(draw({ eventId: 'e1' }).button).toHaveTextContent('(No title)')
  })

  it('carries its occurrence key for the drag layer to find it by', () => {
    expect(draw({ eventId: 'e1', instanceId: '20260916T090000' }).button)
      .toHaveAttribute('data-key', 'e1#20260916T090000')
  })

  it('withholds the hour from a chip too short to hold it', () => {
    const short = draw(DATED, { style: { height: 28 } })
    expect(short.button).not.toHaveTextContent('09:00')
    short.unmount()
    expect(draw(DATED, { style: { height: 56 } }).button).toHaveTextContent('09:00')
  })

  it('withholds the place until the chip is tall enough for it', () => {
    const dated = { ...DATED, location: 'Room 3' }
    const short = draw(dated, { style: { height: 44 } })
    expect(short.button).not.toHaveTextContent('Room 3')
    short.unmount()
    expect(draw(dated, { style: { height: 112 } }).button).toHaveTextContent('Room 3')
  })

  it('opens the preview on a click and the editor on a double one', async () => {
    const onOpen = vi.fn()
    const onOpenEditor = vi.fn()
    const user = userEvent.setup()
    const { button } = draw({ eventId: 'e1', summary: 'Stand-up' }, { onOpen, onOpenEditor })

    await user.click(button)
    expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ eventId: 'e1' }), button)

    await user.dblClick(button)
    expect(onOpenEditor).toHaveBeenCalledWith(expect.objectContaining({ eventId: 'e1' }))
  })

  it('names the calendar under a row with no place of its own', () => {
    expect(draw(DATED, { variant: 'row' }).button).toHaveTextContent('Personal')
  })

  it('prefers the place to the calendar under a row', () => {
    expect(draw({ ...DATED, location: 'Room 3' }, { variant: 'row' }).button)
      .not.toHaveTextContent('Personal')
  })

  it('leads a row with the date when the day is not already named', () => {
    expect(draw(DATED, { variant: 'row', showDate: true }).button).toHaveTextContent('16 Sep')
  })
})
