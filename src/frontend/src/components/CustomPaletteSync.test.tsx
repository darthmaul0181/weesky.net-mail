import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useEffect } from 'react'
import { act, render, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider, useTheme } from '../contexts/ThemeContext'
import CustomPaletteSync from './CustomPaletteSync'
import { createTestQueryClient } from '../test-utils'
import { CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_STYLE_ID } from '../lib/customPaletteStyle'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({ isLoggedIn: true }) }))

let pick: (p: 'classic' | 'custom') => void = () => {}
function Picker() {
  const { setPalette } = useTheme()
  useEffect(() => { pick = setPalette })
  return null
}

function renderSync(value: string) {
  mocks.getPreferences.mockResolvedValue({ 'ui.customPalette': value })
  return render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider><CustomPaletteSync /><Picker /></ThemeProvider>
    </QueryClientProvider>,
  )
}

describe('CustomPaletteSync', () => {
  beforeEach(() => { localStorage.clear(); document.getElementById(CUSTOM_PALETTE_STYLE_ID)?.remove() })

  it("writes the account's palette", async () => {
    renderSync('265,muted,35')
    await waitFor(() => expect(document.getElementById(CUSTOM_PALETTE_STYLE_ID)).not.toBeNull())
    expect(localStorage.getItem('appearance_custom_palette')).toBe('265,muted,35,structure')
  })

  // Another account on the same device, or an answer that names no palette for a while: night is
  // drawn, but the device's choice is not overwritten — it is the owner's, and the palette may return.
  it('draws night when the account has none, without forgetting the choice', async () => {
    localStorage.setItem('appearance_palette', 'custom')
    localStorage.setItem('appearance_custom_palette', '100,vivid,200')
    localStorage.setItem(CUSTOM_PALETTE_CSS_KEY, "[data-palette='custom'] {}")
    renderSync('')

    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('night'))
    expect(localStorage.getItem('appearance_palette')).toBe('custom')
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBeNull()
  })

  // The reported bug: a device left on "custom" whose mirror was lost reopened on night for good.
  it('returns to the custom palette once the account names it again', async () => {
    localStorage.setItem('appearance_palette', 'custom')
    renderSync('265,muted,35,accent')

    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('custom'))
    expect(localStorage.getItem('appearance_palette')).toBe('custom')
  })
})

describe('CustomPaletteSync — cost', () => {
  beforeEach(() => { localStorage.clear(); document.getElementById(CUSTOM_PALETTE_STYLE_ID)?.remove() })

  // Picking another card changes nothing about the account's palette: no re-parse, no rewrite.
  it('leaves the stylesheet alone when only the active palette changes', async () => {
    renderSync('265,muted,35')
    await waitFor(() => expect(document.getElementById(CUSTOM_PALETTE_STYLE_ID)).not.toBeNull())
    const style = document.getElementById(CUSTOM_PALETTE_STYLE_ID)!
    const writes = vi.fn()
    new MutationObserver(writes).observe(style, { childList: true, characterData: true, subtree: true })

    act(() => pick('classic'))
    await new Promise(resolve => setTimeout(resolve, 0))

    expect(writes).not.toHaveBeenCalled()
  })
})
