import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import AuthBadge from './AuthBadge'
import type { MailAuthentication } from '../api/mailTypes'

const passed: MailAuthentication = {
  spf: 'pass', dkim: 'pass',
  raw: 'Authentication-Results: mx.example.com; spf=pass; dkim=pass',
}

describe('AuthBadge', () => {
  it('renders nothing without a recognised verdict', () => {
    const { container } = render(<AuthBadge />)

    expect(container.textContent).toBe('')
  })

  // The precedent is HelpTooltip: a real button, describing itself with the bubble it opens.
  it('is a real button, named for the verdict and describing itself with the raw header', () => {
    render(<AuthBadge authentication={passed} />)

    const trigger = screen.getByRole('button', { name: 'Passed SPF and DKIM' })
    const bubble = screen.getByRole('tooltip')

    expect(trigger).toHaveAttribute('type', 'button')
    expect(trigger).toHaveAttribute('aria-describedby', bubble.id)
    expect(bubble).toHaveTextContent(passed.raw)
  })

  it('names a failing verdict distinctly', () => {
    render(<AuthBadge authentication={{ spf: 'fail', dkim: 'pass', raw: 'x' }} />)

    expect(screen.getByRole('button', { name: 'Failed SPF or DKIM' })).toBeInTheDocument()
  })
})
