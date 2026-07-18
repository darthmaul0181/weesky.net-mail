import { Outlet } from 'react-router-dom'
import AppRail from './AppRail'
import TopBar from './TopBar'

export default function AppShell() {
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
