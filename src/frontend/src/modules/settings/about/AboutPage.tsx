import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '../../../api.js'
import ExternalLinkIcon from '../../../icons/ExternalLinkIcon'
import InfoIcon from '../../../icons/InfoIcon'
import { BUILT_AT, WEB_COMMIT, WEB_VERSION, versionLabel } from '../../../lib/appVersion'
import { PRODUCT } from '../../../lib/product'
import scotty from '../../../assets/scotty-720.webp'
import scottyLarge from '../../../assets/scotty-1440.webp'

/** The page the build writes beside the bundle — the bundled libraries plus our own assets. */
const LICENCES = '/third-party-licenses.html'

interface ServerVersion {
  version: string
  /** Optional, not just nullable: the API omits a null field entirely. */
  commit?: string | null
}

export default function AboutPage() {
  const { t, i18n } = useTranslation('settings')
  // Instance-wide rather than account-scoped, and it only moves on a deploy.
  const { data: server, isError } = useQuery({
    queryKey: ['version'],
    queryFn: ({ signal }) => api.getVersion({ signal }) as Promise<ServerVersion>,
    staleTime: 5 * 60 * 1000,
  })

  const built = new Date(BUILT_AT)
  const webLine = `${t('about.webApp')} ${versionLabel(WEB_VERSION, WEB_COMMIT)}`
  // Three states, and the pending one keeps the line's place rather than letting the block jump
  // under the reader when the answer lands.
  const serverLine = server ? `${t('about.server')} ${versionLabel(server.version, server.commit ?? null)}`
    : isError ? t('about.serverUnavailable')
      : `${t('about.server')} …`

  return (
    <div className="about-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><InfoIcon size={17} />{t('nav.about')}</h1>
      </div>

      <img
        className="about-logo"
        src={scotty}
        srcSet={`${scotty} 720w, ${scottyLarge} 1440w`}
        sizes="(max-width: 639px) calc(100vw - 32px), 560px"
        width={720}
        height={499}
        alt=""
      />

      <p className="about-product">{PRODUCT.name} <span className="about-product-kind">{PRODUCT.kind}</span></p>
      <span className="about-badge">{t('about.version', { version: WEB_VERSION })}</span>

      <p className="about-builds">
        <span>{webLine}</span>
        <span>{serverLine}</span>
        <span>{new Intl.DateTimeFormat(i18n.language, { dateStyle: 'long' }).format(built)}</span>
      </p>

      <div className="about-actions">
        <a className="about-action" href={LICENCES} target="_blank" rel="noopener noreferrer">
          {t('about.thirdParty')}<ExternalLinkIcon size={13} />
        </a>
      </div>

      <p className="about-rights">{t('about.rights', { year: built.getFullYear() })}</p>
    </div>
  )
}
