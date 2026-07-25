import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import Tooltip from './Tooltip'

describe('Tooltip', () => {
  it('renders its trigger and its bubble', () => {
    render(<Tooltip content="the detail"><span>trigger</span></Tooltip>)

    expect(screen.getByText('trigger')).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('the detail')
  })

  // The bubble is revealed by CSS, so the placement modifier is the only thing a test can
  // hold on to — and getting it wrong puts the bubble outside the mail column's overflow.
  it('places the bubble above and to the right by default', () => {
    render(<Tooltip content="x"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-top-right')
  })

  it('places the bubble below and to the left on request', () => {
    render(<Tooltip content="x" placement="bottom-left"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-bottom-left')
  })

  // For a trigger flush against the column's right edge: the bubble opens down-LEFT,
  // the one direction the mail column's overflow:hidden cannot clip.
  it('places the bubble below and to the right on request', () => {
    render(<Tooltip content="x" placement="bottom-right"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-bottom-right')
  })
})
