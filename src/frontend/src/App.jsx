import { useState, useEffect } from 'react'
import { hasSession, setUnauthorizedHandler } from './api.js'
import LoginPage from './pages/LoginPage.jsx'
import AliasesPage from './pages/AliasesPage.jsx'
import RulesPage from './pages/RulesPage.jsx'

export default function App() {
  const [loggedIn, setLoggedIn] = useState(hasSession)
  const [page, setPage] = useState('aliases')

  useEffect(() => {
    setUnauthorizedHandler(() => { setLoggedIn(false); setPage('aliases') })
    return () => setUnauthorizedHandler(null)
  }, [])

  if (!loggedIn) {
    return <LoginPage onLogin={() => setLoggedIn(true)} />
  }

  if (page === 'rules') {
    return <RulesPage onBack={() => setPage('aliases')} />
  }

  return (
    <AliasesPage
      onLogout={() => setLoggedIn(false)}
      onOpenRules={() => setPage('rules')}
    />
  )
}
