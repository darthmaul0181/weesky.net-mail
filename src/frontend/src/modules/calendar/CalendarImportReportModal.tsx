import { useTranslation } from 'react-i18next'
import type { CalendarImportReport } from './calendarTypes'
import Modal from '../../components/Modal'

interface Props {
  report: CalendarImportReport
  onClose: () => void
}

/** What the import did, entry by entry where it refused. Six buckets, not four: tasks and journals
 * this calendar cannot show must read as set aside, not lost. */
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
    <Modal title={t('import.reportTitle')} onClose={onClose}>
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
    </Modal>
  )
}
