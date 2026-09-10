import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import SearchResults from './SearchResults'
import { occurrenceOf, renderInCalendar } from './calendarTestHarness'
import type { Occurrence } from './calendarTypes'

const noop = () => {}

const STANDUP = occurrenceOf({
  eventId: 'a1', summary: 'Stand-up',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})
const REVIEW = occurrenceOf({
  eventId: 'b1', summary: 'Review',
  startUtc: '2026-10-01T07:00:00Z', endUtc: '2026-10-01T08:00:00Z',
})

function results(occurrences: Occurrence[], props = {}, overrides = {}) {
  return renderInCalendar(
    <SearchResults occurrences={occurrences} loading={false} failed={false} tooShort={false}
      onClear={noop} onOpen={noop} onOpenEditor={noop} {...props} />, overrides)
}

describe('SearchResults', () => {
  it('counts what came back', () => {
    results([STANDUP, REVIEW])
    expect(screen.getByText('2 events found')).toBeInTheDocument()
    expect(document.querySelectorAll('.event-row')).toHaveLength(2)
  })

  it('says when the server stopped counting', () => {
    results(Array.from({ length: 200 }, (_, index) =>
      occurrenceOf({ eventId: `e${index}`, summary: `Event ${index}`,
        startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z' })))
    expect(screen.getByText('Only the first 200 results are shown')).toBeInTheDocument()
  })

  // Decision 11: the click goes to that day *in the current view*, so the box is emptied on the
  // way — results left standing would go on covering the grid they have just moved.
  it('moves the grid to the day a result sits on, and clears the search', async () => {
    const setAnchor = vi.fn()
    const onOpen = vi.fn()
    const onClear = vi.fn()
    const user = userEvent.setup()
    results([REVIEW], { onOpen, onClear }, { setAnchor })

    await user.click(screen.getByText('Review'))
    expect(setAnchor).toHaveBeenCalledWith('2026-10-01')
    expect(onClear).toHaveBeenCalled()
    expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ eventId: 'b1' }),
      expect.anything())
  })

  it('hands Clear back to whoever owns the box', async () => {
    const onClear = vi.fn()
    const user = userEvent.setup()
    results([STANDUP], { onClear })

    await user.click(screen.getByRole('button', { name: 'Clear' }))
    expect(onClear).toHaveBeenCalled()
  })

  it('waits rather than claiming nothing matched a single letter', () => {
    results([], { tooShort: true })
    expect(screen.getByText('Type at least 2 characters to search')).toBeInTheDocument()
    expect(screen.queryByText('0 events found')).toBeNull()
  })
})
