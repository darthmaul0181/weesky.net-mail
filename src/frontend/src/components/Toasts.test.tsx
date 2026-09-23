import { act, fireEvent, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Toasts from './Toasts'
import { useToasts } from '../hooks/useToasts'

describe('Toasts', () => {
  // A live region announces only what changes *inside* an already-present region — one created
  // together with its first child is routinely not announced at all, which was the commonest
  // case (one toast into an empty stack) failing the task's headline requirement. The empty
  // container costs nothing on screen: no padding, no background, position: fixed with no
  // children sizes to 0x0.
  it('renders an empty, polite live region when the list is empty', () => {
    const { container } = render(<Toasts toasts={[]} onRemove={vi.fn()} />)
    const region = container.querySelector('.toast-container')!

    expect(region).toHaveAttribute('role', 'status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region.children).toHaveLength(0)
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

  it('pauses on hover (a real mouse) and resumes on pointer leave', () => {
    const onPause = vi.fn()
    const onResume = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={onResume} />)
    const toast = screen.getByText('Saved').closest('.toast')!

    fireEvent.pointerEnter(toast, { pointerType: 'mouse' })
    expect(onPause).toHaveBeenCalledWith(9)

    fireEvent.pointerLeave(toast, { pointerType: 'mouse' })
    expect(onResume).toHaveBeenCalledWith(9)
  })

  it('pauses on focus and resumes on blur', () => {
    const onPause = vi.fn()
    const onResume = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={onResume} />)
    const toast = screen.getByText('Saved').closest('.toast')!

    fireEvent.focus(toast)
    expect(onPause).toHaveBeenCalledWith(9)

    fireEvent.blur(toast)
    expect(onResume).toHaveBeenCalledWith(9)
  })

  // WCAG 2.2.1: a pause the keyboard armed must survive the mouse merely passing over and
  // leaving — without reason-counting the toast would vanish under a keyboard user reading it.
  it('does not resume while focus still holds the toast, even after the mouse leaves', () => {
    const onPause = vi.fn()
    const onResume = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={onResume} />)
    const toast = screen.getByText('Saved').closest('.toast')!

    fireEvent.focus(toast)
    fireEvent.pointerEnter(toast, { pointerType: 'mouse' })
    onPause.mockClear()

    fireEvent.pointerLeave(toast, { pointerType: 'mouse' })
    expect(onResume).not.toHaveBeenCalled()

    fireEvent.blur(toast)
    expect(onResume).toHaveBeenCalledWith(9)
  })

  // A tap emulates pointerenter with no matching pointerleave until the next interaction
  // elsewhere — a touch user has no hover intent, so a coarse pointer must never pause at all.
  it('ignores a touch tap for the hover half', () => {
    const onPause = vi.fn()
    render(
      <Toasts toasts={[{ id: 9, message: 'Saved', type: 'success' }]} onRemove={vi.fn()}
        onPause={onPause} onResume={vi.fn()} />)
    const toast = screen.getByText('Saved').closest('.toast')!

    fireEvent.pointerEnter(toast, { pointerType: 'touch' })

    expect(onPause).not.toHaveBeenCalled()
  })

  it('the close button is type="button", not a form submitter', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Oops', type: 'error' }]} onRemove={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Close' })).toHaveAttribute('type', 'button')
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
    act(() => { vi.advanceTimersByTime(3000) })

    expect(result.current.toasts).toHaveLength(0)
  })

  // Long enough to read what happened and decide to undo it; 3 seconds is not.
  it('keeps a toast carrying an action for 8 seconds', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('2 contacts added', 'success', { label: 'Undo', onClick: () => {} }))
    act(() => { vi.advanceTimersByTime(3000) })
    expect(result.current.toasts).toHaveLength(1)

    act(() => { vi.advanceTimersByTime(5000) })
    expect(result.current.toasts).toHaveLength(0)
  })
})
