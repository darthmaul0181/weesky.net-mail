import { NavLink } from 'react-router'
import { useTranslation } from 'react-i18next'
import WhatsNewDot from '../modules/whatsnew/WhatsNewDot'
import { useWhatsNew } from '../modules/whatsnew/useWhatsNew'
import { MODULES, SETTINGS_MODULE, type ModuleItem } from './modules'

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  const { t } = useTranslation()
  const { lights } = useWhatsNew()
  const item = ({ to, labelKey, Icon, anchor }: ModuleItem) => {
    const label = t(labelKey)
    // The aria-label hides the dot from a reader, so the name itself says New while it is lit.
    const name = lights(anchor) ? `${label} ${t('new', { ns: 'whatsNew' })}` : label
    return (
      <NavLink key={to} to={to} className={railClass} aria-label={name} title={label}>
        <Icon />
        <WhatsNewDot anchor={anchor} />
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
