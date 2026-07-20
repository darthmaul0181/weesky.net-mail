import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'

function paneClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'pane-item is-active' : 'pane-item'
}

export default function SettingsLayout() {
  const { isAdmin } = useAuth()
  return (
    <div className="settings-layout">
      <nav className="context-pane" aria-label="Settings">
        <NavLink to="/settings/account" end className={paneClass}>Account</NavLink>
        <NavLink to="/settings/general" className={paneClass}>General</NavLink>
        <NavLink to="/settings/accounts" className={paneClass}>Linked accounts</NavLink>
        <NavLink to="/settings/appearance" className={paneClass}>Appearance</NavLink>
        <NavLink to="/settings/folders" className={paneClass}>Folders list</NavLink>
        <NavLink to="/settings/aliases" className={paneClass}>Aliases</NavLink>
        <NavLink to="/settings/rules" className={paneClass}>Rules</NavLink>
        {isAdmin && <NavLink to="/settings/admin" className={paneClass}>Administration</NavLink>}
      </nav>
      <div className="settings-content">
        <Outlet />
      </div>
    </div>
  )
}
