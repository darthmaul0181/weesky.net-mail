import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useRef, useState } from 'react'
import { describe, it, expect, vi } from 'vitest'
import DeleteConfirmModal from './DeleteConfirmModal.jsx'
import { fireEscape, pressBackdrop } from '../test-utils'

describe('DeleteConfirmModal', () => {
  // A confirm interrupts to ask one question, so it is an alertdialog rather than a dialog.
  it('is an alertdialog named by its own title', () => {
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={vi.fn()} />)

    const dialog = screen.getByRole('alertdialog', { name: 'Confirm deletion' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
  })

  it('takes the title override as its name', () => {
    render(<DeleteConfirmModal title="Discard changes?" onConfirm={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByRole('alertdialog', { name: 'Discard changes?' })).toBeInTheDocument()
  })

  // Owner decision: every dialog carrying a ✕ closes on Escape too.
  it('closes on Escape', () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} />)

    fireEscape()

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('takes the focus on open and hands it back to the opener on close', () => {
    function Host({ open }) {
      return (
        <div>
          <button type="button">Delete alice</button>
          {open && <DeleteConfirmModal entityLabel="alice" onConfirm={vi.fn()} onClose={vi.fn()} />}
        </div>
      )
    }
    const { rerender } = render(<Host open={false} />)
    const trigger = screen.getByRole('button', { name: 'Delete alice' })
    trigger.focus()

    rerender(<Host open />)
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()

    rerender(<Host open={false} />)
    expect(trigger).toHaveFocus()
  })

  it('renders the entity label', () => {
    render(<DeleteConfirmModal entityLabel="alice@weesky.be" onConfirm={vi.fn()} onClose={vi.fn()} loading={false} />)
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('calls onClose when ✕ is clicked — the only way out, no Cancel button', async () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} loading={false} />)
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('renders the message override instead of the default line', () => {
    render(<DeleteConfirmModal message="Custom warning" onConfirm={vi.fn()} onClose={vi.fn()} loading={false} />)
    expect(screen.getByText('Custom warning')).toBeInTheDocument()
  })

  it('calls onConfirm when Delete is clicked', async () => {
    const onConfirm = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={onConfirm} onClose={vi.fn()} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disables the confirm button while loading', () => {
    const { container } = render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={vi.fn()} loading={true} />)
    expect(container.querySelector('.btn-primary')).toBeDisabled()
  })

  // The delete is already on the wire and no dismissal cancels it: a confirm that closed under it
  // leaves the user with no feedback, and re-confirming the same row sends a second DELETE.
  it('offers no way out while the delete is in flight', async () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} loading={true} />)

    fireEscape()
    pressBackdrop()

    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    expect(onClose).not.toHaveBeenCalled()
  })

  // Deleting the last row of a list takes the button that opened this with it, so there is no
  // opener left to hand the focus back to.
  it('hands focus to the return ref when the opener goes with the confirmed action', () => {
    // 0: the row alone — 1: its confirm over it — 2: the row is deleted and the confirm with it.
    function Host({ phase }) {
      const region = useRef(null)
      return (
        <div>
          <div data-testid="region" tabIndex={-1} ref={region} />
          {phase < 2 && <button type="button">Delete alice</button>}
          {phase === 1 && (
            <DeleteConfirmModal entityLabel="alice" onConfirm={vi.fn()} onClose={vi.fn()}
              returnFocusRef={region} />
          )}
        </div>
      )
    }
    const { rerender } = render(<Host phase={0} />)
    screen.getByRole('button', { name: 'Delete alice' }).focus()
    rerender(<Host phase={1} />)

    rerender(<Host phase={2} />)

    expect(screen.getByTestId('region')).toHaveFocus()
  })

  // The opener's reachability is not the question after a confirmed action: a list refetch may
  // remove it a commit later, and nothing re-checks then.
  it('prefers the return ref over an opener the confirmed action left standing', async () => {
    render(<Host />)

    await userEvent.click(screen.getByRole('button', { name: 'Delete alice' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    expect(screen.getByTestId('region')).toHaveFocus()
  })

  // The other half of the same decision: nothing was deleted, so the button that asked is still
  // the place the user is working from.
  it('hands focus back to the opener when the dialog is cancelled', async () => {
    render(<Host />)
    const trigger = screen.getByRole('button', { name: 'Delete alice' })

    await userEvent.click(trigger)
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(trigger).toHaveFocus()
  })
})

/** A row whose delete button survives the confirmed deletion, as one waiting on a list refetch
    does: what focus lands on is a decision, not a question about that button. */
function Host() {
  const region = useRef(null)
  const [open, setOpen] = useState(false)
  return (
    <div>
      <div data-testid="region" tabIndex={-1} ref={region} />
      <button type="button" onClick={() => setOpen(true)}>Delete alice</button>
      {open && (
        <DeleteConfirmModal entityLabel="alice" onConfirm={() => setOpen(false)}
          onClose={() => setOpen(false)} returnFocusRef={region} />
      )}
    </div>
  )
}
