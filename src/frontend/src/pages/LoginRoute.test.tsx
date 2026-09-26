import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router'
import RequireAuth from '../layouts/RequireAuth'
import LoginRoute from './LoginRoute'

const auth = vi.hoisted(() => ({ isLoggedIn: false, syncFromSession: vi.fn() }))
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => auth }))
vi.mock('../hooks/useTabTitle', () => ({ useTabTitle: () => {} }))
vi.mock('./LoginPage', () => ({
  default: ({ onLogin }: { onLogin: () => void }) =>
    <button type="button" onClick={() => { auth.isLoggedIn = true; onLogin() }}>Sign in</button>,
}))

function Here() {
  const { pathname, search } = useLocation()
  return <span>at {pathname}{search}</span>
}

function renderAt(entry: string | { pathname: string; state: unknown }) {
  const router = createMemoryRouter([
    { path: '/login', element: <LoginRoute /> },
    { element: <RequireAuth />, children: [{ path: '*', element: <Here /> }] },
  ], { initialEntries: [entry] })
  render(<RouterProvider router={router} />)
}

describe('LoginRoute', () => {
  beforeEach(() => { auth.isLoggedIn = false })

  it('returns to the requested page, query intact, after signing in', async () => {
    renderAt('/mail?folder=INBOX&uid=7')

    ;(await screen.findByRole('button', { name: 'Sign in' })).click()

    expect(await screen.findByText('at /mail?folder=INBOX&uid=7')).toBeInTheDocument()
  })

  it("lands the installed app's mailto link on the composer", async () => {
    renderAt('/mail/compose?mailto=mailto%3Abob%40example.com')

    ;(await screen.findByRole('button', { name: 'Sign in' })).click()

    expect(await screen.findByText('at /mail/compose?mailto=mailto%3Abob%40example.com'))
      .toBeInTheDocument()
  })

  it('goes home when the remembered location points elsewhere', async () => {
    renderAt({ pathname: '/login', state: { from: { pathname: '//evil.example', search: '', hash: '' } } })

    ;(await screen.findByRole('button', { name: 'Sign in' })).click()

    expect(await screen.findByText('at /')).toBeInTheDocument()
  })

  it('sends a signed-in visitor of the login page to the remembered page', async () => {
    auth.isLoggedIn = true
    renderAt({ pathname: '/login', state: { from: { pathname: '/contacts', search: '', hash: '' } } })

    expect(await screen.findByText('at /contacts')).toBeInTheDocument()
  })
})
