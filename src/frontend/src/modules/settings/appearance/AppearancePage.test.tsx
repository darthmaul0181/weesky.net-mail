import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider, PALETTE_IDS } from '../../../contexts/ThemeContext'
import AppearancePage from './AppearancePage'

describe('AppearancePage', () => {
  beforeEach(() => localStorage.clear())

  it('reflects current preferences', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    expect(screen.getByLabelText('System')).toBeChecked()
    expect(screen.getByLabelText(/Night & coral/)).toBeChecked()
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

  it('offers every palette the app knows, in order', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const radios = screen.getAllByRole('radio', { name: /Night|Sea breeze|Forest|Slate|Plum|Ink/ })
    expect(radios.map(r => (r as HTMLInputElement).value))
      .toEqual(['night', 'classic', 'forest', 'slate', 'plum', 'ink'])
    expect(new Set(radios.map(r => (r as HTMLInputElement).value)))
      .toEqual(new Set(PALETTE_IDS))
  })

  it('changes to a new palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    fireEvent.click(screen.getByLabelText('Plum & gold'))

    expect(document.documentElement.getAttribute('data-palette')).toBe('plum')
    expect(localStorage.getItem('appearance_palette')).toBe('plum')
  })

  // Each thumbnail declares the palette it advertises, which is the only thing standing between
  // six previews and six copies of the active one.
  it('previews each palette in its own colours', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const previews = Array.from(container.querySelectorAll('.palette-preview'))
    expect(previews.map(p => p.getAttribute('data-palette')))
      .toEqual(['night', 'classic', 'forest', 'slate', 'plum', 'ink'])
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
