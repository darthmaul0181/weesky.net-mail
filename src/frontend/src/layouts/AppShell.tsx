import { Outlet } from 'react-router-dom'
import { useMailNotifications } from '../modules/mail/notify/useMailNotifications'
import AppRail from './AppRail'
import TopBar from './TopBar'

export default function AppShell() {
  // Here rather than in MailLayout: new mail must ring from settings and calendar too.
  useMailNotifications()

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
