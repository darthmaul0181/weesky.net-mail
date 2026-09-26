import { lazy, Suspense, type ComponentType } from 'react'
import { createBrowserRouter, Navigate, type RouteObject } from 'react-router'
import RequireAuth from './layouts/RequireAuth'
import Gate from './layouts/Gate'
import { allowAdmin, allowAliases, allowPrimary, allowSieve } from './layouts/gates'
import AppShell from './layouts/AppShell'
import LoginRoute from './pages/LoginRoute'
import RouteError from './pages/RouteError'
import { hasSession } from './api.js'

const mailChunk = () => import('./modules/mail/MailLayout')
// A returning session almost always lands on mail; starting the fetch here, at module
// evaluation, beats waiting for the router to match /mail and Suspense to trigger it.
if (hasSession()) void mailChunk()
const MailLayout = lazy(mailChunk)
const ContactsLayout = lazy(() => import('./modules/contacts/ContactsLayout'))
const CalendarLayout = lazy(() => import('./modules/calendar/CalendarLayout'))
const SettingsLayout = lazy(() => import('./modules/settings/SettingsLayout'))
const AccountPage = lazy(() => import('./modules/settings/account/AccountPage'))
const ConnectedAccountsPage = lazy(() => import('./modules/settings/accounts/ConnectedAccountsPage'))
const AppearancePage = lazy(() => import('./modules/settings/appearance/AppearancePage'))
const FoldersPage = lazy(() => import('./modules/settings/mail/FoldersPage'))
const GeneralPage = lazy(() => import('./modules/settings/general/GeneralPage'))
const AliasesPage = lazy(() => import('./modules/settings/aliases/AliasesPage'))
const IdentitiesPage = lazy(() => import('./modules/settings/identities/IdentitiesPage'))
const RulesPage = lazy(() => import('./modules/settings/rules/RulesPage'))
const AdminPage = lazy(() => import('./modules/settings/admin/AdminPage'))
const MessageSourceView = lazy(() => import('./modules/mail/source/MessageSourceView'))
const SyncPage = lazy(() => import('./modules/settings/sync/SyncPage'))
const AboutPage = lazy(() => import('./modules/settings/about/AboutPage'))

// Keyed per module: a navigation is a transition, which keeps a revealed boundary on the previous
// module until the next chunk lands. A boundary of its own blanks at once, as v6 did.
function loaded(key: string, Page: ComponentType) {
  return <Suspense key={key} fallback={null}><Page /></Suspense>
}

export const routes: RouteObject[] = [
  {
    // A pathless wrapper: its only job is the errorElement, so a route that throws — or a stale
    // lazy-chunk import — is caught wherever it happens, /login included.
    errorElement: <RouteError />,
    children: [
      { path: '/login', element: <LoginRoute /> },
      {
        element: <RequireAuth />,
        children: [
          // A sibling of AppShell, not a child: that placement is what leaves the rail and the
          // folder column out, and with them useFolders' poll, in a tab that only shows a text file.
          { path: 'mail/source', element: loaded('mail/source', MessageSourceView) },
          {
            element: <AppShell />,
            children: [
              { index: true, element: <Navigate to="/mail" replace /> },
              { path: 'mail', element: loaded('mail', MailLayout) },
              // The composer lives inside the mail module: same layout, list and reader replaced.
              { path: 'mail/compose', element: loaded('mail', MailLayout) },
              { path: 'calendar', element: loaded('calendar', CalendarLayout) },
              // The event editor lives inside the calendar module: same layout, a surface over
              // the grid. An event id is a GUID, so it travels safely in a route segment.
              { path: 'calendar/new', element: loaded('calendar', CalendarLayout) },
              { path: 'calendar/:id/edit', element: loaded('calendar', CalendarLayout) },
              { path: 'contacts', element: loaded('contacts', ContactsLayout) },
              // The editor lives inside the contacts module: same layout, the two content columns
              // replaced. A contact id is a GUID, so it travels safely in a route segment.
              { path: 'contacts/new', element: loaded('contacts', ContactsLayout) },
              { path: 'contacts/:id/edit', element: loaded('contacts', ContactsLayout) },
              {
                path: 'settings',
                element: loaded('settings', SettingsLayout),
                children: [
                  { index: true, element: <Navigate to="/settings/account" replace /> },
                  {
                    element: <Gate allow={allowPrimary} redirect="/settings/general" />,
                    children: [
                      { path: 'account', element: loaded('account', AccountPage) },
                      { path: 'sync', element: loaded('sync', SyncPage) },
                      {
                        element: <Gate allow={allowAliases} redirect="/settings/general" />,
                        children: [
                          { path: 'aliases', element: loaded('aliases', AliasesPage) },
                        ],
                      },
                    ],
                  },
                  { path: 'general', element: loaded('general', GeneralPage) },
                  { path: 'accounts', element: loaded('accounts', ConnectedAccountsPage) },
                  { path: 'appearance', element: loaded('appearance', AppearancePage) },
                  { path: 'folders', element: loaded('folders', FoldersPage) },
                  // The folders page grew out of the old system-folders one; keep its URL working.
                  { path: 'system-folders', element: <Navigate to="/settings/folders" replace /> },
                  { path: 'identities', element: loaded('identities', IdentitiesPage) },
                  { path: 'about', element: loaded('about', AboutPage) },
                  {
                    element: <Gate allow={allowSieve} redirect="/settings/general" />,
                    children: [{ path: 'rules', element: loaded('rules', RulesPage) }],
                  },
                  {
                    element: <Gate allow={allowAdmin} redirect="/settings/account" />,
                    children: [{ path: 'admin', element: loaded('admin', AdminPage) }],
                  },
                ],
              },
              { path: '*', element: <Navigate to="/mail" replace /> },
            ],
          },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
