import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import SparkleIcon from '../../icons/SparkleIcon'
import WhatsNewList from './WhatsNewList'
import { useWhatsNew } from './useWhatsNew'

/** The whole history, newest version first; reading it lists every fresh novelty, as the popover does. */
export default function WhatsNewPage() {
  const { t } = useTranslation('whatsNew')
  const { novelties, markListed } = useWhatsNew()
  useEffect(() => { markListed() }, [markListed])

  const versions = [...new Set(novelties.map(n => n.entry.version))]
  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><SparkleIcon size={17} />{t('title')}</h1>
      </div>
      {versions.length === 0 && <p className="whats-new-empty">{t('nothingAtAll')}</p>}
      {versions.map(version => (
        <section key={version} className="account-section">
          <h2>{t('version', { version })}</h2>
          <WhatsNewList novelties={novelties.filter(n => n.entry.version === version)} />
        </section>
      ))}
    </div>
  )
}
