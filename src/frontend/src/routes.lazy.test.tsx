import { act, render, screen } from '@testing-library/react'
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

const held = vi.hoisted(() => {
  let release = () => {}
  const gate = new Promise<void>(resolve => { release = resolve })
  return { gate, release: () => release() }
})

const heldGeneral = vi.hoisted(() => {
  let release = () => {}
  const gate = new Promise<void>(resolve => { release = resolve })
  return { gate, release: () => release() }
})

vi.mock('./layouts/RequireAuth', () => ({ default: () => <Outlet /> }))
vi.mock('./layouts/AppShell', () => ({ default: () => <Outlet /> }))
vi.mock('./layouts/Gate', () => ({ default: () => <Outlet /> }))
vi.mock('./modules/mail/MailLayout', () => ({ default: () => <p>mail module</p> }))
// Held until the test releases it: the chunk of a module visited for the first time.
vi.mock('./modules/calendar/CalendarLayout', async () => {
  await held.gate
  return { default: () => <p>calendar module</p> }
})
vi.mock('./modules/settings/SettingsLayout', () => ({
  default: () => (
    <div>
      <nav>settings nav</nav>
      <Outlet />
    </div>
  ),
}))
vi.mock('./modules/settings/account/AccountPage', () => ({ default: () => <p>account page</p> }))
// Held until the test releases it: a settings sub-page chunk not yet fetched.
vi.mock('./modules/settings/general/GeneralPage', async () => {
  await heldGeneral.gate
  return { default: () => <p>general page</p> }
})

const { routes } = await import('./routes')

describe('lazy module routes', () => {
  // A router navigation is a transition, which keeps a revealed Suspense boundary on screen while
  // the next module loads. Each module owns its boundary, so the old one leaves at once.
  it('clears the previous module while the next one is still loading', async () => {
    const router = createMemoryRouter(routes, { initialEntries: ['/mail'] })
    render(<RouterProvider router={router} />)
    await screen.findByText('mail module')

    await act(() => router.navigate('/calendar'))

    expect(screen.queryByText('mail module')).toBeNull()
    held.release()
    expect(await screen.findByText('calendar module')).toBeInTheDocument()
  })

  // SettingsLayout owns its own Suspense boundary, one level above the Outlet: a sub-page's
  // boundary is nested inside it, so switching pages re-suspends only the content area and
  // never unmounts the nav sitting beside it.
  it('keeps the settings nav mounted while a sub-page chunk loads', async () => {
    const router = createMemoryRouter(routes, { initialEntries: ['/settings/account'] })
    render(<RouterProvider router={router} />)
    await screen.findByText('account page')
    expect(screen.getByText('settings nav')).toBeInTheDocument()

    await act(() => router.navigate('/settings/general'))

    expect(screen.getByText('settings nav')).toBeInTheDocument()
    expect(screen.queryByText('general page')).toBeNull()
    heldGeneral.release()
    expect(await screen.findByText('general page')).toBeInTheDocument()
    expect(screen.getByText('settings nav')).toBeInTheDocument()
  })
})
