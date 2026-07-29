import { Outlet } from 'react-router-dom'
import { useMailNotifications } from '../modules/mail/notify/useMailNotifications'
import { useTabTitle } from '../hooks/useTabTitle'
import AppRail from './AppRail'
import TopBar from './TopBar'

export default function AppShell() {
  // Here rather than in MailLayout: new mail must ring from settings and calendar too.
  useMailNotifications()
  // Same reason: the tab names the mailbox from every section, not only the mail one.
  useTabTitle()

  return (
    <div className="app-shell">
      <TopBar />
      <div className="app-shell-body">
        <AppRail />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
