import { Outlet } from 'react-router-dom'
import AppRail from './AppRail'

export default function AppShell() {
  return (
    <div className="app-shell">
      <header className="app-topbar">{/* TopBar content lands in Task 7 */}</header>
      <div className="app-shell-body">
        <AppRail />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
