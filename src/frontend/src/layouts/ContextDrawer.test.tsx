import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useNavigate } from 'react-router-dom'
import ContextDrawer, { useContextDrawer } from './ContextDrawer'
import { changeViewport, mockViewport, resetViewport } from '../test-utils'

afterEach(resetViewport)

function drawer(open: boolean, onClose = vi.fn()) {
  return render(
    <MemoryRouter>
      <ContextDrawer open={open} onClose={onClose}>
        <button type="button">Inbox</button>
      </ContextDrawer>
    </MemoryRouter>,
  )
}

// Mail names its open folder in a search param (?folder=), so proving the route effect reacts
// to a route change means actually navigating within the router, not just re-rendering the same
// location — a rerender with an unchanged MemoryRouter location wouldn't move `search` at all.
function DrawerWithNav({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  return (
    <>
      <ContextDrawer open={open} onClose={onClose}>
        <button type="button">Inbox</button>
      </ContextDrawer>
      <button type="button" onClick={() => navigate('/mail?folder=Sent')}>Navigate</button>
    </>
  )
}

function drawerWithNav(open: boolean, onClose = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/mail?folder=Inbox']}>
      <DrawerWithNav open={open} onClose={onClose} />
    </MemoryRouter>,
  )
}

describe('ContextDrawer', () => {
  it('keeps its children mounted while closed', () => {
    drawer(false)
    // Mounted, not merely present: the folder tree's expand state and its query live in here.
    expect(screen.getByRole('button', { name: 'Inbox' })).toBeTruthy()
  })

  it('marks the open panel as a modal dialog', () => {
    drawer(true)
    const panel = screen.getByRole('dialog')
    expect(panel.getAttribute('aria-modal')).toBe('true')
  })

  it('closes on Escape', async () => {
    const onClose = vi.fn()
    drawer(true, onClose)
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })

  it('closes on a scrim click', async () => {
    const onClose = vi.fn()
    const { container } = drawer(true, onClose)
    await userEvent.click(container.querySelector('.context-drawer-scrim')!)
    expect(onClose).toHaveBeenCalled()
  })

  it('does not listen for Escape while closed', async () => {
    const onClose = vi.fn()
    drawer(false, onClose)
    await userEvent.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()
  })

  it('moves focus into the panel when it opens', () => {
    drawer(true)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Inbox' }))
  })

  it('closes an open drawer when the route search changes', async () => {
    const onClose = vi.fn()
    drawerWithNav(true, onClose)
    onClose.mockClear() // drop the mount-time call so only the navigation's call is asserted
    await userEvent.click(screen.getByRole('button', { name: 'Navigate' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('does not call onClose for a closed drawer on a route change', async () => {
    const onClose = vi.fn()
    drawerWithNav(false, onClose)
    await userEvent.click(screen.getByRole('button', { name: 'Navigate' }))
    expect(onClose).not.toHaveBeenCalled()
  })
})

describe('useContextDrawer', () => {
  it('puts the pane in a drawer below 1024px', () => {
    mockViewport('tablet')
    expect(renderHook(() => useContextDrawer()).result.current.inDrawer).toBe(true)
  })

  it('leaves the pane inline on desktop', () => {
    mockViewport('desktop')
    expect(renderHook(() => useContextDrawer()).result.current.inDrawer).toBe(false)
  })

  it('closes when the viewport grows to desktop', async () => {
    mockViewport('phone')
    const { result } = renderHook(() => useContextDrawer())
    await act(async () => result.current.toggle())
    expect(result.current.open).toBe(true)
    await changeViewport('desktop')
    // A focus trap left armed on a panel nobody can see is worse than a drawer left open.
    expect(result.current.open).toBe(false)
  })
})
