import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ImportDialog from './ImportDialog'
import type { Calendar } from './calendarTypes'
import { fireEscape, pressBackdrop } from '../../test-utils'

function calendar(id: string, displayName: string, isDefault = false): Calendar {
  return {
    id, davName: id, displayName, description: '', color: '#3b82c4', order: 0,
    timeZone: 'Europe/Brussels', isVisible: true, isDefault,
  }
}

const CALENDARS = [calendar('a', 'Personal', true), calendar('b', 'Work')]

const ICS = 'BEGIN:VCALENDAR\r\nVERSION:2.0\r\n'
  + 'X-WR-CALNAME:Belgian holidays\r\nX-APPLE-CALENDAR-COLOR:#15803d\r\n'
  + 'BEGIN:VEVENT\r\nUID:1\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n'

function open(props: Partial<Parameters<typeof ImportDialog>[0]> = {}) {
  const onImport = vi.fn()
  const onClose = vi.fn()
  const view = render(
    <ImportDialog calendars={CALENDARS} targetId="b" saving={false}
      onImport={onImport} onClose={onClose} {...props} />,
  )
  return { onImport, onClose, ...view }
}

function icsFile(name = 'holidays.ics') {
  return new File([ICS], name, { type: 'text/calendar' })
}

const submit = () => screen.getByRole('button', { name: 'Import' })

describe('ImportDialog', () => {
  it('offers the two destinations, with the calendar of the row preselected', () => {
    open()
    expect(screen.getByRole('radio', { name: 'An existing calendar' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'A new calendar' })).not.toBeChecked()
    expect(screen.getByRole('combobox')).toHaveValue('b')
  })

  // The file says what it is; asking the user to retype it is asking them to get it wrong.
  it('pre-fills the new calendar from the file header', async () => {
    open()
    await userEvent.upload(screen.getByLabelText('File'), icsFile())
    await userEvent.click(screen.getByRole('radio', { name: 'A new calendar' }))
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Belgian holidays'))
    expect(screen.getByLabelText('Hex code')).toHaveValue('#15803d')
  })

  // A head the browser cannot read (the file moved or lost its permission since the pick) says
  // nothing: the file's own name still names the calendar, so Save is not left inert and unexplained.
  it('names the new calendar after the file when its head cannot be read', async () => {
    open()
    const file = icsFile('Team events.ics')
    const head = new Blob([ICS])
    vi.spyOn(head, 'text').mockRejectedValue(new DOMException('unreadable', 'NotReadableError'))
    vi.spyOn(file, 'slice').mockReturnValue(head)
    await userEvent.upload(screen.getByLabelText('File'), file)
    await userEvent.click(screen.getByRole('radio', { name: 'A new calendar' }))
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Team events'))
    expect(submit()).toBeEnabled()
  })

  it('pours into the chosen calendar', async () => {
    const { onImport } = open()
    expect(submit()).toBeDisabled()
    const file = icsFile()
    await userEvent.upload(screen.getByLabelText('File'), file)
    await waitFor(() => expect(submit()).toBeEnabled())
    await userEvent.click(submit())
    expect(onImport).toHaveBeenCalledWith({ mode: 'existing', id: 'b', file })
  })

  it('creates the calendar and pours into it in one gesture', async () => {
    const { onImport } = open()
    const file = icsFile()
    await userEvent.upload(screen.getByLabelText('File'), file)
    await userEvent.click(screen.getByRole('radio', { name: 'A new calendar' }))
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Belgian holidays'))
    await userEvent.click(submit())
    expect(onImport).toHaveBeenCalledWith(
      { mode: 'new', file, displayName: 'Belgian holidays', color: '#15803d' })
  })

  // An input that keeps its value fires no change event when the same file is picked twice, so
  // a header cleared by hand could never be re-read from the file that wrote it.
  it('clears the box so the same file can be picked twice', async () => {
    open()
    const input = screen.getByLabelText<HTMLInputElement>('File')
    await userEvent.upload(input, icsFile())
    await waitFor(() => expect(input).toHaveValue(''))

    await userEvent.click(screen.getByRole('radio', { name: 'A new calendar' }))
    await userEvent.clear(screen.getByLabelText('Name'))
    await userEvent.upload(input, icsFile())
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Belgian holidays'))
  })

  it('is a dialog named by its own title', () => {
    open()
    expect(screen.getByRole('dialog', { name: 'Import a calendar' }))
      .toHaveAttribute('aria-modal', 'true')
  })

  // A dialog opened to choose a file opens on the box that chooses one, like its two siblings.
  it('opens on the file box', () => {
    open()
    expect(screen.getByLabelText('File')).toHaveFocus()
  })

  it('withholds every way out while the import is in flight', () => {
    const { onClose } = open({ saving: true })

    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    fireEscape()
    pressBackdrop()

    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes on the ✕, on Escape and on a press on the backdrop', async () => {
    const { onClose } = open()

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEscape()
    expect(onClose).toHaveBeenCalledTimes(2)

    pressBackdrop()
    expect(onClose).toHaveBeenCalledTimes(3)
  })
})
