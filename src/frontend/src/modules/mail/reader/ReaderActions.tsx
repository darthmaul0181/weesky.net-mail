import Tooltip from '../../../components/Tooltip'
import KebabIcon from '../../../icons/KebabIcon'
import MoonIcon from '../../../icons/MoonIcon'
import SunIcon from '../../../icons/SunIcon'

interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
}

/** Icon AND tooltip describe the action to come, not the current state — the validated design
    (spec 2026-07-22-reader-actions): the sun promises the sender's colours, the moon the way
    back. Pairing them to the current state instead is the rejected alternative, not a bug. */
export default function ReaderActions({ showColourToggle, originalColours, onToggleColours }: Props) {
  return (
    <div className="reader-actions">
      {showColourToggle && (
        <>
          <Tooltip
            placement="bottom-right"
            content={originalColours
              ? 'Colours are adapted to your dark theme.'
              : 'Showing the colours the sender chose.'}
          >
            <button
              type="button"
              className="action-btn"
              aria-label={originalColours ? 'Match my theme' : 'Original colours'}
              onClick={onToggleColours}
            >
              {originalColours ? <MoonIcon /> : <SunIcon />}
            </button>
          </Tooltip>
          <span className="actions-rule" />
        </>
      )}
      <button type="button" className="action-btn" aria-label="Message actions">
        <KebabIcon />
      </button>
    </div>
  )
}
