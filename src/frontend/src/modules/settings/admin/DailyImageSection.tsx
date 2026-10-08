import { useTranslation } from 'react-i18next'
import { APP_SETTING_KEYS, dailyImageOf, useAppSettings, useSetAppSetting } from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import type { AddToast } from '../../../hooks/useToasts'

export default function DailyImageSection({ addToast }: { addToast: AddToast }) {
  const { t } = useTranslation('admin')
  const { data: settings } = useAppSettings()
  const setSetting = useSetAppSetting()
  if (!settings) return null

  async function toggle(on: boolean) {
    try {
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.dailyImage, value: String(on) })
      addToast(t(on ? 'application.dailyImageOn' : 'application.dailyImageOff'))
    } catch (error) {
      addToast(apiErrorMessage(error, t('application.saveFailed')), 'error')
    }
  }

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title">{t('application.dailyImage')}</h2>
      <p className="svc-account-section-intro">{t('application.dailyImageIntro')}</p>
      <div className="field-h is-setting">
        <label htmlFor="app-daily-image">{t('application.dailyImage')}</label>
        <label className="toggle-switch">
          <input
            id="app-daily-image"
            type="checkbox"
            checked={dailyImageOf(settings)}
            disabled={setSetting.isPending}
            aria-label={t('application.dailyImage')}
            onChange={event => void toggle(event.target.checked)}
          />
          <span className="toggle-track" />
        </label>
      </div>
    </div>
  )
}
