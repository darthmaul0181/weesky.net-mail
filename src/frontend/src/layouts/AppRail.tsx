import { NavLink } from 'react-router-dom'
import MailIcon from '../icons/MailIcon'
import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'

const modules = [
  { to: '/mail', label: 'Mail', Icon: MailIcon },
  { to: '/calendar', label: 'Calendar', Icon: CalendarIcon },
  { to: '/contacts', label: 'Contacts', Icon: ContactsIcon },
]

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  return (
    <nav className="app-rail" aria-label="Modules">
      {modules.map(({ to, label, Icon }) => (
        <NavLink key={to} to={to} className={railClass} aria-label={label} title={label}>
          <Icon />
        </NavLink>
      ))}
      <div className="rail-spacer" />
      <NavLink to="/settings" className={railClass} aria-label="Settings" title="Settings">
        <GearIcon />
      </NavLink>
    </nav>
  )
}
