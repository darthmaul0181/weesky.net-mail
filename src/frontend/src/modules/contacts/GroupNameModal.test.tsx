import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import GroupNameModal from './GroupNameModal'
import { fireEscape, pressBackdrop } from '../../test-utils'

function renderModal(props: Partial<Parameters<typeof GroupNameModal>[0]> = {}) {
  const onSubmit = vi.fn()
  const onClose = vi.fn()
  render(<GroupNameModal title="New group" initialName="" saving={false}
    onSubmit={onSubmit} onClose={onClose} {...props} />)
  return { onSubmit, onClose }
}

const field = () => screen.getByLabelText('Name')
const submit = () => screen.getByRole('button', { name: 'Save' })

describe('GroupNameModal', () => {
  // One dialog for the two gestures: it is the title that tells them apart.
  it('wears the title it is given', () => {
    renderModal({ title: 'Rename group' })

    expect(screen.getByText('Rename group')).toBeInTheDocument()
  })

  it('submits the typed name, trimmed', async () => {
    const { onSubmit } = renderModal()

    await userEvent.type(field(), '  Friends  ')
    await userEvent.click(submit())

    expect(onSubmit).toHaveBeenCalledWith('Friends')
  })

  // An empty name is no name, and neither is a space.
  it('refuses an empty name', async () => {
    renderModal()

    expect(submit()).toBeDisabled()
    await userEvent.type(field(), '   ')

    expect(submit()).toBeDisabled()
  })

  // The greyed-out button is not the only road to submit: the guard is replayed in the handler,
  // or a form submission would send an empty name to the API.
  it('refuses a form submit that bypasses the disabled button', () => {
    const { onSubmit } = renderModal({ initialName: 'Friends' })

    fireEvent.submit(document.querySelector('form')!)

    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('seeds a rename from the current name and refuses it unchanged', async () => {
    const { onSubmit } = renderModal({ initialName: 'Friends' })

    expect(field()).toHaveValue('Friends')
    expect(submit()).toBeDisabled()

    await userEvent.type(field(), ' & family')
    await userEvent.click(submit())

    expect(onSubmit).toHaveBeenCalledWith('Friends & family')
  })

  // The server column stops at 255: refusing it at the keyboard is better than refusing it after
  // a round trip.
  it('stops the field at 255 characters', () => {
    renderModal()

    expect(field()).toHaveAttribute('maxLength', '255')
  })

  it('is a dialog named by its own title', () => {
    renderModal()

    expect(screen.getByRole('dialog', { name: 'New group' }))
      .toHaveAttribute('aria-modal', 'true')
  })

  // The field, not the ✕: a box opened to be filled in opens on its field, and it is the ref that
  // says so now — `autoFocus` is set before the layer moves the focus.
  it('opens on the name field', () => {
    renderModal()

    expect(field()).toHaveFocus()
  })

  it('closes on the ✕, on Escape and on a press on the backdrop, and never submits', async () => {
    const { onClose, onSubmit } = renderModal({ initialName: 'Friends' })

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEscape()
    expect(onClose).toHaveBeenCalledTimes(2)

    pressBackdrop()
    expect(onClose).toHaveBeenCalledTimes(3)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  // A request in flight cannot be started a second time by a click. The button then wears a
  // spinner, so it has no accessible name any more: it is found by its type instead. And the
  // three ways out are inert with it: sending the user away with no word on whether the group
  // was created is the real defect.
  it('withholds the submit and every way out while a write is in flight', () => {
    const { onClose } = renderModal({ initialName: 'Friends', saving: true })

    expect(document.querySelector('button[type="submit"]')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    fireEscape()
    pressBackdrop()

    expect(onClose).not.toHaveBeenCalled()
  })
})
