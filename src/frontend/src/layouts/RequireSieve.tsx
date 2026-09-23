import { Navigate, Outlet } from 'react-router'
import { useAuth } from '../contexts/AuthContext'

// `!== false`, as in RequirePrimary: activeAccount and capabilities are null while they load. A
// connected account answers to its own sieveSupported, the primary also to capabilities.rules, and
// that redirect waits on `accountsLoading` (docs/architecture-shell.md).
export default function RequireSieve() {
  const { activeAccount, accountsLoading, capabilities } = useAuth()
  const isPrimary = activeAccount?.isPrimary !== false
  if (activeAccount?.sieveSupported === false) return <Navigate to="/settings/general" replace />
  if (!accountsLoading && isPrimary && capabilities?.rules === false) {
    return <Navigate to="/settings/general" replace />
  }
  return <Outlet />
}
