import type { Palette } from '../../../contexts/ThemeContext'
import type { TokenSet } from '../../../lib/customPalette'

/** Renders in the palette it advertises: the palette selectors are attribute-based and unanchored,
 * so stamping both attributes re-declares every token here. It shows --action-primary (compose,
 * attachment chip), which tells palettes apart; `large` only scales --pp. `tokens`, inline, win
 * over the attributes: the custom palette's editor previews a draft nothing has declared yet. */
export default function PalettePreview({ value, dark, large, tokens }: {
  value: Palette; dark: boolean; large?: boolean; tokens?: TokenSet
}) {
  return (
    <span
      className={`palette-preview${large ? ' is-large' : ''}`}
      data-palette={value}
      data-theme={dark ? 'dark' : 'light'}
      style={tokens}
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
