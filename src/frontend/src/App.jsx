import { useState, useEffect } from 'react'
import { hasSession, setUnauthorizedHandler } from './api.js'
import LoginPage from './pages/LoginPage.jsx'
import AliasesPage from './pages/AliasesPage.jsx'

export default function App() {
  const [loggedIn, setLoggedIn] = useState(hasSession)

  useEffect(() => {
    setUnauthorizedHandler(() => setLoggedIn(false))
    return () => setUnauthorizedHandler(null)
  }, [])

  if (!loggedIn) {
    return <LoginPage onLogin={() => setLoggedIn(true)} />
  }

  return <AliasesPage onLogout={() => setLoggedIn(false)} />
}
