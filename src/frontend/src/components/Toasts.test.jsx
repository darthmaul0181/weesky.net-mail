import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import Toasts from './Toasts.jsx'

describe('Toasts', () => {
  it('renders nothing when the list is empty', () => {
    const { container } = render(<Toasts toasts={[]} onRemove={vi.fn()} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders a success toast without a close button', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Done', type: 'success' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Done')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders an error toast with a close button', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Oops', type: 'error' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Oops')).toBeInTheDocument()
    expect(screen.getByRole('button')).toBeInTheDocument()
  })

  it('calls onRemove with the toast id when the close button is clicked', async () => {
    const onRemove = vi.fn()
    render(<Toasts toasts={[{ id: 42, message: 'Err', type: 'error' }]} onRemove={onRemove} />)
    await userEvent.click(screen.getByRole('button'))
    expect(onRemove).toHaveBeenCalledWith(42)
  })
})
