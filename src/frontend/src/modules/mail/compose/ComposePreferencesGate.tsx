import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { usePreferences, type Preferences } from '../../../hooks/usePreferences'
import LoadingBlock from '../../../components/LoadingBlock'

/** The form reads the default editor once, at mount: mounted before preferences are known, a cold
    load (a mailto: link, a reload) would open in HTML whatever the account chose. */
export default function ComposePreferencesGate(
  { onClose, children }: { onClose: () => void; children: (preferences: Preferences) => ReactNode },
) {
  const { t } = useTranslation('compose')
  const { data: preferences, isError, isFetching, refetch } = usePreferences()
  if (preferences) return children(preferences)
  if (!isError || isFetching) return <div className="mail-full-pane"><LoadingBlock /></div>
  // Close as well as Retry: on a phone the composer hides every other way out of this route.
  return (
    <div className="mail-full-pane">
      <p>{t('general.loadFailed', { ns: 'settings' })}</p>
      <button type="button" className="btn btn-primary" onClick={() => void refetch()}>
        {t('list.retry', { ns: 'mail' })}
      </button>
      <button type="button" className="btn btn-ghost" onClick={onClose}>
        {t('actions.close', { ns: 'common' })}
      </button>
    </div>
  )
}
