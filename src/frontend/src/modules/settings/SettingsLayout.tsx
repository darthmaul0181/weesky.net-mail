import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import IdentityMenu from '../../layouts/IdentityMenu'
// Each row wears the icon its own page's title wears — the site's trigger/title continuity rule.
// Changing one without the other is what the rule exists to prevent.
import UserIcon from '../../icons/UserIcon'
import SlidersIcon from '../../icons/SlidersIcon'
import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'
import DropletIcon from '../../icons/DropletIcon'
import FolderIcon from '../../icons/FolderIcon'
import AtSignIcon from '../../icons/AtSignIcon'
import MailIcon from '../../icons/MailIcon'
import FunnelIcon from '../../icons/FunnelIcon'
import ShieldIcon from '../../icons/ShieldIcon.jsx'

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
        {isPrimary && <NavLink to="/settings/account" end className={paneClass}><UserIcon size={16} />Account</NavLink>}
        <NavLink to="/settings/general" className={paneClass}><SlidersIcon size={16} />General</NavLink>
        <NavLink to="/settings/accounts" className={paneClass}><PersonPlusIcon size={16} />Connected accounts</NavLink>
        <NavLink to="/settings/appearance" className={paneClass}><DropletIcon size={16} />Appearance</NavLink>
        <NavLink to="/settings/folders" className={paneClass}><FolderIcon size={16} />Folders</NavLink>
        {isPrimary && <NavLink to="/settings/aliases" className={paneClass}><AtSignIcon size={16} />Aliases</NavLink>}
        <NavLink to="/settings/identities" className={paneClass}><MailIcon size={16} />Identities</NavLink>
        {rulesAvailable && <NavLink to="/settings/rules" className={paneClass}><FunnelIcon size={16} />Rules</NavLink>}
        {isAdmin && isPrimary && <NavLink to="/settings/admin" className={paneClass}><ShieldIcon size={16} />Administration</NavLink>}
        {/* Switching mailbox from settings: the same menu the folder column carries. */}
        <div className="settings-nav-foot"><IdentityMenu /></div>
      </nav>
      <div className="settings-content">
        <Outlet />
      </div>
    </div>
  )
}
