import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import DeleteConfirmModal from './DeleteConfirmModal.jsx'
import { fireEscape } from '../test-utils'

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
})
