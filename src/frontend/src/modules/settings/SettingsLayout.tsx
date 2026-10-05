import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet, useLocation } from 'react-router'
import { useAuth } from '../../contexts/AuthContext'
import IdentityMenu from '../../layouts/IdentityMenu'
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
// Each row wears the icon its own page's title wears — the site's trigger/title continuity rule.
// Changing one without the other is what the rule exists to prevent.
import UserIcon from '../../icons/UserIcon'
import SlidersIcon from '../../icons/SlidersIcon'
import PersonPlusIcon from '../../icons/PersonPlusIcon'
import DropletIcon from '../../icons/DropletIcon'
import FolderIcon from '../../icons/FolderIcon'
import AtSignIcon from '../../icons/AtSignIcon'
import MailIcon from '../../icons/MailIcon'
import FunnelIcon from '../../icons/FunnelIcon'
import ShieldIcon from '../../icons/ShieldIcon'
import RefreshIcon from '../../icons/RefreshIcon'
import InfoIcon from '../../icons/InfoIcon'

function paneClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'pane-item is-active' : 'pane-item'
}

const GROUPS = ['mail', 'calendar', 'application'] as const
type NavGroup = typeof GROUPS[number]

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  end?: boolean
  group?: NavGroup
}

export default function SettingsLayout() {
  const { isAdmin, activeAccount, capabilities } = useAuth()
  const { t } = useTranslation('settings')
  const { pathname } = useLocation()
  const drawer = useContextDrawer()
  // `!== false`: activeAccount and capabilities are null while they load (capabilities absent on an
  // older backend), and the nav must not flash away and back. A connected account's Rules answers
  // to its own sieveSupported, never to the platform's capabilities.
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
    { to: '/settings/appearance', label: t('nav.appearance'), icon: <DropletIcon size={16} /> },
    // Gated isPrimary like Account and Aliases: the secret authenticates the weesky user, and a
    // connected external account has neither an address book nor a principal here. Ungrouped: it
    // syncs the calendars and the address book alike.
    ...(isPrimary && davAvailable ? [{ to: '/settings/sync', label: t('nav.sync'), icon: <RefreshIcon size={16} /> }] : []),
    { to: '/settings/mail', label: t('nav.general'), icon: <SlidersIcon size={16} />, group: 'mail' },
    { to: '/settings/accounts', label: t('nav.accounts'), icon: <PersonPlusIcon size={16} />, group: 'mail' },
    { to: '/settings/folders', label: t('nav.folders'), icon: <FolderIcon size={16} />, group: 'mail' },
    { to: '/settings/identities', label: t('nav.identities'), icon: <MailIcon size={16} />, group: 'mail' },
    ...(isPrimary && aliasesAvailable ? [{ to: '/settings/aliases', label: t('nav.aliases'), icon: <AtSignIcon size={16} />, group: 'mail' as const }] : []),
    ...(rulesAvailable ? [{ to: '/settings/rules', label: t('nav.rules'), icon: <FunnelIcon size={16} />, group: 'mail' as const }] : []),
    { to: '/settings/calendar', label: t('nav.general'), icon: <SlidersIcon size={16} />, group: 'calendar' },
    // Not gated isPrimary: administration is the deployment's, whichever mailbox is being read.
    ...(isAdmin && adminAvailable ? [{ to: '/settings/admin', label: t('nav.admin'), icon: <ShieldIcon size={16} />, group: 'application' as const }] : []),
    // Last, and gated on nothing: every account reads the same product, on its own mailbox or an
    // attached one. It keeps its group open, so no group heading ever stands over nothing.
    { to: '/settings/about', label: t('nav.about'), icon: <InfoIcon size={16} />, group: 'application' },
  ]

  // The module name is the fallback, not the answer: it is what /settings shows for the frame of
  // a render before the index route's redirect lands.
  const section = items.find(item => pathname === item.to || pathname.startsWith(`${item.to}/`))
  // Two groups each hold a General: the bar names the group, which the nav shows as a heading.
  const sectionTitle = !section ? t('nav.label')
    : section.group ? `${t(`nav.${section.group}`)} · ${section.label}` : section.label

  const link = (item: NavItem) => (
    <NavLink key={item.to} to={item.to} end={item.end} className={paneClass}>{item.icon}{item.label}</NavLink>
  )

  const nav = (
    <nav className="context-pane" aria-label={t('nav.label')}>
      {items.filter(item => !item.group).map(link)}
      {GROUPS.map(group => (
        <div key={group} className="pane-group" role="group" aria-labelledby={`settings-nav-${group}`}>
          <span id={`settings-nav-${group}`} className="pane-group-label">{t(`nav.${group}`)}</span>
          {items.filter(item => item.group === group).map(link)}
        </div>
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
            <span className="settings-mobile-title">{sectionTitle}</span>
          </div>
        )}
        <Outlet />
      </div>
    </div>
  )
}
