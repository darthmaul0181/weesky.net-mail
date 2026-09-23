import { NavLink } from 'react-router'
import { useTranslation } from 'react-i18next'
import { MODULES, SETTINGS_MODULE, type ModuleItem } from './modules'

function tabClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'bottom-nav-item is-active' : 'bottom-nav-item'
}

/** The rail, under the thumb. Phone only: CSS hides it from 640px up, so it renders
 * unconditionally and depends on no viewport hook. */
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
