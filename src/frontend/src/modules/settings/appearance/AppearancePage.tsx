import { useTheme, type ThemePreference, type Palette } from '../../../contexts/ThemeContext'

const THEMES: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

const PALETTES: { value: Palette; label: string }[] = [
  { value: 'night', label: 'Night & coral (default)' },
  { value: 'classic', label: 'Classic' },
]

export default function AppearancePage() {
  const { theme, setTheme, palette, setPalette } = useTheme()
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
        {PALETTES.map(({ value, label }) => (
          <label key={value} className="radio-row">
            <input
              type="radio"
              name="palette"
              checked={palette === value}
              onChange={() => setPalette(value)}
            />
            {label}
          </label>
        ))}
      </section>
    </div>
  )
}
