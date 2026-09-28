import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from '../contexts/ThemeContext'
import CustomPaletteSync from './CustomPaletteSync'
import { createTestQueryClient } from '../test-utils'
import { CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_STYLE_ID } from '../lib/customPaletteStyle'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({ isLoggedIn: true }) }))

function renderSync(value: string) {
  mocks.getPreferences.mockResolvedValue({ 'ui.customPalette': value })
  return render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider><CustomPaletteSync /></ThemeProvider>
    </QueryClientProvider>,
  )
}

describe('CustomPaletteSync', () => {
  beforeEach(() => { localStorage.clear(); document.getElementById(CUSTOM_PALETTE_STYLE_ID)?.remove() })

  it("writes the account's palette", async () => {
    renderSync('265,muted,35')
    await waitFor(() => expect(document.getElementById(CUSTOM_PALETTE_STYLE_ID)).not.toBeNull())
    expect(localStorage.getItem('appearance_custom_palette')).toBe('265,muted,35')
  })

  // Another account on the same device: its choice of "custom" named someone else's palette.
  it('falls back to night when the account has none', async () => {
    localStorage.setItem('appearance_palette', 'custom')
    localStorage.setItem('appearance_custom_palette', '100,vivid,200')
    localStorage.setItem(CUSTOM_PALETTE_CSS_KEY, "[data-palette='custom'] {}")
    renderSync('')

    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('night'))
    expect(localStorage.getItem('appearance_palette')).toBe('night')
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBeNull()
  })
})
