import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom'
import RequireAuth from './layouts/RequireAuth'
import RequireAdmin from './layouts/RequireAdmin'
import AppShell from './layouts/AppShell'
import LoginRoute from './pages/LoginRoute'
import ComingSoon from './components/ComingSoon'
import SettingsLayout from './modules/settings/SettingsLayout'

const RulesPage = lazy(() => import('./modules/settings/rules/RulesPage.jsx'))

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginRoute /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <Navigate to="/mail" replace /> },
          { path: 'mail', element: <ComingSoon module="Mail" /> },
          { path: 'calendar', element: <ComingSoon module="Calendar" /> },
          { path: 'contacts', element: <ComingSoon module="Contacts" /> },
          {
            path: 'settings',
            element: <SettingsLayout />,
            children: [
              { index: true, element: <Navigate to="/settings/account" replace /> },
              { path: 'account', element: <ComingSoon module="Account" /> },        // Task 11
              { path: 'accounts', element: <ComingSoon module="Linked accounts" /> }, // sub-project 2
              { path: 'appearance', element: <ComingSoon module="Appearance" /> },  // Task 12
              { path: 'aliases', element: <ComingSoon module="Aliases" /> },        // Task 13
              { path: 'rules', element: <Suspense fallback={null}><RulesPage /></Suspense> },
              {
                element: <RequireAdmin />,
                children: [{ path: 'admin', element: <ComingSoon module="Administration" /> }], // Task 10
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
