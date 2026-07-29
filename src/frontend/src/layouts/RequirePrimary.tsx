import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

// `!== false`, not `=== true`: activeAccount is null while the account list loads, and the
// primary-only screens must stay reachable during that window rather than flash-redirect away.
export default function RequirePrimary() {
  const { activeAccount } = useAuth()
  if (activeAccount?.isPrimary === false) return <Navigate to="/settings/general" replace />
  return <Outlet />
}
