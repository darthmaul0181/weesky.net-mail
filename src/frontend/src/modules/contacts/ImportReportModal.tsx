import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { ContactImportReport } from './contactTypes'
import ImportReportShell from '../../components/ImportReportShell'

interface Props {
  report: ContactImportReport
  onClose: () => void
}

/** The store's refusals arrive as prose, so they are matched and re-spelled from the catalogue; an
 * unknown one prints as the generic, never as English on a French screen. Every key is written
 * out: one reaching `t()` as a variable is invisible to `keys.test.ts`. */
function reasonText(reason: string, t: TFunction<'contacts'>): string {
  if (reason === 'Neither a name nor a valid e-mail address') {
    return t('contactImport.noNameOrAddress', { ns: 'errors' })
  }
  if (reason === 'An address on this row already belongs to more than one contact') {
    return t('contactImport.ambiguousAddress', { ns: 'errors' })
  }
  if (reason === 'This row carries no address, and its name is on more than one contact') {
    return t('contactImport.ambiguousName', { ns: 'errors' })
  }
  if (reason === 'The first name on this row was too long and was left out') {
    return t('contactImport.overLongFirstName', { ns: 'errors' })
  }
  if (reason === 'The last name on this row was too long and was left out') {
    return t('contactImport.overLongLastName', { ns: 'errors' })
  }
  if (reason === 'The nickname on this row was too long and was left out') {
    return t('contactImport.overLongNickname', { ns: 'errors' })
  }
  const cap = /^You have reached the maximum of (\d+) contacts$/.exec(reason)
  if (cap) return t('contactImport.capReached', { ns: 'errors', max: cap[1] })
  const kept = /^Only the first (\d+) addresses were kept$/.exec(reason)
  if (kept) return t('contactImport.addressCapReached', { ns: 'errors', max: kept[1] })
  const invalid = /^'(.*)' is not a valid e-mail address and was ignored$/.exec(reason)
  if (invalid) return t('contactImport.invalidAddress', { ns: 'errors', address: invalid[1] })
  return t('contactImport.unknown', { ns: 'errors' })
}

/** What the import did, line by line where it refused. The counters count rows, so they add up to
 * the file's data rows. */
export default function ImportReportModal({ report, onClose }: Props) {
  const { t } = useTranslation('contacts')

  return (
    <ImportReportShell title={t('import.title')} onClose={onClose}
      counters={[
        { value: report.created, label: t('import.added', { count: report.created }) },
        { value: report.merged, label: t('import.updated', { count: report.merged }) },
        { value: report.skipped, label: t('import.skipped', { count: report.skipped }) },
        { value: report.failed, label: t('import.refused', { count: report.failed }) },
      ]}
      errors={report.errors} totalErrors={report.totalErrors}
      renderError={error => (
        <>
          <span className="import-error-line">{t('import.line', { line: error.line })}</span>
          {' '}
          {reasonText(error.reason, t)}
        </>
      )}
      moreLabel={count => t('import.more', { count })} />
  )
}
