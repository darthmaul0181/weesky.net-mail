import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { readStored, writeStored } from '../lib/safeStorage'
import { applyCustomPalette, hasCustomPaletteMirror } from '../lib/customPaletteStyle'
import type { CustomPaletteDef } from '../lib/customPalette'

export type ThemePreference = 'light' | 'dark' | 'system'

export const PALETTE_IDS = [
  'night', 'classic', 'forest', 'slate', 'plum', 'ink', 'azure', 'indigo',
] as const
export type BuiltinPalette = typeof PALETTE_IDS[number]
export type Palette = BuiltinPalette | 'custom'

interface ThemeContextValue {
  theme: ThemePreference
  /** What is drawn: the device's choice, or night while its custom palette is not declared. */
  palette: Palette
  /** The preference resolved against the OS — "system" on its own says nothing. */
  isDark: boolean
  setTheme: (t: ThemePreference) => void
  setPalette: (p: Palette) => void
  /** Declares the account's palette, or that it has none; the device's choice is left alone. */
  declareCustomPalette: (def: CustomPaletteDef | null) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

const THEME_KEY = 'appearance_theme'
const PALETTE_KEY = 'appearance_palette'

function readTheme(): ThemePreference {
  const v = readStored(THEME_KEY)
  return v === 'light' || v === 'dark' ? v : 'system'
}

function readPalette(): Palette {
  const stored = readStored(PALETTE_KEY)
  if (stored === 'custom') return 'custom'
  return PALETTE_IDS.includes(stored as BuiltinPalette) ? stored as BuiltinPalette : 'night'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(readTheme)
  const [choice, setChoice] = useState<Palette>(readPalette)
  const [customDeclared, setCustomDeclared] = useState(hasCustomPaletteMirror)
  const palette = choice === 'custom' && !customDeclared ? 'night' : choice
  const [isDark, setIsDark] = useState(false)

  useEffect(() => {
    function apply() {
      const dark = theme === 'dark' ||
        (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light')
      setIsDark(dark)
    }
    apply()
    if (theme !== 'system') return
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    mq.addEventListener('change', apply)
    return () => mq.removeEventListener('change', apply)
  }, [theme])

  useEffect(() => {
    document.documentElement.setAttribute('data-palette', palette)
  }, [palette])

  function setTheme(t: ThemePreference) {
    writeStored(THEME_KEY, t)
    setThemeState(t)
  }

  function setPalette(p: Palette) {
    writeStored(PALETTE_KEY, p)
    setChoice(p)
  }

  const declareCustomPalette = useCallback((def: CustomPaletteDef | null) => {
    applyCustomPalette(def)
    setCustomDeclared(def !== null)
  }, [])

  return (
    <ThemeContext.Provider value={{ theme, palette, isDark, setTheme, setPalette, declareCustomPalette }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider')
  return ctx
}
