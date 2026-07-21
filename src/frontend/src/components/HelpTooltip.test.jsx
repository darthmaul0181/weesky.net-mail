import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import HelpTooltip from './HelpTooltip'

// The bubble moved out into Tooltip; this is what catches the icon or the text being
// dropped on the way, in a component two settings pages render and nothing else covers.
describe('HelpTooltip', () => {
  it('shows its icon and hands its text to the bubble', () => {
    render(<HelpTooltip text="Only an admin can do this." />)

    expect(screen.getByText('?')).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Only an admin can do this.')
  })

  it('keeps the bubble above and to the right, where the settings pages have room for it', () => {
    render(<HelpTooltip text="x" />)

    expect(screen.getByRole('tooltip')).toHaveClass('is-top-right')
  })
})
