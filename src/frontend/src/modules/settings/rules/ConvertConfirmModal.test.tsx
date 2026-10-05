import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { setupUser } from '../../../test-utils'
import { ConvertConfirmModal } from './ConvertConfirmModal'

describe('ConvertConfirmModal', () => {
  it('lists every incompatible rule with its reason', () => {
    const incompatible = [
      { id: '1', name: 'A', reason: 'reason A' },
      { id: '2', name: 'B', reason: 'reason B' },
    ]
    render(<ConvertConfirmModal incompatible={incompatible} onConfirm={() => {}} onClose={() => {}} />)

    expect(screen.getByText('A')).toBeInTheDocument()
    expect(screen.getByText('reason A')).toBeInTheDocument()
    expect(screen.getByText('B')).toBeInTheDocument()
    expect(screen.getByText('reason B')).toBeInTheDocument()
  })

  it('calls onConfirm when the confirm button is clicked', async () => {
    const user = setupUser()
    const onConfirm = vi.fn()
    render(<ConvertConfirmModal incompatible={[{ id: '1', name: 'A', reason: 'r' }]}
      onConfirm={onConfirm} onClose={() => {}} />)

    await user.click(screen.getByText('Delete & switch'))
    expect(onConfirm).toHaveBeenCalled()
  })

  it('closes on its named ✕', async () => {
    const user = setupUser()
    const onClose = vi.fn()
    render(<ConvertConfirmModal incompatible={[]} onConfirm={() => {}} onClose={onClose} />)

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})
