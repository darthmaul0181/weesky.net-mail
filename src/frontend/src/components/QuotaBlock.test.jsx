import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { QuotaBlock, QuotaMini } from './QuotaBlock.jsx'

const MB = 1024 * 1024
const GB = 1024 * MB

// ── QuotaBlock ────────────────────────────────────────────────

describe('QuotaBlock', () => {
  it('renders nothing when quota is null', () => {
    const { container } = render(<QuotaBlock quota={null} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders nothing when storageBytesLimit is 0', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 0, storageBytesLimit: 0 }} />)
    expect(container.firstChild).toBeNull()
  })

  it('shows MB unit when usage is under 1 GB', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 200 * MB, storageBytesLimit: 500 * MB }} />)
    // format(200) uses toFixed(0) because 200 >= 100 → "200 MB"
    expect(container.querySelector('.panel-quota-used').textContent).toMatch(/200\s+MB/)
    expect(container.querySelector('.panel-quota-total').textContent).toMatch(/500\s+MB/)
  })

  it('shows GB unit when any value reaches 1 GB', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 1 * GB, storageBytesLimit: 2 * GB }} />)
    expect(container.querySelector('.panel-quota-used').textContent).toMatch(/GB/)
    expect(container.querySelector('.panel-quota-total').textContent).toMatch(/GB/)
  })

  it('shows percentage', () => {
    render(<QuotaBlock quota={{ storageBytesUsed: 50 * MB, storageBytesLimit: 100 * MB }} />)
    expect(screen.getByText('50%')).toBeInTheDocument()
  })

  it('applies is-danger class at ≥ 90% usage', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 95 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-danger')
  })

  it('applies is-warn class at 75–89% usage', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 80 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-warn')
  })

  it('has no level class below 75%', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 40 * MB, storageBytesLimit: 100 * MB }} />
    )
    const bar = container.querySelector('.panel-quota-bar')
    expect(bar).not.toHaveClass('is-danger')
    expect(bar).not.toHaveClass('is-warn')
  })
})

// ── QuotaMini ─────────────────────────────────────────────────

describe('QuotaMini', () => {
  it('renders — when quota is null', () => {
    render(<QuotaMini quota={null} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('renders — when storageBytesLimit is zero', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 0, storageBytesLimit: 0 }} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('displays used / total in MB when values are under 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB }} />)
    expect(screen.getByText(/50\.0 \/ 200 MB/)).toBeInTheDocument()
  })

  it('displays used / total in GB when values reach 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 1 * GB, storageBytesLimit: 2 * GB }} />)
    expect(screen.getByText(/1\.0 \/ 2\.0 GB/)).toBeInTheDocument()
  })

  it('applies is-danger class when usage is ≥ 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 92 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-danger')
  })

  it('applies is-warn class when usage is between 75% and 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 80 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-warn')
  })
})
