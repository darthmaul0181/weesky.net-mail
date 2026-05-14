import { useState, useEffect } from 'react'
import { ConfigProvider, App as AntApp } from 'antd'
import { hasToken, setUnauthorizedHandler } from './api.js'
import LoginPage from './pages/LoginPage.jsx'
import AliasesPage from './pages/AliasesPage.jsx'

const theme = {
  token: {
    colorPrimary: '#3450a3',
    colorError: '#dc2626',
    colorSuccess: '#16a34a',
    borderRadius: 4,
    fontFamily: "system-ui, -apple-system, 'Segoe UI', sans-serif",
    fontSize: 14,
    colorBgContainer: '#ffffff',
    colorBgLayout: '#f0f2f5',
    colorBorder: '#dde1e7',
    colorText: '#1a1d23',
    colorTextSecondary: '#6b7280',
  },
}

function Root() {
  const [loggedIn, setLoggedIn] = useState(hasToken)

  useEffect(() => {
    setUnauthorizedHandler(() => setLoggedIn(false))
    return () => setUnauthorizedHandler(null)
  }, [])

  if (!loggedIn) return <LoginPage onLogin={() => setLoggedIn(true)} />
  return <AliasesPage onLogout={() => setLoggedIn(false)} />
}

export default function App() {
  return (
    <ConfigProvider theme={theme}>
      <AntApp>
        <Root />
      </AntApp>
    </ConfigProvider>
  )
}
