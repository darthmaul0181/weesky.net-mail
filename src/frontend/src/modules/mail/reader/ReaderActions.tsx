import Tooltip from '../../../components/Tooltip'
import DropdownMenu from '../../../components/DropdownMenu'
import KebabIcon from '../../../icons/KebabIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import MoonIcon from '../../../icons/MoonIcon'
import StarIcon from '../../../icons/StarIcon'
import SunIcon from '../../../icons/SunIcon'

interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
  seen: boolean
  flagged: boolean
  onToggleSeen: () => void
  onToggleFlagged: () => void
}

/** Icon AND tooltip describe the action to come, not the current state — the validated design
    (spec 2026-07-22-reader-actions): the sun promises the sender's colours, the moon the way
    back. Pairing them to the current state instead is the rejected alternative, not a bug. */
export default function ReaderActions({
  showColourToggle, originalColours, onToggleColours, seen, flagged, onToggleSeen, onToggleFlagged,
}: Props) {
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
      <DropdownMenu
        ariaLabel="Message actions"
        className="action-btn"
        trigger={<KebabIcon />}
        items={[
          {
            label: seen ? 'Mark as unread' : 'Mark as read',
            icon: seen ? <MailIcon size={16} /> : <MailOpenIcon size={16} />,
            onSelect: onToggleSeen,
          },
          {
            label: flagged ? 'Unstar' : 'Star',
            icon: <StarIcon filled={flagged} />,
            onSelect: onToggleFlagged,
          },
        ]}
      />
    </div>
  )
}
