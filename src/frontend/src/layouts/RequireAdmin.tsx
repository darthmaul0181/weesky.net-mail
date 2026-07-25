import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function RequireAdmin() {
  const { isAdmin, accountLoaded } = useAuth()
  if (!accountLoaded) return null // account still loading — decide once known
  if (!isAdmin) return <Navigate to="/settings/account" replace />
  return <Outlet />
}
