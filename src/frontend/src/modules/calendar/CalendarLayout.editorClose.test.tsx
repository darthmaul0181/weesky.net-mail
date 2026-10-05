import { onlineManager } from '@tanstack/react-query'
import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { calendarKeys } from './queries'
import {
  api, ApiError, detail, floating, installLayoutMocks, user, mount, REPEAT,
  renderAt, UPDATED,
} from './calendarLayoutHarness'
import { fireEscape, pressBackdrop, settle } from '../../test-utils'

vi.mock('../../api.js', () => import('./calendarApiMock'))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

installLayoutMocks()

describe('CalendarLayout — deleting and leaving the editor', () => {
  it('deletes a plain event behind the shared confirm', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.deleteEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    const box = (await screen.findByText('Delete “Dentist”?')).closest('.modal') as HTMLElement
    await user.click(within(box).getByRole('button', { name: 'Delete' }))
    await waitFor(() =>
      expect(api.deleteEvent).toHaveBeenCalledWith('e1', 'All', undefined, 'en'))
  })

  it('keeps the confirm open and busy until the delete lands', async () => {
    let answer: (value: null) => void = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.deleteEvent.mockReturnValue(new Promise(resolve => { answer = resolve }))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    const box = await screen.findByRole('alertdialog')
    await user.click(within(box).getByRole('button', { name: 'Delete' }))

    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(box.querySelector('.btn-danger-solid')).toBeDisabled()
    expect(box.querySelector('.spinner')).not.toBeNull()

    await act(async () => { answer(null) })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
  })

  // The toast says why; the confirm has nothing left to ask, so it closes like the calendar's.
  it('closes the confirm on a refused delete and says so', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.deleteEvent.mockRejectedValue(new ApiError('boom', 500))
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    const box = await screen.findByRole('alertdialog')
    await user.click(within(box).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(await screen.findByText('Could not delete the event')).toBeInTheDocument()
  })

  it('asks the scope before deleting a recurring event', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    api.deleteEvent.mockResolvedValue(null)
    renderAt('/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    expect(await screen.findByText('Delete a recurring event')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'This occurrence only' }))
    await waitFor(() => expect(api.deleteEvent)
      .toHaveBeenCalledWith('e1', 'This', '2026-09-16T09:00:00', 'en'))
  })

  // An obsolete bookmark is a target that no longer exists, never an invitation to create one.
  it('says so and goes back when the event is gone', async () => {
    api.getEvent.mockRejectedValue(new ApiError('Not found', 404))
    const router = renderAt('/calendar/gone/edit?view=week&date=2026-09-16')
    expect(await screen.findByText('This event no longer exists')).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // A detail left in the cache by the bubble is not proof the event still exists: the read the
  // editor makes on opening decides, and a 404 there is a bookmark gone, not a form to sow.
  it('says so and goes back when a cached event is gone by the time the editor opens', async () => {
    api.getEvent.mockRejectedValue(new ApiError('Not found', 404))
    const { router, client } = mount('/calendar?view=week&date=2026-09-16')
    await screen.findByRole('button', { name: 'Today' })
    client.setQueryData(calendarKeys.event('primary', 'e1'), detail(),
      { updatedAt: Date.now() - 120_000 })

    await router.navigate('/calendar/e1/edit?view=week&date=2026-09-16')
    expect(await screen.findByText('This event no longer exists')).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
    expect(screen.queryByLabelText('Title')).toBeNull()
  })

  // Offline, the read the editor makes on opening is paused, not fetching: it must still be waited
  // for, neither sown from the stale copy nor taken for a settled failure.
  describe('while the read of the event is paused offline', () => {
    afterEach(() => onlineManager.setOnline(true))

    it('does not sow the editor from a stale cached copy', async () => {
      api.getEvent.mockResolvedValue({ ...detail(), icsHash: 'h2' })
      const { router, client } = mount('/calendar?view=week&date=2026-09-16')
      await screen.findByRole('button', { name: 'Today' })
      client.setQueryData(calendarKeys.event('primary', 'e1'), detail({ summary: 'Old' }),
        { updatedAt: Date.now() - 120_000 })
      onlineManager.setOnline(false)

      await router.navigate('/calendar/e1/edit?view=week&date=2026-09-16')
      await settle()
      expect(screen.queryByLabelText('Title')).toBeNull()

      onlineManager.setOnline(true)
      expect(await screen.findByLabelText('Title')).toHaveValue('Dentist')
    })

    it('does not bounce on an earlier failure the paused read may still undo', async () => {
      const { router, client } = mount('/calendar?view=week&date=2026-09-16')
      await screen.findByRole('button', { name: 'Today' })
      client.setQueryData(calendarKeys.event('primary', 'e1'), detail({ summary: 'Old' }),
        { updatedAt: Date.now() - 120_000 })
      await client.fetchQuery({
        queryKey: calendarKeys.event('primary', 'e1'),
        queryFn: () => Promise.reject(new ApiError('boom', 500)),
      }).catch(() => {})
      onlineManager.setOnline(false)

      await router.navigate('/calendar/e1/edit?view=week&date=2026-09-16')
      await settle()
      expect(router.state.location.pathname).toBe('/calendar/e1/edit')

      onlineManager.setOnline(true)
      expect(await screen.findByLabelText('Title')).toHaveValue('Dentist')
      expect(router.state.location.pathname).toBe('/calendar/e1/edit')
    })
  })

  // Only a first read that failed is a target gone: a refetch failing behind an open form keeps
  // the event it already read, and closing the editor would throw the draft away.
  it('keeps the editor and what was typed when a background read of the event fails', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const { router, client } = mount('/calendar/e1/edit?view=week&date=2026-09-16')
    await user.type(await screen.findByLabelText('Title'), '!')

    const reads = api.getEvent.mock.calls.length
    api.getEvent.mockRejectedValue(new ApiError('boom', 500))
    await client.refetchQueries()
    await settle()
    expect(api.getEvent.mock.calls.length).toBeGreaterThan(reads)
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist!')
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')
  })

  // Reload is the user's lever: a read that fails there is said in the band, and the form stays.
  it('says a failed reload out loud and keeps the form', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockRejectedValue(new ApiError('conflict', 409))
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.type(await screen.findByLabelText('Title'), '!')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByText(/changed elsewhere/)
    await waitFor(() => expect(api.getEvent.mock.calls.length).toBeGreaterThan(1))

    api.getEvent.mockRejectedValue(new ApiError('Not found', 404))
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    const band = await screen.findByText('This event no longer exists')
    expect(band.closest('.editor-error')).not.toBeNull()
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist!')
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')
  })

  it('asks before dropping what was typed on the ✕', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.type(await screen.findByLabelText('Title'), '!')
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(await screen.findByText('Discard changes?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Discard' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // From 640px up the editor is a dialog like every other one: named, aria-modal, and three ways
  // out — each of them through the dirty guard the ✕ already went through.
  it('mounts the editor as a named dialog on its own routes', async () => {
    renderAt('/calendar/new?view=week&date=2026-09-14')
    const editor = await screen.findByRole('dialog', { name: 'New event' })
    expect(editor).toHaveClass('modal', 'calendar-editor')
    expect(editor).toHaveAttribute('aria-modal', 'true')
  })

  it('draws no editor dialog on the grid route', async () => {
    renderAt('/calendar?view=week&date=2026-09-14')
    await screen.findByRole('button', { name: 'Today' })
    expect(screen.queryByRole('dialog', { name: 'New event' })).toBeNull()
  })

  it.each([
    ['on Escape', fireEscape],
    ['on a press on the backdrop', pressBackdrop],
  ])('closes a clean form %s', async (_way, leave) => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')
    await screen.findByLabelText('Title')

    leave()

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // The backdrop and Escape go through the very guard the ✕ goes through: what was typed is
  // never dropped on a stray key or on a click that missed the dialog.
  it('asks before dropping what was typed, on Escape as on the ✕', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')
    await user.type(await screen.findByLabelText('Title'), '!')

    fireEscape()
    expect(await screen.findByText('Discard changes?')).toBeInTheDocument()

    // The question owns the key while it stands: one Escape answers it and the editor is still
    // there, carrying what was typed.
    fireEscape()
    await waitFor(() => expect(screen.queryByText('Discard changes?')).toBeNull())
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist!')
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')
  })

  // The question is the editor's, and `backToGrid` is only the in-app way out: the browser's Back
  // leaves the route without passing through it, and left ungated the question stood over the grid.
  it('takes the discard question away with the editor on a browser Back', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const { router } = mount(
      '/calendar/e1/edit?view=week&date=2026-09-16', '/calendar?view=week&date=2026-09-16')

    await user.type(await screen.findByLabelText('Title'), '!')
    fireEscape()
    expect(await screen.findByText('Discard changes?')).toBeInTheDocument()

    await act(async () => { await router.navigate(-1) })

    expect(router.state.location.pathname).toBe('/calendar')
    expect(screen.queryByText('Discard changes?')).toBeNull()
  })

  // Hiding the question is not dropping it: left standing in the state, it greeted the next
  // editor — an empty New event asking whether to discard, and a Discard that threw the user out of
  // the form they had just opened.
  it('leaves no discard question behind for the next editor', async () => {
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    const { router } = mount(
      '/calendar/e1/edit?view=week&date=2026-09-16', '/calendar?view=week&date=2026-09-16')

    await user.type(await screen.findByLabelText('Title'), '!')
    fireEscape()
    await screen.findByText('Discard changes?')
    await act(async () => { await router.navigate(-1) })

    await act(async () => { await router.navigate('/calendar/new?view=week&date=2026-09-16') })

    expect(await screen.findByLabelText('Title')).toHaveValue('')
    expect(screen.queryByText('Discard changes?')).toBeNull()
  })

  // A save in flight owns the editor. Without that, Escape opened the discard question over a
  // write already on the wire, and the save's own `backToGrid` then left it standing over the grid,
  // asking whether to discard what had just been saved.
  it('is inert while the save is in flight, and leaves no question behind it', async () => {
    let land = () => {}
    api.getOccurrences.mockResolvedValue({ occurrences: [floating('e1', 'Dentist')] })
    api.getEvent.mockResolvedValue(detail())
    api.updateEvent.mockImplementation(() => new Promise(resolve => { land = () => resolve(UPDATED) }))
    const router = renderAt('/calendar/e1/edit?view=week&date=2026-09-16')

    await user.type(await screen.findByLabelText('Title'), '!')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(api.updateEvent).toHaveBeenCalled())

    fireEscape()
    pressBackdrop()
    await settle()
    expect(screen.queryByText('Discard changes?')).toBeNull()
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')

    land()
    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
    expect(screen.queryByText('Discard changes?')).toBeNull()
  })

  // The scope question's own ways out all mean "no scope", and the editor under it stays exactly
  // as it was.
  it('gives Escape to the scope question alone, over the editor', async () => {
    api.getOccurrences.mockResolvedValue({
      occurrences: [floating('e1', 'Dentist', '2026-09-16T09:00:00')],
    })
    api.getEvent.mockResolvedValue(detail({ repeat: REPEAT }))
    const router = renderAt(
      '/calendar/e1/edit?view=week&date=2026-09-16&instance=2026-09-16T09:00:00')

    await user.click(await screen.findByRole('button', { name: 'Save' }))
    await screen.findByText('Save a recurring event')

    fireEscape()

    await waitFor(() => expect(screen.queryByText('Save a recurring event')).toBeNull())
    expect(screen.getByLabelText('Title')).toHaveValue('Dentist')
    expect(router.state.location.pathname).toBe('/calendar/e1/edit')
    await settle()
    expect(api.updateEvent).not.toHaveBeenCalled()
  })

  // A load that never lands must not be a room whose only door is the browser's Back button.
  it('offers a way out of the editor while it is still loading', async () => {
    api.getCalendars.mockRejectedValue(new ApiError('nope', 500))
    const router = renderAt('/calendar/new?view=week&date=2026-09-16')

    const editor = await screen.findByRole('dialog', { name: 'New event' })
    await user.click(within(editor).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/calendar'))
  })

  // The other half of the same rule: an opener still on screen is where focus belongs, and the
  // region it covered is the fallback rather than the answer.
  it('hands focus back to the button that opened it', async () => {
    renderAt('/calendar?view=week&date=2026-09-16')
    // The sidebar's, the first of the two: the floating + carries the same name, drawn at every
    // width and hidden by CSS.
    const opener = (await screen.findAllByRole('button', { name: 'New event' }))[0]!
    await user.click(opener)
    await screen.findByLabelText('Title')

    fireEscape()

    await waitFor(() => expect(opener).toHaveFocus())
  })
})
