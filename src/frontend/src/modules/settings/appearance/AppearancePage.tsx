import { useEffect, useState } from 'react'
import { useTheme, type ThemePreference, type Palette } from '../../../contexts/ThemeContext'
import SearchIcon from '../../../icons/SearchIcon'

const THEMES: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

const PALETTES: { value: Palette; label: string }[] = [
  { value: 'night', label: 'Night & coral (default)' },
  { value: 'classic', label: 'Sea breeze' },
  { value: 'forest', label: 'Forest & amber' },
  { value: 'slate', label: 'Slate & teal' },
  { value: 'plum', label: 'Plum & gold' },
  { value: 'ink', label: 'Ink' },
  { value: 'azure', label: 'Azure' },
  { value: 'indigo', label: 'Indigo & violet' },
]

/** Renders in the palette it advertises rather than the active one: the palette selectors are
    attribute-based and unanchored to <html>, so stamping both attributes here re-declares all
    the tokens on this subtree.

    It summarises what the app actually shows now — the compose button and the attachment chip
    both carry --action-primary, which is the trait that most distinguishes one palette from
    another, and the folder column is --folders-bg rather than --surface. A preview built before
    those two changes showed the accent on a rail item alone and read as six shades of grey.
    `large` only scales --pp: one markup and one rule set for both sizes, so the thumbnail and
    the enlarged view cannot drift apart. */
function PalettePreview({ value, dark, large }: { value: Palette; dark: boolean; large?: boolean }) {
  return (
    <span
      className={`palette-preview${large ? ' is-large' : ''}`}
      data-palette={value}
      data-theme={dark ? 'dark' : 'light'}
      aria-hidden="true"
    >
      <span className="pp-bar" />
      <span className="pp-body">
        <span className="pp-rail">
          <span className="pp-rail-item is-on" />
          <span className="pp-rail-item" />
          <span className="pp-rail-item" />
        </span>
        <span className="pp-pane">
          <span className="pp-compose" />
          <span className="pp-folder is-on" />
          <span className="pp-folder" />
          <span className="pp-folder" />
        </span>
        <span className="pp-rows">
          <span className="pp-row is-unread" />
          <span className="pp-row" />
          <span className="pp-row" />
          <span className="pp-attachments"><span className="pp-chip" /></span>
        </span>
      </span>
    </span>
  )
}

/** Both modes at once, which is the one thing a thumbnail cannot do: it can only ever show the
    mode in use, and a palette is chosen once for both. */
function PaletteZoomModal({ value, label, onClose }: { value: Palette; label: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal palette-zoom-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          {/* The loupe again, so the dialog reads as the enlargement of what was clicked. */}
          <span className="modal-title"><SearchIcon size={16} /> {label}</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <div className="palette-zoom-pair">
          {[false, true].map(dark => (
            <figure key={String(dark)}>
              <PalettePreview value={value} dark={dark} large />
              <figcaption>{dark ? 'Dark' : 'Light'}</figcaption>
            </figure>
          ))}
        </div>
      </div>
    </div>
  )
}

export default function AppearancePage() {
  const { theme, setTheme, palette, setPalette, isDark } = useTheme()
  const [zoomed, setZoomed] = useState<{ value: Palette; label: string } | null>(null)

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
            // The loupe sits outside the label on purpose: inside one, clicking it would activate
            // the label's radio and pick the palette as a side effect of asking to look at it.
            <div key={value} className="palette-card">
              <label className="palette-pick">
                <PalettePreview value={value} dark={isDark} />
                <span className="palette-name">
                  <input
                    type="radio"
                    name="palette"
                    value={value}
                    checked={palette === value}
                    onChange={() => setPalette(value)}
                  />
                  {label}
                </span>
              </label>
              <button
                type="button"
                className="palette-zoom"
                aria-label={`Enlarge the ${label} preview`}
                onClick={() => setZoomed({ value, label })}
              >
                <SearchIcon size={14} />
              </button>
            </div>
          ))}
        </div>
      </section>

      {zoomed && (
        <PaletteZoomModal value={zoomed.value} label={zoomed.label} onClose={() => setZoomed(null)} />
      )}
    </div>
  )
}
