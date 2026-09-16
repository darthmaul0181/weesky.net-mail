import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ModalOverlay from './ModalOverlay'

function setup(onClose = vi.fn()) {
  render(
    // No stopPropagation here on purpose: the inner element must not be what defeats a bad
    // close — the overlay's own target check has to be what's under test in every case below.
    <ModalOverlay onClose={onClose}>
      <div className="modal">
        <input placeholder="field" />
      </div>
    </ModalOverlay>,
  )
  return { onClose, overlay: screen.getByRole('presentation') }
}

describe('ModalOverlay', () => {
  it('does not close when the press started inside the dialog and the click lands on the overlay', () => {
    const { onClose, overlay } = setup()
    fireEvent.mouseDown(screen.getByPlaceholderText('field'))
    fireEvent.click(overlay)

    expect(onClose).not.toHaveBeenCalled()
  })

  // The drag-out case: press starts on the backdrop (arming the flag) but the pointer is
  // released over a field inside the dialog — a user selecting text that overruns the dialog's
  // edge. The eventual click still lands on the overlay (common ancestor), so the flag alone
  // must not be enough; the mouseup has to disarm it when it isn't on the backdrop either.
  it('does not close when the press started on the backdrop but the release lands inside the dialog', () => {
    const { onClose, overlay } = setup()
    fireEvent.mouseDown(overlay)
    fireEvent.mouseUp(screen.getByPlaceholderText('field'))
    fireEvent.click(overlay)

    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes once when the press and the click both land on the overlay', () => {
    const { onClose, overlay } = setup()
    fireEvent.mouseDown(overlay)
    fireEvent.mouseUp(overlay)
    fireEvent.click(overlay)

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('does not close on a click inside the dialog', () => {
    const { onClose } = setup()
    fireEvent.mouseDown(screen.getByPlaceholderText('field'))
    fireEvent.click(screen.getByPlaceholderText('field'))

    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes on a plain user click on the overlay', async () => {
    const { onClose, overlay } = setup()
    await userEvent.click(overlay)

    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
