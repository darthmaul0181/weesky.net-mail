import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  api, detail, floating, installLayoutMocks, user, occurrence, params, renderAt,
} from './calendarLayoutHarness'
import { fireEscape, mockViewport } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

installLayoutMocks()

describe('CalendarLayout — the phone tier', () => {
  // A 360px screen has nowhere to hang a 300px bubble: the tap is the editor.
  it('goes straight to the editor from a chip on a phone', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar?view=day&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/e1/edit'))
    expect(screen.queryByRole('dialog', { name: 'Stand-up' })).toBeNull()
  })

  it('draws the month as a picker over the selected day’s list on a phone', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [occurrence('e1', 'Stand-up')] })
    renderAt('/calendar?view=month&date=2026-09-16')
    await screen.findByRole('button', { name: /Stand-up/ })
    expect(document.querySelector('.phone-month')).not.toBeNull()
    expect(document.querySelector('.month-view')).toBeNull()
    expect(document.querySelector('.upcoming-list')).not.toBeNull()
  })

  it('opens the editor from a row of that list, naming the occurrence', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Stand-up', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar?view=month&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: /Stand-up/ }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/e1/edit'))
    expect(params(router).get('instance')).toBe('2026-09-16T09:00:00')
  })

  it('draws a week strip over a one-day grid on a phone', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.day-strip')).not.toBeNull())
    expect(document.querySelectorAll('.day-column')).toHaveLength(1)
  })

  // The toolbar has no room for a 30ch box beside three segments: the field is the list's.
  it('searches from the head of the list on a phone', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=list&date=2026-09-16')
    const field = await screen.findByRole('searchbox', { name: 'Search events' })
    expect(field).toHaveClass('phone-search')

    await user.type(field, 'retro{Enter}')
    await screen.findByText('0 events found')
    expect(screen.getByRole('searchbox', { name: 'Search events' })).toHaveValue('retro')
  })

  it('offers the floating button over the grid', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.floating-action')).not.toBeNull())
  })

  /* Bound straight to the handler, the button hands its own event in as that handler's first
     argument: a parameter added there writes [object Object] into a bookmarkable URL, and
     TypeScript sees nothing, one arity being assignable to the shorter one. */
  it('opens an empty draft from the floating button, with nothing of its own click in the URL', async () => {
    mockViewport('phone')
    const router = renderAt('/calendar?view=day&date=2026-09-16')
    await waitFor(() => expect(document.querySelector('.floating-action')).not.toBeNull())

    await user.click(document.querySelector('.floating-action') as HTMLElement)

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar/new'))
    expect([...params(router).keys()]).toEqual(['view', 'date'])
  })

  it('has no floating button while the editor holds the screen', async () => {
    mockViewport('phone')
    renderAt('/calendar/new?view=day&date=2026-09-16')
    await screen.findByTestId('calendar-editor')
    expect(document.querySelector('.floating-action')).toBeNull()
  })

  // It is not a `Modal`, but it covers the screen and traps Tab, so it has to say so: a reader
  // left free to browse the grid behind it would be reading what no key can reach.
  it('names the full-screen editor as the modal surface it is', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    renderAt('/calendar/e1/edit?view=day&date=2026-09-16')

    const editor = await screen.findByRole('dialog', { name: 'Edit event' })
    expect(editor).toHaveClass('calendar-editor-screen')
    expect(editor).toHaveAttribute('aria-modal', 'true')
  })

  // The floating + that opened it is withheld while it stands and comes back in the very commit
  // that closes it — too late for a ref — so focus goes back to the region it covered instead of
  // dropping to <body>.
  it('hands focus back to the grid column when what opened it is gone', async () => {
    mockViewport('phone')
    renderAt('/calendar/new?view=day&date=2026-09-16')
    await screen.findByLabelText('Title')

    fireEscape()

    await waitFor(() => expect(document.querySelector('.calendar-main')).toHaveFocus())
  })

  // The screen is not a dialog, but while it stands it owns Escape and Tab exactly as one does.
  it('closes the full-screen editor on Escape, and keeps Tab inside it', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=day&date=2026-09-16')
    await screen.findByLabelText('Title')

    screen.getByRole('button', { name: 'Open navigation' }).focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus()

    fireEscape()
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  it('asks before dropping what was typed on a phone Escape', async () => {
    mockViewport('phone')
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=day&date=2026-09-16')
    await user.type(await screen.findByLabelText('Title'), '!')

    fireEscape()

    expect(await screen.findByText('Discard changes?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')
  })

  it('opens the calendars in a drawer from the hamburger', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: 'Open navigation' }))
    expect(document.querySelector('.context-drawer')).toHaveClass('is-open')
  })

  // A row of the drawer opens a dialog over it: the dialog is the topmost layer, so Tab cycles
  // inside it and Escape answers it alone — closing the column under the question being asked
  // would take the row that raised it with it.
  it('keeps the drawer standing under a dialog opened from one of its rows', async () => {
    mockViewport('phone')
    renderAt('/calendar?view=day&date=2026-09-16')
    await user.click(await screen.findByRole('button', { name: 'Open navigation' }))
    await user.click(screen.getByRole('button', { name: 'Actions for Work' }))
    await user.click(screen.getByRole('menuitem', { name: 'Rename…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Calendar settings' })

    screen.getByRole('button', { name: 'Save' }).focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(dialog.contains(document.activeElement)).toBe(true)

    fireEscape()

    await waitFor(() => expect(screen.queryByLabelText('Name')).not.toBeInTheDocument())
    expect(document.querySelector('.context-drawer')).toHaveClass('is-open')
  })
})
