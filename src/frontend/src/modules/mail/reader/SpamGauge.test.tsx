import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import SpamGauge from './SpamGauge'

const spam = { score: 7, threshold: 16, raw: 'X-Spamd-Result: default: False [7.00 / 16.00];' }

describe('SpamGauge', () => {
  it('shows the label and the score as the filter reported it', () => {
    render(<SpamGauge spamScore={spam} />)

    expect(screen.getByText(/^Spam score:/)).toBeInTheDocument()
    expect(screen.getByText('7.0 / 16.0')).toBeInTheDocument()
  })

  // jsdom applies no stylesheet, so the custom property on the track is what a test can pin:
  // it drives both the fill width and the green-to-red mix.
  it('hands the clamped ratio to the CSS', () => {
    const { container } = render(<SpamGauge spamScore={spam} />)

    const track = container.querySelector('.spam-gauge-track') as HTMLElement
    expect(track.style.getPropertyValue('--gauge-ratio')).toBe('0.4375')
  })

  it('keeps the raw header one hover away', () => {
    render(<SpamGauge spamScore={spam} />)

    expect(screen.getByRole('tooltip')).toHaveTextContent('X-Spamd-Result: default: False')
  })

  it('renders nothing without a score', () => {
    const { container } = render(<SpamGauge spamScore={null} />)

    expect(container.textContent).toBe('')
  })

  it('renders nothing when the threshold makes no sense', () => {
    const { container } = render(<SpamGauge spamScore={{ score: 3, threshold: 0, raw: 'x' }} />)

    expect(container.textContent).toBe('')
  })
})
