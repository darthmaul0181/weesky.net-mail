import { useTranslation } from 'react-i18next'
import type { CalendarImportReport } from './calendarTypes'
import ImportReportShell from '../../components/ImportReportShell'

interface Props {
  report: CalendarImportReport
  onClose: () => void
}

/** What the import did, entry by entry where it refused. Six buckets, not four: tasks and journals
 * this calendar cannot show must read as set aside, not lost. */
export default function CalendarImportReportModal({ report, onClose }: Props) {
  const { t } = useTranslation('calendar')

  return (
    <ImportReportShell title={t('import.reportTitle')} onClose={onClose} grid
      counters={[
        { value: report.created, label: t('import.created') },
        { value: report.replaced, label: t('import.replaced') },
        { value: report.ignoredTodos, label: t('import.ignoredTodos') },
        { value: report.ignoredJournals, label: t('import.ignoredJournals') },
        { value: report.failed, label: t('import.failed') },
        { value: report.totalErrors, label: t('import.totalErrors') },
      ]}
      errors={report.errors} totalErrors={report.totalErrors}
      renderError={error => t('import.errorLine', { line: error.line, reason: error.reason })}
      moreLabel={count => t('import.moreErrors', { count })} />
  )
}
