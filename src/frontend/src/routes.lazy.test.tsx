import { act, render, screen } from '@testing-library/react'
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

const held = vi.hoisted(() => {
  let release = () => {}
  const gate = new Promise<void>(resolve => { release = resolve })
  return { gate, release: () => release() }
})

vi.mock('./layouts/RequireAuth', () => ({ default: () => <Outlet /> }))
vi.mock('./layouts/AppShell', () => ({ default: () => <Outlet /> }))
vi.mock('./modules/mail/MailLayout', () => ({ default: () => <p>mail module</p> }))
// Held until the test releases it: the chunk of a module visited for the first time.
vi.mock('./modules/calendar/CalendarLayout', async () => {
  await held.gate
  return { default: () => <p>calendar module</p> }
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
})
