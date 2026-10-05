import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import type { Quota } from '../types/account'
import QuotaBlock, { QuotaMini } from './QuotaBlock'

const MB = 1024 * 1024
const GB = 1024 * MB

/** The card and the delete quota answer never carry message counts absent, but this component
    reads neither, so the fixture fills them with the size figures alone in play. */
function quota(storageBytesUsed: number, storageBytesLimit: number): Quota {
  return { storageBytesUsed, storageBytesLimit, messageCount: 0, messageLimit: 0 }
}

// ── QuotaBlock ────────────────────────────────────────────────

describe('QuotaBlock', () => {
  it.each([['null', null], ['a storageBytesLimit of 0', quota(0, 0)]])('renders nothing for %s', (_, q) => {
    const { container } = render(<QuotaBlock quota={q} />)
    expect(container.firstChild).toBeNull()
  })

  it('shows MB unit when usage is under 1 GB', () => {
    const { container } = render(<QuotaBlock quota={quota(200 * MB, 500 * MB)} />)
    // format(200) uses toFixed(0) because 200 >= 100 → "200 MB"
    expect(container.querySelector('.panel-quota-used')!.textContent).toMatch(/200\s+MB/)
    expect(container.querySelector('.panel-quota-total')!.textContent).toMatch(/500\s+MB/)
  })

  it('shows GB unit when any value reaches 1 GB', () => {
    const { container } = render(<QuotaBlock quota={quota(1 * GB, 2 * GB)} />)
    expect(container.querySelector('.panel-quota-used')!.textContent).toMatch(/GB/)
    expect(container.querySelector('.panel-quota-total')!.textContent).toMatch(/GB/)
  })

  it('shows percentage', () => {
    render(<QuotaBlock quota={quota(50 * MB, 100 * MB)} />)
    expect(screen.getByText('50%')).toBeInTheDocument()
  })
})

// ── Level class, same rule for both ─────────────────────────

describe.each([['QuotaBlock', QuotaBlock], ['QuotaMini', QuotaMini]])('%s level class', (_, Component) => {
  it.each([[95, 'is-danger'], [90, 'is-danger'], [80, 'is-warn'], [75, 'is-warn'], [40, 'none']])(
    'at %i%% usage carries %s',
    (percent, level) => {
      const { container } = render(<Component quota={quota(percent * MB, 100 * MB)} />)
      const bar = container.querySelector('.panel-quota-bar')!
      for (const cls of ['is-danger', 'is-warn']) {
        if (cls === level) expect(bar).toHaveClass(cls)
        else expect(bar).not.toHaveClass(cls)
      }
    })
})

// ── QuotaMini ─────────────────────────────────────────────────

describe('QuotaMini', () => {
  it.each([['null', null], ['a storageBytesLimit of 0', quota(0, 0)]])('renders — for %s', (_, q) => {
    render(<QuotaMini quota={q} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('displays used / total in MB when values are under 1 GB', () => {
    render(<QuotaMini quota={quota(50 * MB, 200 * MB)} />)
    expect(screen.getByText(/50\.0 \/ 200 MB/)).toBeInTheDocument()
  })

  it('displays used / total in GB when values reach 1 GB', () => {
    render(<QuotaMini quota={quota(1 * GB, 2 * GB)} />)
    expect(screen.getByText(/1\.0 \/ 2\.0 GB/)).toBeInTheDocument()
  })
})
