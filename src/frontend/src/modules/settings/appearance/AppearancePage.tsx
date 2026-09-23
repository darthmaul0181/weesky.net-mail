import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocale } from '../../../contexts/LocaleContext'
import { useTheme, type ThemePreference, type Palette } from '../../../contexts/ThemeContext'
import Modal from '../../../components/Modal'
import DropletIcon from '../../../icons/DropletIcon'
import SearchIcon from '../../../icons/SearchIcon'

const LANGUAGES = [
  { value: 'auto', labelKey: 'appearance.language.auto' as const },
  // Written in their own language on purpose: a francophone lost in an English interface does not
  // search for "French". These two are therefore not translated and not in a catalogue.
  { value: 'en', label: 'English' },
  { value: 'fr', label: 'Français' },
]

const THEMES = [
  { value: 'light', labelKey: 'appearance.theme.light' },
  { value: 'dark', labelKey: 'appearance.theme.dark' },
  { value: 'system', labelKey: 'appearance.theme.system' },
] as const satisfies { value: ThemePreference; labelKey: string }[]

// Product names, not prose: they stay out of the catalogue. Only the "(default)" suffix moves.
const PALETTES: { value: Palette; name: string; isDefault?: boolean }[] = [
  { value: 'night', name: 'Night & coral', isDefault: true },
  { value: 'classic', name: 'Sea breeze' },
  { value: 'forest', name: 'Forest & amber' },
  { value: 'slate', name: 'Slate & teal' },
  { value: 'plum', name: 'Plum & gold' },
  { value: 'ink', name: 'Ink' },
  { value: 'azure', name: 'Azure' },
  { value: 'indigo', name: 'Indigo & violet' },
]

/** Renders in the palette it advertises: the palette selectors are attribute-based and unanchored,
 * so stamping both attributes re-declares every token here. It shows --action-primary (compose,
 * attachment chip), which tells palettes apart; `large` only scales --pp. */
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
  const { t } = useTranslation('settings')
  return (
    // The loupe again, so the dialog reads as the enlargement of what was clicked.
    <Modal icon={<SearchIcon size={16} />} title={label} onClose={onClose} className="palette-zoom-modal">
      <div className="palette-zoom-pair">
        {[false, true].map(dark => (
          <figure key={String(dark)}>
            <PalettePreview value={value} dark={dark} large />
            <figcaption>{t(dark ? 'appearance.theme.dark' : 'appearance.theme.light')}</figcaption>
          </figure>
        ))}
      </div>
    </Modal>
  )
}

export default function AppearancePage() {
  const { theme, setTheme, palette, setPalette, isDark } = useTheme()
  const { preference, setPreference } = useLocale()
  const { t } = useTranslation('settings')
  const [zoomed, setZoomed] = useState<{ value: Palette; label: string } | null>(null)

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><DropletIcon size={17} />{t('nav.appearance')}</h1>
      </div>

      <section className="account-section">
        <h2 id="language-heading">{t('appearance.language.heading')}</h2>
        <div className="seg" role="radiogroup" aria-labelledby="language-heading">
          {LANGUAGES.map(({ value, label, labelKey }) => (
            <label key={value}>
              <input
                type="radio"
                name="language"
                checked={preference === value}
                onChange={() => setPreference(value)}
              />
              {labelKey ? t(labelKey) : label}
            </label>
          ))}
        </div>
      </section>

      <section className="account-section">
        <h2 id="theme-heading">{t('appearance.theme.heading')}</h2>
        <div className="seg" role="radiogroup" aria-labelledby="theme-heading">
          {THEMES.map(({ value, labelKey }) => (
            <label key={value}>
              <input
                type="radio"
                name="theme"
                checked={theme === value}
                onChange={() => setTheme(value)}
              />
              {t(labelKey)}
            </label>
          ))}
        </div>
      </section>

      <section className="account-section">
        <h2>{t('appearance.palette.heading')}</h2>
        <div className="palette-grid">
          {PALETTES.map(({ value, name, isDefault }) => {
            const label = isDefault ? t('appearance.palette.default', { name }) : name
            // The loupe sits outside the label on purpose: inside one, clicking it would activate
            // the label's radio and pick the palette as a side effect of asking to look at it.
            return (
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
                  aria-label={t('appearance.palette.enlarge', { name: label })}
                  onClick={() => setZoomed({ value, label })}
                >
                  <SearchIcon size={14} />
                </button>
              </div>
            )
          })}
        </div>
      </section>

      {zoomed && (
        <PaletteZoomModal value={zoomed.value} label={zoomed.label} onClose={() => setZoomed(null)} />
      )}
    </div>
  )
}
