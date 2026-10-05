import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import ImportReportModal from './ImportReportModal'
import type { ContactImportReport } from './contactTypes'
import { fireEscape, pressBackdrop, setupUser } from '../../test-utils'

const report = (fields: Partial<ContactImportReport> = {}): ContactImportReport => ({
  created: 0, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [], ...fields,
})

describe('ImportReportModal', () => {
  it('prints the four counters', () => {
    render(<ImportReportModal
      report={report({ created: 12, merged: 3, skipped: 1, failed: 2 })} onClose={vi.fn()} />)

    const tiles = [...document.querySelectorAll('.import-counter')].map(t => t.textContent)
    expect(tiles).toEqual([
      expect.stringMatching(/^12.*added/i), expect.stringMatching(/^3.*updated/i),
      expect.stringMatching(/^1.*skipped/i), expect.stringMatching(/^2.*refused/i),
    ])
  })

  it('lists a refused line with its number and reason', () => {
    render(<ImportReportModal
      report={report({ failed: 1, totalErrors: 1, errors: [{ line: 7, reason: 'Neither a name nor a valid e-mail address' }] })}
      onClose={vi.fn()} />)

    expect(screen.getByText(/line 7/i)).toBeInTheDocument()
    expect(screen.getByText(/neither a name/i)).toBeInTheDocument()
  })

  // Fifty of ten thousand is a report; ten thousand is a wall.
  it('says how many reasons it is not showing', () => {
    render(<ImportReportModal
      report={report({ failed: 312, totalErrors: 312, errors: [{ line: 2, reason: 'bad' }] })}
      onClose={vi.fn()} />)

    expect(screen.getByText(/311 more/i)).toBeInTheDocument()
  })

  it('lists no refused line when nothing was refused', () => {
    render(<ImportReportModal report={report({ created: 4 })} onClose={vi.fn()} />)

    expect(screen.queryByText(/line /i)).not.toBeInTheDocument()
  })

  it('is a dialog named by its own title', () => {
    render(<ImportReportModal report={report()} onClose={vi.fn()} />)

    expect(screen.getByRole('dialog', { name: 'Import finished' }))
      .toHaveAttribute('aria-modal', 'true')
  })

  // The three ways out of every dialog on the site.
  it('closes on the ✕, on Escape and on a press on the backdrop', async () => {
    const user = setupUser()
    const onClose = vi.fn()
    render(<ImportReportModal report={report()} onClose={onClose} />)

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEscape()
    expect(onClose).toHaveBeenCalledTimes(2)

    pressBackdrop()
    expect(onClose).toHaveBeenCalledTimes(3)
  })
})
