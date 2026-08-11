import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { MODULES, SETTINGS_MODULE, type ModuleItem } from './modules'

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  const { t } = useTranslation()
  const item = ({ to, labelKey, Icon }: ModuleItem) => {
    const label = t(labelKey)
    return (
      <NavLink key={to} to={to} className={railClass} aria-label={label} title={label}>
        <Icon />
      </NavLink>
    )
  }
  return (
    <nav className="app-rail" aria-label={t('rail.label')}>
      {MODULES.map(item)}
      <div className="rail-spacer" />
      {item(SETTINGS_MODULE)}
    </nav>
  )
}
