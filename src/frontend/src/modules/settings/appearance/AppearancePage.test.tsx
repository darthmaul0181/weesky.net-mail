import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider } from '../../../contexts/ThemeContext'
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
    fireEvent.click(screen.getByLabelText('Classic'))
    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
    expect(localStorage.getItem('appearance_palette')).toBe('classic')
  })
})
