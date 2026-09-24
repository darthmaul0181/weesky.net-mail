import { Navigate, Outlet } from 'react-router'
import { useAuth, type AuthContextValue } from '../contexts/AuthContext'

interface Props {
  allow: (auth: AuthContextValue) => boolean | 'wait'
  redirect: string
}

/** A route guard as one predicate: 'wait' withholds the outlet while a decision cannot be made
 *  yet (avoids a flash-redirect), false sends the visitor elsewhere, true lets them through. */
export default function Gate({ allow, redirect }: Props) {
  const decision = allow(useAuth())
  if (decision === 'wait') return null
  if (!decision) return <Navigate to={redirect} replace />
  return <Outlet />
}
