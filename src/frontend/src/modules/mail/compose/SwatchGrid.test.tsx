import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import SwatchGrid from './SwatchGrid'

/** The grid is exercised through the toolbar in EditorToolbar.test.tsx. What only a direct render
    reaches is how `value` is read: the editor reports a colour the way the document spells it, and
    the twin in calendar/ColorSwatches normalises it identically. */
describe('SwatchGrid', () => {
  it('marks the applied colour however the editor spells it', () => {
    render(<SwatchGrid value=" #F8E71C " labelledBy="trigger" onPick={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Yellow' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('presses nothing when no colour is in force', () => {
    render(<SwatchGrid value="currentColor" labelledBy="trigger" onPick={vi.fn()} />)

    const pressed = screen.getAllByRole('button')
      .filter(button => button.getAttribute('aria-pressed') === 'true')
    expect(pressed).toEqual([])
  })
})
