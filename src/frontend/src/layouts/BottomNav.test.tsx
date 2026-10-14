import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { createTestQueryClient, withQueryClient } from '../test-utils'
import AppRail from './AppRail'
import BottomNav from './BottomNav'
import { MODULES } from './modules'

vi.mock('../api.js', () => ({ api: { markWhatsNew: vi.fn().mockResolvedValue(null) } }))

function names(container: HTMLElement) {
  return [...container.querySelectorAll('a')].map(a => a.getAttribute('href'))
}

function renderAt(ui: React.ReactElement, path = '/mail', dailyImage = false) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': String(dailyImage) })
  client.setQueryData(['whatsNew'], { baseline: '0.0.0', items: [] })
  return render(<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>, { wrapper: withQueryClient(client) })
}

describe('BottomNav', () => {
  it('offers the same destinations as the rail', () => {
    const rail = renderAt(<AppRail />)
    const bottom = renderAt(<BottomNav />)
    // Both read modules.ts. A module added to one and not the other is the bug this catches.
    expect(names(bottom.container)).toEqual(names(rail.container))
  })

  it('covers every module plus settings', () => {
    const { container } = renderAt(<BottomNav />)
    expect(names(container)).toHaveLength(MODULES.length + 1)
  })

  it('labels each destination in text, not only in aria', () => {
    renderAt(<BottomNav />)
    // A bar of bare glyphs is unreadable at 56px; the label is what makes it a tab bar.
    expect(screen.getByText('Mail')).toBeTruthy()
    expect(screen.getByText('Settings')).toBeTruthy()
  })

  it('dots the Settings tab while a novelty waits behind it, and keeps its label', () => {
    renderAt(<BottomNav />, '/mail', true)
    const dot = screen.getByRole('img', { name: 'New' })
    expect(dot.closest('a')).toHaveAttribute('href', '/settings')
    expect(dot.closest('a')).toHaveTextContent('Settings')
    // The dot is read after the label, as the rail's own name reads.
    expect(screen.getByRole('link', { name: 'Settings New' })).toBe(dot.closest('a'))
  })

  it('draws no dot while nothing is new', () => {
    renderAt(<BottomNav />)
    expect(screen.queryByRole('img', { name: 'New' })).toBeNull()
  })
})

describe('AppRail', () => {
  // jsdom computes no colour: this fixes the `.is-active .new-dot` structure the stylesheet's
  // active-ink rule targets, since a coral dot on the coral active fill would disappear.
  it('paints the dot in the active ink on the active rail item', () => {
    renderAt(<AppRail />, '/settings', true)
    const dot = screen.getByRole('img', { name: 'New' })
    expect(dot.closest('.is-active')).not.toBeNull()
    expect(dot.closest('a')).toHaveAttribute('href', '/settings')
  })

  // The link's aria-label hides the dot from a reader: the name itself says New while it is lit.
  it('says New in the name of a dotted rail item, and nowhere else', () => {
    renderAt(<AppRail />, '/mail', true)
    expect(screen.getByRole('link', { name: 'Settings New' })).toHaveAttribute('title', 'Settings')
    expect(screen.getByRole('link', { name: 'Mail' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Settings' })).toBeNull()
  })

  it('names the settings item plainly once nothing is new', () => {
    renderAt(<AppRail />, '/mail')
    expect(screen.getByRole('link', { name: 'Settings' })).toBeInTheDocument()
  })

  it('leaves the other modules undotted', () => {
    renderAt(<AppRail />, '/mail', true)
    expect(screen.getAllByRole('img', { name: 'New' })).toHaveLength(1)
  })
})
