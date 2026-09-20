import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import CalendarDialog from './CalendarDialog'
import { CALENDAR_COLORS } from './calendarColors'
import { fireEscape, pressBackdrop } from '../../test-utils'

function open(props: Partial<Parameters<typeof CalendarDialog>[0]> = {}) {
  const onSubmit = vi.fn()
  const onClose = vi.fn()
  const view = render(
    <CalendarDialog title="New calendar" initialName="" initialColor={CALENDAR_COLORS[0]}
      focus="name" saving={false} onSubmit={onSubmit} onClose={onClose} {...props} />,
  )
  return { onSubmit, onClose, ...view }
}

const save = () => screen.getByRole('button', { name: 'Save' })

describe('CalendarDialog', () => {
  it('keeps Save inert while the name is empty', async () => {
    const { onSubmit } = open()
    expect(save()).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Name'), 'Work')
    expect(save()).toBeEnabled()
    await userEvent.click(save())
    expect(onSubmit).toHaveBeenCalledWith({ displayName: 'Work', color: CALENDAR_COLORS[0] })
  })

  it('takes the colour from a clicked swatch', async () => {
    const { onSubmit } = open({ initialName: 'Work' })
    await userEvent.click(screen.getByRole('button', { name: 'Coral' }))
    await userEvent.click(save())
    expect(onSubmit).toHaveBeenCalledWith({ displayName: 'Work', color: CALENDAR_COLORS[3] })
  })

  // The hex box is the way out of the twelve, and a half-typed value is not a colour: refusing it
  // at the keyboard beats refusing it after a round trip.
  it('refuses a hex code that is not one', async () => {
    const { onSubmit } = open({ initialName: 'Work' })
    const hex = screen.getByLabelText('Hex code')
    await userEvent.clear(hex)
    await userEvent.type(hex, 'xyz')
    expect(save()).toBeDisabled()

    await userEvent.clear(hex)
    await userEvent.type(hex, '#abcdef')
    expect(save()).toBeEnabled()
    await userEvent.click(save())
    expect(onSubmit).toHaveBeenCalledWith({ displayName: 'Work', color: '#abcdef' })
  })

  it('is a dialog named by its own title', () => {
    open()
    expect(screen.getByRole('dialog', { name: 'New calendar' }))
      .toHaveAttribute('aria-modal', 'true')
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

  // A write in flight owns the dialog: dismissing it would leave the user with no idea whether
  // the calendar was created.
  it('withholds every way out while a write is in flight', () => {
    const { onClose } = open({ initialName: 'Work', saving: true })

    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    fireEscape()
    pressBackdrop()

    expect(onClose).not.toHaveBeenCalled()
  })

  it('opens on the field its door named', () => {
    open({ initialName: 'Work', focus: 'colour' })
    expect(screen.getByLabelText('Hex code')).toHaveFocus()
  })

  it('opens on the name when that is the door', () => {
    open()
    expect(screen.getByLabelText('Name')).toHaveFocus()
  })
})
