import { useTranslation } from 'react-i18next'
import { dailyImageOf, useAppSettings } from '../../../hooks/useAppSettings'
import {
  DAILY_IMAGE_STYLES, PREFERENCE_KEYS, dailyImageStyleOf, usePreferences, useSetPreference,
  type DailyImageStyle,
} from '../../../hooks/usePreferences'

/** Shown only while the admin offers the image of the day; a choice stored before stays stored. */
export default function DailyImageSection() {
  const { t } = useTranslation('settings')
  const { data: settings } = useAppSettings()
  const { data: preferences } = usePreferences()
  const setPreference = useSetPreference()

  if (!settings || !dailyImageOf(settings) || !preferences) return null
  const current = dailyImageStyleOf(preferences)

  // Literal keys, one per style: a key built from a variable is invisible to locales/keys.test.ts.
  function labelOf(style: DailyImageStyle) {
    switch (style) {
      case 'none': return t('appearance.dailyImage.none')
      case 'fullBleed': return t('appearance.dailyImage.fullBleed')
      case 'postcard': return t('appearance.dailyImage.postcard')
      case 'watermark': return t('appearance.dailyImage.watermark')
    }
  }

  return (
    <section className="account-section">
      <h2 id="daily-image-heading">{t('appearance.dailyImage.heading')}</h2>
      <p className="svc-account-section-intro">{t('appearance.dailyImage.intro')}</p>
      <div className="seg" role="radiogroup" aria-labelledby="daily-image-heading">
        {DAILY_IMAGE_STYLES.map(style => (
          <label key={style}>
            <input
              type="radio"
              name="daily-image"
              value={style}
              checked={current === style}
              onChange={() => setPreference.mutate({ key: PREFERENCE_KEYS.dailyImage, value: style })}
            />
            {labelOf(style)}
          </label>
        ))}
      </div>
    </section>
  )
}
