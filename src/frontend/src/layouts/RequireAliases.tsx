import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

// Nested inside RequirePrimary in routes.tsx, so a connected account never reaches here — this
// guard only has to answer the platform's own capabilities.aliases, `!== false` like every other
// gate (null while it loads, or on a backend that predates the endpoint, reads as "available").
export default function RequireAliases() {
  const { capabilities } = useAuth()
  if (capabilities?.aliases === false) return <Navigate to="/settings/general" replace />
  return <Outlet />
}
