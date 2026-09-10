import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import CalendarImportReportModal from './CalendarImportReportModal'
import type { CalendarImportReport } from './calendarTypes'

const REPORT: CalendarImportReport = {
  created: 12, replaced: 3, ignoredTodos: 2, ignoredJournals: 1, failed: 4, totalErrors: 6,
  errors: [
    { line: 7, reason: 'No DTSTART' },
    { line: 9, reason: 'Unreadable recurrence rule' },
  ],
}

function open(report: CalendarImportReport = REPORT) {
  const onClose = vi.fn()
  const view = render(<CalendarImportReportModal report={report} onClose={onClose} />)
  return { onClose, ...view }
}

describe('CalendarImportReportModal', () => {
  // Six buckets, so a reader missing events can tell which one took them.
  it('counts every bucket the import fills', () => {
    const { container } = open()
    const counters = [...container.querySelectorAll('.import-counter')]
      .map(node => [
        node.querySelector('.import-counter-value')?.textContent,
        node.querySelector('.import-counter-label')?.textContent,
      ])
    expect(counters).toEqual([
      ['12', 'Created'], ['3', 'Replaced'], ['2', 'Tasks ignored'],
      ['1', 'Journals ignored'], ['4', 'Failed'], ['6', 'Errors'],
    ])
  })

  it('prints the refused entries with their rank', () => {
    open()
    const list = screen.getByRole('list')
    expect(within(list).getByText('Entry 7 — No DTSTART')).toBeInTheDocument()
    expect(within(list).getByText('Entry 9 — Unreadable recurrence rule')).toBeInTheDocument()
  })

  // The server caps what it lists; the count is what says the rest happened.
  it('says how many refusals it could not list', () => {
    open()
    expect(screen.getByText('…and 4 further errors')).toBeInTheDocument()
  })

  it('closes on the ✕', async () => {
    const { onClose } = open()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})
