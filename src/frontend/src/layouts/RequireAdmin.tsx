import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

// capabilities reads `!== false` like every other gate: null while it is still loading (or on a
// backend that predates the endpoint) must read as "available", never as a flash-redirect.
export default function RequireAdmin() {
  const { isAdmin, accountLoaded, capabilities } = useAuth()
  if (!accountLoaded) return null // account still loading — decide once known
  if (!isAdmin || capabilities?.admin === false) return <Navigate to="/settings/account" replace />
  return <Outlet />
}
