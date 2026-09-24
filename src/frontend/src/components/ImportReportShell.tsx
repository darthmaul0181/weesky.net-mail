import type { ReactNode } from 'react'
import Modal from './Modal'

export interface ImportCounter {
  value: number
  label: string
}

interface ImportReportShellProps<E> {
  title: string
  counters: ImportCounter[]
  /** Tiles on a grid rather than in one row, for a report with more counters than a row holds. */
  grid?: boolean
  errors: E[]
  /** Every error the server counted, the listed ones included. */
  totalErrors: number
  renderError: (entry: E) => ReactNode
  moreLabel: (count: number) => string
  onClose: () => void
}

/** An import's report: its counter tiles, then every refused entry the server listed. */
export default function ImportReportShell<E>({
  title, counters, grid = false, errors, totalErrors, renderError, moreLabel, onClose,
}: ImportReportShellProps<E>) {
  const hidden = totalErrors - errors.length

  return (
    <Modal title={title} onClose={onClose}>
      <div className={grid ? 'import-counters is-grid' : 'import-counters'}>
        {counters.map(({ value, label }, index) => (
          <div className="import-counter" key={index}>
            <span className="import-counter-value">{value}</span>
            <span className="import-counter-label">{label}</span>
          </div>
        ))}
      </div>

      {errors.length > 0 && (
        <ul className="import-errors">
          {/* Keyed by position: one entry can carry the same reason twice, and the list never
              reorders. */}
          {errors.map((entry, index) => <li key={index}>{renderError(entry)}</li>)}
          {hidden > 0 && <li className="import-errors-more">{moreLabel(hidden)}</li>}
        </ul>
      )}
    </Modal>
  )
}
