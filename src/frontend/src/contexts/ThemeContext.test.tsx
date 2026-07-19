import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider, useTheme } from './ThemeContext'

function Probe() {
  const { theme, palette, isDark, setTheme, setPalette } = useTheme()
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <span data-testid="isDark">{String(isDark)}</span>
      <span data-testid="palette">{palette}</span>
      <button onClick={() => setTheme('dark')}>dark</button>
      <button onClick={() => setPalette('classic')}>classic</button>
    </div>
  )
}

describe('ThemeContext', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-palette')
  })

  it('defaults to system theme and night palette', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(screen.getByTestId('theme')).toHaveTextContent('system')
    expect(screen.getByTestId('palette')).toHaveTextContent('night')
    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    // matchMedia stub matches:false → system resolves to light
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })

  it('setTheme("dark") applies attribute and persists', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    fireEvent.click(screen.getByText('dark'))
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('appearance_theme')).toBe('dark')
  })

  // The mail reader inverts the message body when the resolved theme is dark, so it needs the
  // resolved value, not the preference: "system" alone says nothing.
  it('resolves isDark from the preference', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(screen.getByTestId('isDark')).toHaveTextContent('false')

    fireEvent.click(screen.getByText('dark'))

    expect(screen.getByTestId('isDark')).toHaveTextContent('true')
  })

  it('setPalette("classic") applies attribute and persists', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    fireEvent.click(screen.getByText('classic'))
    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
    expect(localStorage.getItem('appearance_palette')).toBe('classic')
  })

  it('reads persisted preferences on mount', () => {
    localStorage.setItem('appearance_theme', 'dark')
    localStorage.setItem('appearance_palette', 'classic')
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(screen.getByTestId('theme')).toHaveTextContent('dark')
    expect(screen.getByTestId('palette')).toHaveTextContent('classic')
  })
})
