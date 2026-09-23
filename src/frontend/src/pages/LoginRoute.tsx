import { Navigate, useNavigate } from 'react-router'
import LoginPage from './LoginPage'
import { useAuth } from '../contexts/AuthContext'
import { useTabTitle } from '../hooks/useTabTitle'

export default function LoginRoute() {
  const { isLoggedIn, syncFromSession } = useAuth()
  const navigate = useNavigate()
  // The shell is not mounted here, and the login page is the first thing a new user sees.
  useTabTitle()
  if (isLoggedIn) return <Navigate to="/" replace />
  return (
    <LoginPage
      onLogin={() => {
        syncFromSession()
        void navigate('/', { replace: true })
      }}
    />
  )
}
