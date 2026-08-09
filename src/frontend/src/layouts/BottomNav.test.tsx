import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import AppRail from './AppRail'
import BottomNav from './BottomNav'
import { MODULES, SETTINGS_MODULE } from './modules'

function names(container: HTMLElement) {
  return [...container.querySelectorAll('a')].map(a => a.getAttribute('href'))
}

describe('BottomNav', () => {
  it('offers the same destinations as the rail', () => {
    const rail = render(<MemoryRouter><AppRail /></MemoryRouter>)
    const bottom = render(<MemoryRouter><BottomNav /></MemoryRouter>)
    // Both read modules.ts. A module added to one and not the other is the bug this catches.
    expect(names(bottom.container)).toEqual(names(rail.container))
  })

  it('covers every module plus settings', () => {
    const { container } = render(<MemoryRouter><BottomNav /></MemoryRouter>)
    expect(names(container)).toHaveLength(MODULES.length + 1)
    expect(SETTINGS_MODULE.to).toBe('/settings')
  })

  it('labels each destination in text, not only in aria', () => {
    render(<MemoryRouter><BottomNav /></MemoryRouter>)
    // A bar of bare glyphs is unreadable at 56px; the label is what makes it a tab bar.
    expect(screen.getByText('Mail')).toBeTruthy()
    expect(screen.getByText('Settings')).toBeTruthy()
  })
})
