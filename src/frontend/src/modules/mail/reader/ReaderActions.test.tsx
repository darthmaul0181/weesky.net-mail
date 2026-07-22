import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ReaderActions from './ReaderActions'

describe('ReaderActions', () => {
  it('offers the sender colours while the message wears the theme adaptation', () => {
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={() => {}} />)

    expect(screen.getByRole('button', { name: 'Original colours' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Showing the colours the sender chose.')
  })

  it('offers the way back while the sender colours are shown', () => {
    render(<ReaderActions showColourToggle originalColours onToggleColours={() => {}} />)

    expect(screen.getByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Colours are adapted to your dark theme.')
  })

  // Both icons are aria-hidden, so the accessible tree cannot tell them apart — the sun's
  // <circle> is the one structural difference a test can hold on to. Without this, swapping
  // the icons while keeping the labels would ship a visually lying button, all tests green.
  it('draws the sun while adapted and the moon once original', () => {
    const { container, rerender } = render(
      <ReaderActions showColourToggle originalColours={false} onToggleColours={() => {}} />)
    expect(container.querySelector('.action-btn circle')).not.toBeNull()

    rerender(<ReaderActions showColourToggle originalColours onToggleColours={() => {}} />)
    expect(container.querySelector('.action-btn circle')).toBeNull()
  })

  it('reports the click', () => {
    const onToggle = vi.fn()
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={onToggle} />)

    fireEvent.click(screen.getByRole('button', { name: 'Original colours' }))

    expect(onToggle).toHaveBeenCalledOnce()
  })

  // A rule beside a lone button reads as a rendering fault — same reason the folder tree
  // only draws its hr between two populated blocks.
  it('hides the toggle and its rule together, keeping the menu button', () => {
    const { container } = render(
      <ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}} />)

    expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
    expect(container.querySelector('.actions-rule')).toBeNull()
    expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
  })

  it('lets the future menu button be clicked without effect', () => {
    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
  })
})
