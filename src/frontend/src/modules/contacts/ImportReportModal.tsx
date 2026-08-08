import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { ContactImportReport } from './contactTypes'

interface Props {
  report: ContactImportReport
  onClose: () => void
}

/**
 * The store's refusals arrive as prose rather than as codes, so they are matched here and
 * re-spelled from the catalogue — the way apiErrorMessage maps a stable code. A reason this does
 * not recognise prints as the generic: English server prose on a French screen is worse than a
 * line number and a plain sentence. Every key is written out, since one reaching `t()` as a
 * variable is invisible to `src/locales/keys.test.ts`.
 */
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

/**
 * What the import did, line by line where it refused. The counters count rows, so they add up to
 * the file's data rows — a reader who is missing contacts can tell which bucket took them.
 */
export default function ImportReportModal({ report, onClose }: Props) {
  const { t } = useTranslation('contacts')
  const counters: [string, number, string][] = [
    ['created', report.created, t('import.added', { count: report.created })],
    ['merged', report.merged, t('import.updated', { count: report.merged })],
    ['skipped', report.skipped, t('import.skipped', { count: report.skipped })],
    ['failed', report.failed, t('import.refused', { count: report.failed })],
  ]
  const hidden = report.totalErrors - report.errors.length

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('import.title')}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>

        <div className="import-counters">
          {counters.map(([key, value, label]) => (
            <div className="import-counter" key={key}>
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
                <span className="import-error-line">{t('import.line', { line: error.line })}</span>
                {' '}
                {reasonText(error.reason, t)}
              </li>
            ))}
            {hidden > 0 && (
              <li className="import-errors-more">{t('import.more', { count: hidden })}</li>
            )}
          </ul>
        )}
      </div>
    </div>
  )
}
