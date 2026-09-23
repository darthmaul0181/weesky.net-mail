import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// A render-time throw anywhere under the auth guard, standing in for a real one (a stale chunk,
// a bug in a route component) without needing the rest of the authenticated tree (AuthContext,
// QueryClient, …) to mount just to reach something deep enough to throw.
vi.mock('./layouts/RequireAuth', () => ({ default: () => { throw new Error('boom') } }))

const { routes } = await import('./routes')

// Rethrows the same way `RouteError.test.tsx` sees, for the same reason: React's dev-mode
// invokeGuardedCallbackDev reports a caught render error a second time as an uncaught window
// error, which jsdom would otherwise print on top of the console.error call React makes itself.
function preventLog(event: ErrorEvent) {
  event.preventDefault()
}

describe('routes', () => {
  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    window.addEventListener('error', preventLog)
  })

  afterEach(() => {
    window.removeEventListener('error', preventLog)
    vi.restoreAllMocks()
  })

  // Proves the actual wiring in routes.tsx, not a reconstruction of it: the root entry is
  // pathless and carries `errorElement: <RouteError />`, so a throw anywhere under it — here,
  // the auth guard every authenticated route sits behind — lands on the error page rather than
  // an unstyled React overlay or a blank tab.
  it('catches a render-time throw under the root errorElement', async () => {
    const router = createMemoryRouter(routes, { initialEntries: ['/mail'] })
    render(<RouterProvider router={router} />)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong on this page.')
  })
})
