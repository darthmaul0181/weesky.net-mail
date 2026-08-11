import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { MODULES, SETTINGS_MODULE, type ModuleItem } from './modules'

function tabClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'bottom-nav-item is-active' : 'bottom-nav-item'
}

/**
 * The rail, moved under the thumb. Phone only — CSS hides it from 640px up, where the rail
 * itself is back. Rendered unconditionally so nothing here depends on the viewport hook.
 */
export default function BottomNav() {
  const { t } = useTranslation()
  const tab = ({ to, labelKey, Icon }: ModuleItem) => (
    <NavLink key={to} to={to} className={tabClass}>
      <Icon size={22} />
      <span className="bottom-nav-label">{t(labelKey)}</span>
    </NavLink>
  )
  return (
    <nav className="app-bottom-nav" aria-label={t('rail.label')}>
      {MODULES.map(tab)}
      {tab(SETTINGS_MODULE)}
    </nav>
  )
}
