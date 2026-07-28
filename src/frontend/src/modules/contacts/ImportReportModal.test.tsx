import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ImportReportModal from './ImportReportModal'
import type { ContactImportReport } from './contactTypes'

const report = (fields: Partial<ContactImportReport> = {}): ContactImportReport => ({
  created: 0, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [], ...fields,
})

describe('ImportReportModal', () => {
  it('prints the four counters', () => {
    render(<ImportReportModal report={report({ created: 12, merged: 3, skipped: 1, failed: 2 })}
      onClose={vi.fn()} />)

    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText(/added/i)).toBeInTheDocument()
    expect(screen.getByText(/updated/i)).toBeInTheDocument()
    expect(screen.getByText(/skipped/i)).toBeInTheDocument()
    expect(screen.getByText(/refused/i)).toBeInTheDocument()
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

  it('says so when nothing went wrong', () => {
    render(<ImportReportModal report={report({ created: 4 })} onClose={vi.fn()} />)

    expect(screen.queryByText(/line /i)).not.toBeInTheDocument()
  })

  // The cross is the only way out, as in every dialog on the site.
  it('closes on the cross', async () => {
    const onClose = vi.fn()
    render(<ImportReportModal report={report()} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: '✕' }))

    expect(onClose).toHaveBeenCalled()
  })
})
