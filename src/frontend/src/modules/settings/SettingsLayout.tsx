import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import IdentityMenu from '../../layouts/IdentityMenu'
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
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
import RefreshIcon from '../../icons/RefreshIcon'

function paneClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'pane-item is-active' : 'pane-item'
}

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  end?: boolean
}

export default function SettingsLayout() {
  const { isAdmin, activeAccount, capabilities } = useAuth()
  const { t } = useTranslation('settings')
  const { pathname } = useLocation()
  const drawer = useContextDrawer()
  // `!== false`, not `=== true`: activeAccount is null while the account list loads, and the
  // primary nav must stay full during that window rather than flash away and back. Capabilities
  // read the same way, and for the same reason — null while it loads, absent on a backend that
  // predates it — and the two never gate the same tab: a connected account's Rules answers to its
  // own sieveSupported, never to the platform's capabilities.
  const isPrimary = activeAccount?.isPrimary !== false
  const aliasesAvailable = capabilities?.aliases !== false
  const davAvailable = capabilities?.dav !== false
  const adminAvailable = capabilities?.admin !== false
  const rulesAvailable = isPrimary
    ? capabilities?.rules !== false
    : activeAccount?.sieveSupported !== false

  // One list, two readers: the rows below and the narrow bar's title. A second copy of the
  // labels would drift the day one of them is renamed.
  const items: NavItem[] = [
    ...(isPrimary ? [{ to: '/settings/account', label: t('nav.account'), icon: <UserIcon size={16} />, end: true }] : []),
    { to: '/settings/general', label: t('nav.general'), icon: <SlidersIcon size={16} /> },
    { to: '/settings/accounts', label: t('nav.accounts'), icon: <PersonPlusIcon size={16} /> },
    { to: '/settings/appearance', label: t('nav.appearance'), icon: <DropletIcon size={16} /> },
    { to: '/settings/folders', label: t('nav.folders'), icon: <FolderIcon size={16} /> },
    ...(isPrimary && aliasesAvailable ? [{ to: '/settings/aliases', label: t('nav.aliases'), icon: <AtSignIcon size={16} /> }] : []),
    { to: '/settings/identities', label: t('nav.identities'), icon: <MailIcon size={16} /> },
    // Gated isPrimary like Account and Aliases: the secret authenticates the weesky user, and a
    // connected external account has neither an address book nor a principal here.
    ...(isPrimary && davAvailable ? [{ to: '/settings/sync', label: t('nav.sync'), icon: <RefreshIcon size={16} /> }] : []),
    ...(rulesAvailable ? [{ to: '/settings/rules', label: t('nav.rules'), icon: <FunnelIcon size={16} /> }] : []),
    ...(isAdmin && isPrimary && adminAvailable ? [{ to: '/settings/admin', label: t('nav.admin'), icon: <ShieldIcon size={16} /> }] : []),
  ]

  // The module name is the fallback, not the answer: it is what /settings shows for the frame of
  // a render before the index route's redirect lands.
  const section = items.find(item => pathname === item.to || pathname.startsWith(`${item.to}/`))

  const nav = (
    <nav className="context-pane" aria-label={t('nav.label')}>
      {items.map(item => (
        <NavLink key={item.to} to={item.to} end={item.end} className={paneClass}>{item.icon}{item.label}</NavLink>
      ))}
      {/* Switching mailbox from settings: the same menu the folder column carries. */}
      <div className="settings-nav-foot"><IdentityMenu /></div>
    </nav>
  )

  return (
    <div className="settings-layout">
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{nav}</ContextDrawer>
        : nav}
      <div className="settings-content">
        {/* The only module that needs a band of its own: its nine pages each draw their own
            .settings-page-header, so a hamburger placed there would be written nine times. */}
        {drawer.inDrawer && (
          <div className="settings-mobile-bar">
            <DrawerToggle onClick={drawer.toggle} />
            <span className="settings-mobile-title">{section?.label ?? t('nav.label')}</span>
          </div>
        )}
        <Outlet />
      </div>
    </div>
  )
}
