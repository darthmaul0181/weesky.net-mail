import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts'
import { usePreferences, type Preferences } from '../../../hooks/usePreferences'
import type { useToasts } from '../../../hooks/useToasts'
import SlidersIcon from '../../../icons/SlidersIcon'

interface Props {
  /** The module's name, over a title every module's page shares. */
  group: string
  toasts: ReturnType<typeof useToasts>
  children: (preferences: Preferences) => ReactNode
}

/** The frame of a module's General page: its title, the load and failure states, the toasts. */
export default function PreferencePage({ group, toasts, children }: Props) {
  const { t } = useTranslation('settings')
  const { data: preferences, isLoading, isError } = usePreferences()

  return (
    <div className="settings-page">
      <p className="settings-page-eyebrow">{group}</p>
      <div className="settings-page-header">
        <h1 className="settings-page-title"><SlidersIcon size={17} />{t('nav.general')}</h1>
      </div>

      {isLoading && <LoadingBlock />}
      {!isLoading && (isError || !preferences) && <p>{t('general.loadFailed')}</p>}
      {!isLoading && !isError && preferences && children(preferences)}

      <Toasts toasts={toasts.toasts} onRemove={toasts.removeToast}
        onPause={toasts.pauseToast} onResume={toasts.resumeToast} />
    </div>
  )
}
