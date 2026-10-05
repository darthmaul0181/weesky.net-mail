import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  api, ApiError, detail, floating, installLayoutMocks, user, occurrence, params, REPEAT,
  renderAt, UPDATED,
} from './calendarLayoutHarness'
import { fireEscape, firePointer, installPointerEvents, settle } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

installLayoutMocks()

describe('CalendarLayout — the bubble', () => {
  // The bubble is anchored to an event that is highlighted, and every chip of that occurrence
  // carries the highlight.
  it('lights the chip the open bubble hangs off', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))

    await screen.findByRole('dialog', { name: 'Stand-up' })
    expect(screen.getByRole('button', { name: /Stand-up/ })).toHaveClass('is-selected')
  })

  // Deleting from the bubble asks the same scope question the editor does, and reads the detail
  // the bubble's own fetch already put in the cache — never the raw rule `one.recurrenceText`
  // carries, which `deletePreviewed` never even reads any more. A rule the picker cannot draw
  // exactly gets the generic label instead.
  it.each([
    { name: 'asks the delete scope with the worded rule, never the raw one, from the bubble',
      rule: 'FREQ=MONTHLY;INTERVAL=6', exact: true, label: 'Every 6 months' },
    { name: 'falls back to the generic label on delete when the cached rule is not exact',
      rule: 'FREQ=MONTHLY;INTERVAL=6;BYSETPOS=-1', exact: false, label: 'Repeats' },
  ].map(row => [row.name, row] as const))('%s', async (_name, { rule, exact, label }) => {
    const one = { ...floating('e1', 'Dentist', '2026-09-16T09:00:00'), recurrenceText: rule }
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue({ ...detail({ repeat: REPEAT }), repeatIsExact: exact })
    renderAt('/calendar?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: /Dentist/ }))
    const bubble = await screen.findByRole('dialog', { name: 'Dentist' })
    // Waits for the bubble's own fetch to land — the cache `deletePreviewed` reads from.
    await waitFor(() => expect(bubble).toHaveTextContent(label))

    await user.click(within(bubble).getByRole('button', { name: 'Delete' }))
    await screen.findByText('Delete a recurring event')
    expect(screen.getByText(/repeats/)).toHaveTextContent(label)
    expect(document.body.textContent).not.toMatch(/FREQ=/)
  })

  // The Edit that opened the editor left with the bubble it sat in, and the bubble hands its focus
  // back to the chip on the way out: that chip is what the editor then has to return to. The
  // region stays the fallback for when it is gone too — the phone editor's case, below.
  it('hands focus back to the chip the bubble hung off', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Dentist/ }))
    const bubble = await screen.findByRole('dialog', { name: 'Dentist' })
    await user.click(within(bubble).getByRole('button', { name: 'Edit' }))
    await screen.findByLabelText('Title')

    fireEscape()

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Dentist/ })).toHaveFocus())
  })

  // Route two of the same rule: the chip goes with the event the bubble was opened on, so the
  // focus the confirm handed back to the bubble's Delete has nothing on screen to return to.
  it('hands focus to the grid column when the bubble’s own delete lands', async () => {
    let rows = [occurrence('e1', 'Stand-up')]
    api.getOccurrences.mockImplementation(async () => ({ occurrences: rows }))
    api.deleteEvent.mockImplementation(async () => { rows = []; return null })
    renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))
    const bubble = await screen.findByRole('dialog', { name: 'Stand-up' })

    await user.click(within(bubble).getByRole('button', { name: 'Delete' }))
    const confirm = await screen.findByRole('alertdialog')
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    // Asserted once the chip has actually left: focus handed to a chip about to vanish reads as
    // correct for one commit and lands on <body> in the next.
    await waitFor(() => expect(screen.queryByRole('button', { name: /Stand-up/ })).toBeNull())
    expect(document.querySelector('.calendar-main')).toHaveFocus()
  })

  // The bubble launched the confirm and is the screen behind it: one Escape answers the confirm,
  // and the bubble is still there to try again from.
  it('closes a confirm opened from the bubble, leaving the bubble open', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))
    const bubble = await screen.findByRole('dialog', { name: 'Stand-up' })
    await user.click(within(bubble).getByRole('button', { name: 'Delete' }))
    await screen.findByText(/Delete .Stand-up/)

    fireEscape()

    await waitFor(() => expect(screen.queryByText(/Delete .Stand-up/)).toBeNull())
    expect(screen.getByRole('dialog', { name: 'Stand-up' })).toBeInTheDocument()
    await settle()
    expect(api.deleteEvent).not.toHaveBeenCalled()
  })
})

