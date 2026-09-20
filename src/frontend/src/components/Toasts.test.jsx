import { act, fireEvent, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Toasts from './Toasts.jsx'
import { useToasts } from '../hooks/useToasts.js'

describe('Toasts', () => {
  it('renders nothing when the list is empty', () => {
    const { container } = render(<Toasts toasts={[]} onRemove={vi.fn()} />)
    expect(container.firstChild).toBeNull()
  })

  it('is a polite, non-atomic live region', () => {
    const { container } = render(<Toasts toasts={[{ id: 1, message: 'Done', type: 'success' }]} onRemove={vi.fn()} />)
    const region = container.querySelector('.toast-container')

    expect(region).toHaveAttribute('role', 'status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).toHaveAttribute('aria-atomic', 'false')
  })

  it('announces an error toast assertively', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Oops', type: 'error' }]} onRemove={vi.fn()} />)

    expect(screen.getByText('Oops').closest('.toast')).toHaveAttribute('role', 'alert')
  })

  it('renders a success toast without a close button', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Done', type: 'success' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Done')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders an error toast with a close button named Close', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Oops', type: 'error' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Oops')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument()
  })

  it('pauses on hover and resumes on mouse leave', () => {
    const onPause = vi.fn()
    const onResume = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={onResume} />)
    const toast = screen.getByText('Saved').closest('.toast')

    fireEvent.mouseEnter(toast)
    expect(onPause).toHaveBeenCalledWith(9)

    fireEvent.mouseLeave(toast)
    expect(onResume).toHaveBeenCalledWith(9)
  })

  it('pauses on focus and resumes on blur', () => {
    const onPause = vi.fn()
    const onResume = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={onResume} />)
    const toast = screen.getByText('Saved').closest('.toast')

    fireEvent.focus(toast)
    expect(onPause).toHaveBeenCalledWith(9)

    fireEvent.blur(toast)
    expect(onResume).toHaveBeenCalledWith(9)
  })

  it('calls onRemove with the toast id when the close button is clicked', async () => {
    const onRemove = vi.fn()
    render(<Toasts toasts={[{ id: 42, message: 'Err', type: 'error' }]} onRemove={onRemove} />)
    await userEvent.click(screen.getByRole('button'))
    expect(onRemove).toHaveBeenCalledWith(42)
  })

  it('renders no button when the toast carries no action', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Saved', type: 'success' }]} onRemove={() => {}} />)

    expect(screen.queryByRole('button')).toBeNull()
  })

  it('runs the action and dismisses the toast on click', async () => {
    const onClick = vi.fn()
    const onRemove = vi.fn()
    render(
      <Toasts
        toasts={[{ id: 7, message: '2 contacts added', type: 'success', action: { label: 'Undo', onClick } }]}
        onRemove={onRemove}
      />)

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))

    expect(onClick).toHaveBeenCalledTimes(1)
    expect(onRemove).toHaveBeenCalledWith(7)
  })
})

describe('useToasts', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('dismisses a plain toast after 3 seconds', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('Saved'))
    act(() => vi.advanceTimersByTime(3000))

    expect(result.current.toasts).toHaveLength(0)
  })

  // Long enough to read what happened and decide to undo it; 3 seconds is not.
  it('keeps a toast carrying an action for 8 seconds', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('2 contacts added', 'success', { label: 'Undo', onClick: () => {} }))
    act(() => vi.advanceTimersByTime(3000))
    expect(result.current.toasts).toHaveLength(1)

    act(() => vi.advanceTimersByTime(5000))
    expect(result.current.toasts).toHaveLength(0)
  })
})
