import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, fireEvent, act } from '@testing-library/react'
import RefreshButton from './RefreshButton'

afterEach(() => { vi.useRealTimers() })

function spinner() {
  return screen.getByLabelText('Refresh').firstElementChild as HTMLElement
}

describe('RefreshButton', () => {
  it('calls onRefresh on an idle click', () => {
    const onRefresh = vi.fn()
    render(<RefreshButton fetching={false} onRefresh={onRefresh} />)
    fireEvent.click(screen.getByLabelText('Refresh'))
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('spins while the folders query fetches', () => {
    const { rerender } = render(<RefreshButton fetching={false} onRefresh={() => {}} />)
    expect(spinner()).not.toHaveClass('is-spinning')
    rerender(<RefreshButton fetching={true} onRefresh={() => {}} />)
    expect(spinner()).toHaveClass('is-spinning')
  })

  it('finishes the rotation it started before stopping', () => {
    const { rerender } = render(<RefreshButton fetching={true} onRefresh={() => {}} />)
    rerender(<RefreshButton fetching={false} onRefresh={() => {}} />)
    // The fetch is over but the turn is not: the class stays until the iteration boundary.
    expect(spinner()).toHaveClass('is-spinning')
    fireEvent.animationIteration(spinner())
    expect(spinner()).not.toHaveClass('is-spinning')
  })

  it('keeps spinning across iteration boundaries while still fetching', () => {
    render(<RefreshButton fetching={true} onRefresh={() => {}} />)
    fireEvent.animationIteration(spinner())
    expect(spinner()).toHaveClass('is-spinning')
  })

  it('ignores a click while spinning', () => {
    const onRefresh = vi.fn()
    const { rerender } = render(<RefreshButton fetching={true} onRefresh={onRefresh} />)
    rerender(<RefreshButton fetching={false} onRefresh={onRefresh} />)
    fireEvent.click(screen.getByLabelText('Refresh'))
    expect(onRefresh).not.toHaveBeenCalled()
  })

  it('releases through the timer when the animation never iterates (reduced motion)', () => {
    vi.useFakeTimers()
    const { rerender } = render(<RefreshButton fetching={true} onRefresh={() => {}} />)
    rerender(<RefreshButton fetching={false} onRefresh={() => {}} />)
    expect(spinner()).toHaveClass('is-spinning')
    act(() => { vi.advanceTimersByTime(800) })
    expect(spinner()).not.toHaveClass('is-spinning')
  })
})
