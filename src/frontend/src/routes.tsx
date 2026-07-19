import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom'
import RequireAuth from './layouts/RequireAuth'
import RequireAdmin from './layouts/RequireAdmin'
import AppShell from './layouts/AppShell'
import LoginRoute from './pages/LoginRoute'
import ComingSoon from './components/ComingSoon'
import SettingsLayout from './modules/settings/SettingsLayout'
import AccountPage from './modules/settings/account/AccountPage'
import AppearancePage from './modules/settings/appearance/AppearancePage'
import SystemFoldersPage from './modules/settings/mail/SystemFoldersPage'

const MailLayout = lazy(() => import('./modules/mail/MailLayout'))
const AliasesPage = lazy(() => import('./modules/settings/aliases/AliasesPage.jsx'))
const RulesPage = lazy(() => import('./modules/settings/rules/RulesPage.jsx'))
const AdminPage = lazy(() => import('./modules/settings/admin/AdminPage.jsx'))

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginRoute /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <Navigate to="/mail" replace /> },
          { path: 'mail', element: <Suspense fallback={null}><MailLayout /></Suspense> },
          { path: 'calendar', element: <ComingSoon module="Calendar" /> },
          { path: 'contacts', element: <ComingSoon module="Contacts" /> },
          {
            path: 'settings',
            element: <SettingsLayout />,
            children: [
              { index: true, element: <Navigate to="/settings/account" replace /> },
              { path: 'account', element: <AccountPage /> },
              { path: 'accounts', element: <ComingSoon module="Linked accounts" /> }, // sub-project 2
              { path: 'appearance', element: <AppearancePage /> },
              { path: 'system-folders', element: <SystemFoldersPage /> },
              { path: 'aliases', element: <Suspense fallback={null}><AliasesPage /></Suspense> },
              { path: 'rules', element: <Suspense fallback={null}><RulesPage /></Suspense> },
              {
                element: <RequireAdmin />,
                children: [{ path: 'admin', element: <Suspense fallback={null}><AdminPage /></Suspense> }],
              },
            ],
          },
          { path: '*', element: <Navigate to="/mail" replace /> },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
