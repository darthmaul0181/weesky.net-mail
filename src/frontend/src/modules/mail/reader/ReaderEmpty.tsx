import { useTranslation } from 'react-i18next'
import DailyImageCredit, { CreditLines } from '../../../components/DailyImageCredit'
import { useDailyImage } from '../../../hooks/useDailyImage'
import { dailyImageStyleOf, usePreferences, type DailyImageStyle } from '../../../hooks/usePreferences'

const VARIANT_CLASS: Record<Exclude<DailyImageStyle, 'none'>, string> = {
  fullBleed: 'is-full-bleed',
  postcard: 'is-postcard',
  watermark: 'is-watermark',
}

/** The reading pane with no message open: the plain text, or the image of the day as the user chose. */
export default function ReaderEmpty() {
  const { t } = useTranslation('mail')
  const { data: preferences } = usePreferences()
  const style = preferences ? dailyImageStyleOf(preferences) : 'none'
  const image = useDailyImage(style !== 'none')
  const text = t('reader.selectMessage')

  if (style === 'none' || !image) return <p className="mail-empty">{text}</p>

  if (style === 'postcard') return (
    <div className="reader-daily is-postcard">
      <p className="reader-daily-label">{text}</p>
      <figure className="reader-daily-card">
        <img className="reader-daily-photo" src={image.src} alt="" />
        <figcaption><CreditLines image={image} /></figcaption>
      </figure>
    </div>
  )

  return (
    <div className={`reader-daily ${VARIANT_CLASS[style]}`}>
      <img className="reader-daily-photo" src={image.src} alt="" />
      <p className="reader-daily-label">{text}</p>
      <DailyImageCredit image={image} className="reader-daily-credit" />
    </div>
  )
}
