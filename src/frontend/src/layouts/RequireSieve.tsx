import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

// Same `!== false` shape as RequirePrimary, and for the same reason: activeAccount is null
// while the account list loads, and the primary account always carries sieveSupported: true.
export default function RequireSieve() {
  const { activeAccount } = useAuth()
  if (activeAccount?.sieveSupported === false) return <Navigate to="/settings/general" replace />
  return <Outlet />
}
