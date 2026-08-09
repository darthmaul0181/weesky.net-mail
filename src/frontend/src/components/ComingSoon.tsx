import { useTranslation } from 'react-i18next'

/** `module` names the rail entry this page stands in for: the heading is that same label. */
export default function ComingSoon({ module }: { module: 'calendar' }) {
  const { t } = useTranslation()
  return (
    <div className="coming-soon">
      <h1>{t(`rail.${module}`)}</h1>
      <p>{t('comingSoon')}</p>
    </div>
  )
}
