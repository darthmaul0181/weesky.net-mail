import { Navigate, useNavigate } from 'react-router-dom'
import LoginPage from './LoginPage.jsx'
import { useAuth } from '../contexts/AuthContext'

export default function LoginRoute() {
  const { isLoggedIn, syncFromSession } = useAuth()
  const navigate = useNavigate()
  if (isLoggedIn) return <Navigate to="/" replace />
  return (
    <LoginPage
      onLogin={() => {
        syncFromSession()
        navigate('/', { replace: true })
      }}
    />
  )
}