describe('CalendarLayout — the grid gestures', () => {
  beforeEach(installPointerEvents)

  /** A press on the chip, a travel of `byPx` down the column, and the release. jsdom lays nothing
      out, so every box is at zero: the vertical travel is the whole of the gesture and the drop
      crosses no column. */
  async function dragChip(name: RegExp, byPx: number) {
    const chip = await screen.findByRole('button', { name })
    fireEvent.pointerDown(chip, { clientX: 100, clientY: 500, button: 0, pointerId: 1 })
    firePointer('pointermove', 100, 500 + byPx)
    firePointer('pointerup', 100, 500 + byPx)
    return chip
  }

  it('moves a lone event without asking anything', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)

    await waitFor(() => expect(api.updateEvent).toHaveBeenCalled())
    const [id, body] = api.updateEvent.mock.calls[0]!
    expect(id).toBe('e1')
    expect(body).toMatchObject({
      scope: 'All', ifHash: 'h1', start: '2026-09-16T10:00:00', end: '2026-09-16T11:00:00',
    })
    expect(body.instanceId).toBeUndefined()
    expect(screen.queryByText('Save a recurring event')).toBeNull()
  })

  it('asks the scope before moving one occurrence of a series', async () => {
    const one = floating('e1', 'Dentist', '2026-09-16T09:00:00')
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Dentist/, 56)

    // The detail `loadDetail` just fetched is what words the question — never a raw stored rule,
    // which this occurrence carries none of in the fixture anyway.
    await screen.findByText('Save a recurring event')
    expect(screen.getByText(/repeats/)).toHaveTextContent('Every 6 months')
    expect(document.body.textContent).not.toMatch(/FREQ=/)

    await user.click(await screen.findByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalled())
    expect(api.updateEvent.mock.calls[0]![1]).toMatchObject({
      scope: 'This', instanceId: '2026-09-16T09:00:00', start: '2026-09-16T10:00:00',
    })
  })

  // A rule the picker cannot draw exactly (`repeatIsExact: false`) gets the generic label on the
  // drop's scope question too — the regression guard for the whole fix, on the third site.
  it('asks the scope with the generic label when the fetched rule is not exact', async () => {
    const one = floating('e1', 'Dentist', '2026-09-16T09:00:00')
    api.getOccurrences.mockResolvedValue({ occurrences: [one] })
    api.getEvent.mockResolvedValue({ ...detail({ repeat: REPEAT }), repeatIsExact: false })
    api.updateEvent.mockResolvedValue(UPDATED)
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Dentist/, 56)

    await screen.findByText('Save a recurring event')
    expect(screen.getByText(/repeats/)).toHaveTextContent('Repeats')
    expect(document.body.textContent).not.toMatch(/FREQ=/)
  })

  it('abandons the drop when the scope question is closed', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Dentist/, 56)
    await user.click(await screen.findByRole('button', { name: 'Close' }))

    await settle()
    expect(api.updateEvent).not.toHaveBeenCalled()
  })

  // The block has to stay where the finger left it: a round trip of its own would snap it back
  // to its old slot for the width of the request, which reads as the drop having failed.
  it('shows the block where it was dropped, and puts it back on a refusal', async () => {
    let refuse = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockReturnValue(new Promise((_resolve, reject) => {
      refuse = () => reject(new ApiError('nope', 500))
    }))
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)
    // Re-read rather than kept: the chip's key carries the minute it starts at, so the block is
    // a new node the instant the cache says it moved.
    await waitFor(() => expect(screen.getByRole('button', { name: /Stand-up/ }))
      .toHaveStyle({ top: '560px' }))

    refuse()
    await waitFor(() => expect(screen.getByRole('button', { name: /Stand-up/ }))
      .toHaveStyle({ top: '504px' }))
  })

  it('sends nothing when the block was dropped where it was', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 0)

    await settle()
    expect(api.updateEvent).not.toHaveBeenCalled()
    expect(api.getEvent).not.toHaveBeenCalled()
  })

  // "Move it, then a quarter of an hour more" is one gesture in the user's head and two drops in
  // ours: sent together they carry the same ifHash and the second comes back 409.
  it('queues a second drop behind the first, on the version the first wrote', async () => {
    let hash = 'h1'
    let land = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    api.getEvent.mockImplementation(() => Promise.resolve({ ...detail(), icsHash: hash }))
    api.updateEvent.mockImplementationOnce(() => new Promise(resolve => {
      land = () => { hash = 'h2'; resolve(UPDATED) }
    })).mockResolvedValue(UPDATED)
    renderAt('/calendar?view=week&date=2026-09-16')

    await dragChip(/Stand-up/, 56)
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledTimes(1))
    await dragChip(/Stand-up/, 14)
    await settle()
    expect(api.updateEvent).toHaveBeenCalledTimes(1)

    land()
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalledTimes(2))
    expect(api.updateEvent.mock.calls[1]![1]).toMatchObject({
      ifHash: 'h2', start: '2026-09-16T10:15:00',
    })
  })

  // Google swallows the first click too: a bubble standing over the grid is dismissed by it and
  // nothing else, where an editor opening underneath would be a second answer nobody asked for.
  it('spends a click on an empty column closing an open bubble', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Stand-up')] })
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await screen.findByRole('dialog', { name: 'Stand-up' })

    const column = document.querySelectorAll('.day-column')[2] as HTMLElement
    fireEvent.pointerDown(column, { clientX: 10, clientY: 520, button: 0, pointerId: 1 })
    fireEvent.mouseDown(column, { clientX: 10, clientY: 520 })
    firePointer('pointerup', 10, 520)

    await settle()
    expect(router.state.location.pathname).toBe('/calendar')
    expect(screen.queryByRole('dialog', { name: 'Stand-up' })).toBeNull()
  })

  it('opens an hour on the slot a click on an empty column names', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.day-column')).not.toBeNull())
    const column = document.querySelectorAll('.day-column')[2] as HTMLElement

    fireEvent.pointerDown(column, { clientX: 10, clientY: 520, button: 0, pointerId: 1 })
    firePointer('pointerup', 10, 520)

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/new'))
    const search = params(router)
    expect(search.get('allDay')).toBe('0')
    expect(new Date(search.get('end') ?? '').getTime()
      - new Date(search.get('start') ?? '').getTime()).toBe(3_600_000)
  })

  // The URL carried the slot all along; the form did not read it. A creation draft is sown once
  // under one key, and that key was the same for "New event" and for every later click on the
  // grid — so the second door found the first door's draft, the next hour of the clock.
  it('sows a click on the grid with its own slot even after another draft was opened', async () => {
    const router = renderAt('/calendar?view=week&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.day-column')).not.toBeNull())

    await user.click(screen.getAllByRole('button', { name: 'New event' })[0]!)
    await screen.findByLabelText('Start date')
    await user.click(screen.getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByLabelText('Start date')).toBeNull())

    const column = document.querySelectorAll('.day-column')[2] as HTMLElement
    fireEvent.pointerDown(column, { clientX: 10, clientY: 520, button: 0, pointerId: 1 })
    firePointer('pointerup', 10, 520)
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/new'))

    // The slot the URL names, read in the zone the layout sows the form in — the machine's.
    const start = new Date(params(router).get('start') ?? '')
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
    const day = new Intl.DateTimeFormat('en-CA', {
      timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
    }).format(start)
    const clock = new Intl.DateTimeFormat('en-GB', {
      timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    }).format(start)
    expect(await screen.findByLabelText('Start date')).toHaveValue(day)
    expect(screen.getByLabelText('Start time')).toHaveValue(clock)
  })
})
