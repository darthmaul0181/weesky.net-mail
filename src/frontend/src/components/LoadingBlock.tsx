import { useTranslation } from 'react-i18next'

/**
 * The house busy state: a centred spinner, never a bare "Loading…". One component so the
 * screens using it cannot drift, and so the announcement the text used to carry survives —
 * a spinner has no accessible name of its own.
 */
export default function LoadingBlock() {
  const { t } = useTranslation()
  return (
    <div className="loading-block">
      <span className="spinner" role="status" aria-label={t('loading')} />
    </div>
  )
}
