import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import DeleteConfirmModal from './DeleteConfirmModal.jsx'

describe('DeleteConfirmModal', () => {
  it('renders the entity label', () => {
    render(<DeleteConfirmModal entityLabel="alice@weesky.be" onConfirm={vi.fn()} onClose={vi.fn()} loading={false} />)
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('calls onClose when Cancel is clicked', async () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('calls onConfirm when Delete is clicked', async () => {
    const onConfirm = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={onConfirm} onClose={vi.fn()} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disables both buttons while loading', () => {
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={vi.fn()} loading={true} />)
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
  })
})
