import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider, PALETTE_IDS } from '../../../contexts/ThemeContext'
import AppearancePage from './AppearancePage'

describe('AppearancePage', () => {
  beforeEach(() => localStorage.clear())

  // By role, not by label text: the loupe's own label names the palette too — deliberately, so
  // twelve of them are told apart by a screen reader — which makes getByLabelText ambiguous.
  it('reflects current preferences', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    expect(screen.getByLabelText('System')).toBeChecked()
    expect(screen.getByRole('radio', { name: /Night & coral/ })).toBeChecked()
  })

  it('changes the theme', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    fireEvent.click(screen.getByLabelText('Dark'))
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('appearance_theme')).toBe('dark')
  })

  it('changes the palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    fireEvent.click(screen.getByLabelText('Sea breeze'))
    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
    expect(localStorage.getItem('appearance_palette')).toBe('classic')
  })

  // Selected by group rather than by a regex over every label, which had to grow with the list.
  it('offers every palette the app knows, in order', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const radios = screen.getAllByRole('radio')
      .filter(r => (r as HTMLInputElement).name === 'palette') as HTMLInputElement[]

    expect(radios.map(r => r.value)).toEqual([...PALETTE_IDS])
  })

  it('changes to a new palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    fireEvent.click(screen.getByLabelText('Plum & gold'))

    expect(document.documentElement.getAttribute('data-palette')).toBe('plum')
    expect(localStorage.getItem('appearance_palette')).toBe('plum')
  })

  // Each thumbnail declares the palette it advertises, which is the only thing standing between
  // twelve previews and twelve copies of the active one.
  it('previews each palette in its own colours', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const previews = Array.from(container.querySelectorAll('.palette-preview'))
    expect(previews.map(p => p.getAttribute('data-palette'))).toEqual([...PALETTE_IDS])
  })

  // The accent is what tells two palettes apart, and it reaches the running app mostly through
  // the compose button and the attachment chips. A preview without them was twelve near-greys.
  it('shows the accent surfaces the app actually carries', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const first = container.querySelector('.palette-preview')!
    expect(first.querySelector('.pp-compose')).not.toBeNull()
    expect(first.querySelector('.pp-chip')).not.toBeNull()
  })

  // The stored preference may be "system", which names no mode: the preview has to show the
  // mode the user is actually in, so it reads the resolved value.
  it('previews in the resolved theme, not the stored preference', () => {
    localStorage.setItem('appearance_theme', 'system')
    const original = window.matchMedia
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation(query => ({
        matches: true,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    })

    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    Array.from(container.querySelectorAll('.palette-preview'))
      .forEach(p => expect(p.getAttribute('data-theme')).toBe('dark'))

    Object.defineProperty(window, 'matchMedia', { writable: true, value: original })
  })

  // The label already names the palette; a screen reader has no use for a picture of colours.
  it('hides the thumbnails from assistive technology', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    Array.from(container.querySelectorAll('.palette-preview'))
      .forEach(p => expect(p).toHaveAttribute('aria-hidden', 'true'))
  })
})

describe('AppearancePage — the enlarged preview', () => {
  beforeEach(() => localStorage.clear())

  const open = (name: string) => {
    const rendered = render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    fireEvent.click(screen.getByRole('button', { name: `Enlarge the ${name} preview` }))
    return rendered
  }

  // The whole reason the loupe sits outside the <label>: inside one, a click on it activates the
  // label's control, so asking to look at a palette would have applied it.
  it('does not select the palette it enlarges', () => {
    const { container } = open('Sea breeze')

    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    expect(localStorage.getItem('appearance_palette')).toBeNull()
    expect(screen.getByRole('radio', { name: /Night & coral/ })).toBeChecked()
    expect(container.querySelector('.modal-title')?.textContent).toContain('Sea breeze')
  })

  // A thumbnail can only ever show the mode in use, and a palette is chosen once for both.
  it('shows the enlarged palette in both modes', () => {
    const { container } = open('Ink')

    const large = Array.from(container.querySelectorAll('.palette-preview.is-large'))
    expect(large.map(p => p.getAttribute('data-theme'))).toEqual(['light', 'dark'])
    large.forEach(p => expect(p.getAttribute('data-palette')).toBe('ink'))
  })

  it('closes on the ✕', () => {
    const { container } = open('Azure')

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(container.querySelector('.palette-zoom-modal')).toBeNull()
  })

  it('closes on Escape', () => {
    const { container } = open('Azure')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(container.querySelector('.palette-zoom-modal')).toBeNull()
  })

  it('offers a loupe per palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    expect(screen.getAllByRole('button', { name: /^Enlarge the .* preview$/ }))
      .toHaveLength(PALETTE_IDS.length)
  })
})
