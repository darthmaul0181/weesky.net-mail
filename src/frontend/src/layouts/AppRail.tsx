import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import MailIcon from '../icons/MailIcon'
import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'

const modules = [
  { to: '/mail', labelKey: 'rail.mail', Icon: MailIcon },
  { to: '/calendar', labelKey: 'rail.calendar', Icon: CalendarIcon },
  { to: '/contacts', labelKey: 'rail.contacts', Icon: ContactsIcon },
] as const

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  const { t } = useTranslation()
  return (
    <nav className="app-rail" aria-label={t('rail.label')}>
      {modules.map(({ to, labelKey, Icon }) => {
        const label = t(labelKey)
        return (
          <NavLink key={to} to={to} className={railClass} aria-label={label} title={label}>
            <Icon />
          </NavLink>
        )
      })}
      <div className="rail-spacer" />
      <NavLink to="/settings" className={railClass}
        aria-label={t('rail.settings')} title={t('rail.settings')}>
        <GearIcon />
      </NavLink>
    </nav>
  )
}
