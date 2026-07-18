import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom'
import RequireAuth from './layouts/RequireAuth'
import AppShell from './layouts/AppShell'
import LoginRoute from './pages/LoginRoute'
import ComingSoon from './components/ComingSoon'

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
          // /settings subtree lands in Task 8
          { path: '*', element: <Navigate to="/mail" replace /> },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
