import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import IdentityMenu from '../../layouts/IdentityMenu'

function paneClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'pane-item is-active' : 'pane-item'
}

export default function SettingsLayout() {
  const { isAdmin, activeAccount } = useAuth()
  // `!== false`, not `=== true`: activeAccount is null while the account list loads, and the
  // primary nav must stay full during that window rather than flash away and back.
  const isPrimary = activeAccount?.isPrimary !== false
  const rulesAvailable = isPrimary || activeAccount?.sieveSupported !== false
  return (
    <div className="settings-layout">
      <nav className="context-pane" aria-label="Settings">
        {isPrimary && <NavLink to="/settings/account" end className={paneClass}>Account</NavLink>}
        <NavLink to="/settings/general" className={paneClass}>General</NavLink>
        <NavLink to="/settings/accounts" className={paneClass}>Connected accounts</NavLink>
        <NavLink to="/settings/appearance" className={paneClass}>Appearance</NavLink>
        <NavLink to="/settings/folders" className={paneClass}>Folders list</NavLink>
        {isPrimary && <NavLink to="/settings/aliases" className={paneClass}>Aliases</NavLink>}
        <NavLink to="/settings/identities" className={paneClass}>Identities</NavLink>
        {rulesAvailable && <NavLink to="/settings/rules" className={paneClass}>Rules</NavLink>}
        {isAdmin && isPrimary && <NavLink to="/settings/admin" className={paneClass}>Administration</NavLink>}
        {/* Switching mailbox from settings: the same menu the folder column carries. */}
        <div className="settings-nav-foot"><IdentityMenu /></div>
      </nav>
      <div className="settings-content">
        <Outlet />
      </div>
    </div>
  )
}
