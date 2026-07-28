import type { ContactImportReport } from './contactTypes'

interface Props {
  report: ContactImportReport
  onClose: () => void
}

/**
 * What the import did, line by line where it refused. The counters count rows, so they add up to
 * the file's data rows — a reader who is missing contacts can tell which bucket took them.
 */
export default function ImportReportModal({ report, onClose }: Props) {
  const counters: [number, string][] = [
    [report.created, 'added'],
    [report.merged, 'updated'],
    [report.skipped, 'skipped'],
    [report.failed, 'refused'],
  ]
  const hidden = report.totalErrors - report.errors.length

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Import finished</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>

        <div className="import-counters">
          {counters.map(([value, label]) => (
            <div className="import-counter" key={label}>
              <span className="import-counter-value">{value}</span>
              <span className="import-counter-label">{label}</span>
            </div>
          ))}
        </div>

        {report.errors.length > 0 && (
          <ul className="import-errors">
            {/* Keyed by position: one line can carry the same reason twice, and the list never reorders. */}
            {report.errors.map((error, index) => (
              <li key={index}>
                <span className="import-error-line">Line {error.line}</span> {error.reason}
              </li>
            ))}
            {hidden > 0 && <li className="import-errors-more">and {hidden} more</li>}
          </ul>
        )}
      </div>
    </div>
  )
}
