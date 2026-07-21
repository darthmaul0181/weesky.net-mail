import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type ThemePreference = 'light' | 'dark' | 'system'

export const PALETTE_IDS = ['night', 'classic', 'forest', 'slate', 'plum', 'ink'] as const
export type Palette = typeof PALETTE_IDS[number]

interface ThemeContextValue {
  theme: ThemePreference
  palette: Palette
  /** The preference resolved against the OS — "system" on its own says nothing. */
  isDark: boolean
  setTheme: (t: ThemePreference) => void
  setPalette: (p: Palette) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

const THEME_KEY = 'appearance_theme'
const PALETTE_KEY = 'appearance_palette'

function readTheme(): ThemePreference {
  const v = localStorage.getItem(THEME_KEY)
  return v === 'light' || v === 'dark' ? v : 'system'
}

function readPalette(): Palette {
  const stored = localStorage.getItem(PALETTE_KEY)
  return PALETTE_IDS.includes(stored as Palette) ? stored as Palette : 'night'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(readTheme)
  const [palette, setPaletteState] = useState<Palette>(readPalette)
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
    localStorage.setItem(THEME_KEY, t)
    setThemeState(t)
  }

  function setPalette(p: Palette) {
    localStorage.setItem(PALETTE_KEY, p)
    setPaletteState(p)
  }

  return (
    <ThemeContext.Provider value={{ theme, palette, isDark, setTheme, setPalette }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider')
  return ctx
}
