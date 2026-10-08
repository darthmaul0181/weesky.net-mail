import { Navigate, useLocation, useNavigate } from 'react-router'
import LoginPage from './LoginPage'
import { useAuth } from '../contexts/AuthContext'
import { useDailyImage } from '../hooks/useDailyImage'
import { useTabTitle } from '../hooks/useTabTitle'
import { returnPathOf } from '../lib/returnPath'

export default function LoginRoute() {
  const { isLoggedIn, syncFromSession } = useAuth()
  const navigate = useNavigate()
  const target = returnPathOf(useLocation().state)
  // The shell is not mounted here, and the login page is the first thing a new user sees.
  useTabTitle()
  const backdrop = useDailyImage()
  if (isLoggedIn) return <Navigate to={target} replace />
  return (
    <LoginPage
      backdrop={backdrop}
      onLogin={() => {
        syncFromSession()
        void navigate(target, { replace: true })
      }}
    />
  )
}
