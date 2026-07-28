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
import FoldersPage from './modules/settings/mail/FoldersPage'
import GeneralPage from './modules/settings/general/GeneralPage'

const MailLayout = lazy(() => import('./modules/mail/MailLayout'))
const ContactsLayout = lazy(() => import('./modules/contacts/ContactsLayout'))
const AliasesPage = lazy(() => import('./modules/settings/aliases/AliasesPage.jsx'))
const IdentitiesPage = lazy(() => import('./modules/settings/identities/IdentitiesPage'))
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
          // The composer lives inside the mail module: same layout, list and reader replaced.
          { path: 'mail/compose', element: <Suspense fallback={null}><MailLayout /></Suspense> },
          { path: 'calendar', element: <ComingSoon module="Calendar" /> },
          { path: 'contacts', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
          // The editor lives inside the contacts module: same layout, the two content columns
          // replaced. A contact id is a GUID, so it travels safely in a route segment.
          { path: 'contacts/new', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
          { path: 'contacts/:id/edit', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
          {
            path: 'settings',
            element: <SettingsLayout />,
            children: [
              { index: true, element: <Navigate to="/settings/account" replace /> },
              { path: 'account', element: <AccountPage /> },
              { path: 'general', element: <GeneralPage /> },
              { path: 'accounts', element: <ComingSoon module="Linked accounts" /> }, // sub-project 2
              { path: 'appearance', element: <AppearancePage /> },
              { path: 'folders', element: <FoldersPage /> },
              // The folders page grew out of the old system-folders one; keep its URL working.
              { path: 'system-folders', element: <Navigate to="/settings/folders" replace /> },
              { path: 'aliases', element: <Suspense fallback={null}><AliasesPage /></Suspense> },
              { path: 'identities', element: <Suspense fallback={null}><IdentitiesPage /></Suspense> },
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
