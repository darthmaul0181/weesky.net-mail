import { useTranslation } from 'react-i18next'

/** The house busy state: a centred spinner, never a bare "Loading…". Its status role keeps the
 * announcement the text used to make, since a spinner has no accessible name. */
export default function LoadingBlock() {
  const { t } = useTranslation()
  return (
    <div className="loading-block">
      <span className="spinner" role="status" aria-label={t('loading')} />
    </div>
  )
}
