import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import RequirePrimary from './RequirePrimary'

const auth = vi.hoisted(() => ({ activeAccount: null as { isPrimary: boolean } | null }))
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => auth }))

function renderGuard() {
  return render(
    <MemoryRouter initialEntries={['/settings/account']}>
      <Routes>
        <Route element={<RequirePrimary />}>
          <Route path="/settings/account" element={<span>Account page</span>} />
        </Route>
        <Route path="/settings/general" element={<span>General page</span>} />
      </Routes>
    </MemoryRouter>
  )
}

describe('RequirePrimary', () => {
  it('renders the outlet for the primary account', () => {
    auth.activeAccount = { isPrimary: true }
    renderGuard()
    expect(screen.getByText('Account page')).toBeInTheDocument()
  })

  // The loading pin: activeAccount is null before the connected-accounts query resolves, and
  // that must read as "let it through", never as a redirect that then flips back.
  it('renders the outlet while the account list is still loading (activeAccount null)', () => {
    auth.activeAccount = null
    renderGuard()
    expect(screen.getByText('Account page')).toBeInTheDocument()
  })

  it('redirects a connected (non-primary) account to General', () => {
    auth.activeAccount = { isPrimary: false }
    renderGuard()
    expect(screen.getByText('General page')).toBeInTheDocument()
  })
})
