import { useTranslation } from 'react-i18next'
import type { CalendarImportReport } from './calendarTypes'

interface Props {
  report: CalendarImportReport
  onClose: () => void
}

/**
 * What the import did, entry by entry where it refused. Six buckets rather than four: a file
 * carries tasks and journals this calendar has no screen for, and a reader missing events needs
 * to see that they were set aside rather than lost.
 */
export default function CalendarImportReportModal({ report, onClose }: Props) {
  const { t } = useTranslation('calendar')
  const counters: [string, number, string][] = [
    ['created', report.created, t('import.created')],
    ['replaced', report.replaced, t('import.replaced')],
    ['todos', report.ignoredTodos, t('import.ignoredTodos')],
    ['journals', report.ignoredJournals, t('import.ignoredJournals')],
    ['failed', report.failed, t('import.failed')],
    ['errors', report.totalErrors, t('import.totalErrors')],
  ]
  const hidden = report.totalErrors - report.errors.length

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('import.reportTitle')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            onClick={onClose}>✕</button>
        </div>

        <div className="import-counters is-grid">
          {counters.map(([key, value, label]) => (
            <div className="import-counter" key={key}>
              <span className="import-counter-value">{value}</span>
              <span className="import-counter-label">{label}</span>
            </div>
          ))}
        </div>

        {report.errors.length > 0 && (
          <ul className="import-errors">
            {/* Keyed by position: one entry can carry the same reason twice, and the list never
                reorders. */}
            {report.errors.map((error, index) => (
              <li key={index}>{t('import.errorLine', { line: error.line, reason: error.reason })}</li>
            ))}
            {hidden > 0 && (
              <li className="import-errors-more">{t('import.moreErrors', { count: hidden })}</li>
            )}
          </ul>
        )}
      </div>
    </div>
  )
}
