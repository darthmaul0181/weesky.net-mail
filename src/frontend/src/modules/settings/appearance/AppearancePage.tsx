import { useTheme, type ThemePreference, type Palette } from '../../../contexts/ThemeContext'

const THEMES: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

const PALETTES: { value: Palette; label: string }[] = [
  { value: 'night', label: 'Night & coral (default)' },
  { value: 'classic', label: 'Classic' },
  { value: 'forest', label: 'Forest & amber' },
  { value: 'slate', label: 'Slate & teal' },
  { value: 'plum', label: 'Plum & gold' },
  { value: 'ink', label: 'Ink' },
]

/** Renders in the palette it advertises rather than the active one: the palette selectors are
    attribute-based and unanchored to <html>, so stamping both attributes here re-declares all
    33 tokens on this subtree. */
function PalettePreview({ value, dark }: { value: Palette; dark: boolean }) {
  return (
    <span
      className="palette-preview"
      data-palette={value}
      data-theme={dark ? 'dark' : 'light'}
      aria-hidden="true"
    >
      <span className="pp-bar" />
      <span className="pp-body">
        <span className="pp-rail" />
        <span className="pp-rows">
          <span className="pp-row is-unread" />
          <span className="pp-row" />
          <span className="pp-row" />
        </span>
      </span>
    </span>
  )
}

export default function AppearancePage() {
  const { theme, setTheme, palette, setPalette, isDark } = useTheme()
  return (
    <div className="settings-page">
      <h1>Appearance</h1>

      <section className="account-section">
        <h2>Theme</h2>
        {THEMES.map(({ value, label }) => (
          <label key={value} className="radio-row">
            <input
              type="radio"
              name="theme"
              checked={theme === value}
              onChange={() => setTheme(value)}
            />
            {label}
          </label>
        ))}
      </section>

      <section className="account-section">
        <h2>Palette</h2>
        <div className="palette-grid">
          {PALETTES.map(({ value, label }) => (
            <label key={value} className="palette-card">
              <input
                type="radio"
                name="palette"
                value={value}
                checked={palette === value}
                onChange={() => setPalette(value)}
              />
              <PalettePreview value={value} dark={isDark} />
              {label}
            </label>
          ))}
        </div>
      </section>
    </div>
  )
}
