import { Outlet, useMatch } from 'react-router-dom'
import { useMailNotifications } from '../modules/mail/notify/useMailNotifications'
import { useTabTitle } from '../hooks/useTabTitle'
import { useFaviconBadge } from '../hooks/useFaviconBadge'
import AppRail from './AppRail'
import BottomNav from './BottomNav'
import TopBar from './TopBar'

export default function AppShell() {
  // Here rather than in MailLayout: new mail must ring from settings and calendar too.
  useMailNotifications()
  // Same reason: the tab names the mailbox from every section, not only the mail one.
  useTabTitle()
  useFaviconBadge()
  // Composing is a full-screen task with its own send bar, and a tab bar under a software
  // keyboard serves nobody.
  const composing = useMatch('/mail/compose') != null

  return (
    <div className="app-shell">
      <TopBar />
      <div className="app-shell-body">
        <AppRail />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
      {!composing && <BottomNav />}
    </div>
  )
}
