import { useTranslation } from 'react-i18next'
import Tooltip from '../../../components/Tooltip'
import DropdownMenu, { type MenuEntry } from '../../../components/DropdownMenu'
import ForwardIcon from '../../../icons/ForwardIcon'
import KebabIcon from '../../../icons/KebabIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import MoonIcon from '../../../icons/MoonIcon'
import ReplyIcon from '../../../icons/ReplyIcon'
import ReplyAllIcon from '../../../icons/ReplyAllIcon'
import StarIcon from '../../../icons/StarIcon'
import SunIcon from '../../../icons/SunIcon'
import TrashIcon from '../../../icons/TrashIcon'

interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
  seen: boolean
  flagged: boolean
  onToggleSeen: () => void
  onToggleFlagged: () => void
  deleteLabel: string
  deleteDisabled: boolean
  onDelete: () => void
  actions: MenuEntry[]
  onReply: () => void
  onReplyAll: () => void
  onForward: () => void
  /** A PrepareQuote round-trip is in flight — the quote actions hold until it lands. */
  preparing: boolean
}

/** Icon AND tooltip describe the action to come, not the current state — the validated design
    (spec 2026-07-22-reader-actions): the sun promises the sender's colours, the moon the way
    back. Pairing them to the current state instead is the rejected alternative, not a bug. */
export default function ReaderActions({
  showColourToggle, originalColours, onToggleColours, seen, flagged, onToggleSeen, onToggleFlagged,
  deleteLabel, deleteDisabled, onDelete, actions,
  onReply, onReplyAll, onForward, preparing,
}: Props) {
  const { t } = useTranslation('mail')

  return (
    <div className="reader-actions">
      <button type="button" className="action-btn" aria-label={t('reader.reply')} title={t('reader.reply')}
        disabled={preparing} onClick={onReply}>
        <ReplyIcon size={18} />
      </button>
      <button type="button" className="action-btn" aria-label={t('reader.replyAll')} title={t('reader.replyAll')}
        disabled={preparing} onClick={onReplyAll}>
        <ReplyAllIcon size={18} />
      </button>
      <button type="button" className="action-btn" aria-label={t('reader.forward')} title={t('reader.forward')}
        disabled={preparing} onClick={onForward}>
        <ForwardIcon size={18} />
      </button>
      <span className="actions-rule" />
      {showColourToggle && (
        <>
          <Tooltip
            placement="bottom-right"
            content={t(originalColours ? 'reader.coloursAdapted' : 'reader.coloursOriginal')}
          >
            <button
              type="button"
              className="action-btn"
              aria-label={t(originalColours ? 'reader.matchTheme' : 'reader.originalColours')}
              onClick={onToggleColours}
            >
              {originalColours ? <MoonIcon size={18} /> : <SunIcon size={18} />}
            </button>
          </Tooltip>
          <span className="actions-rule" />
        </>
      )}
      <button
        type="button"
        className="action-btn is-danger"
        aria-label={deleteLabel}
        disabled={deleteDisabled}
        title={deleteDisabled ? t('actions.noTrashFolder') : undefined}
        onClick={onDelete}
      >
        <TrashIcon size={18} />
      </button>
      <DropdownMenu
        ariaLabel={t('reader.messageActions')}
        className="action-btn"
        trigger={<KebabIcon size={18} />}
        items={[
          {
            label: t(seen ? 'toolbar.markUnread' : 'toolbar.markRead'),
            icon: seen ? <MailIcon size={18} /> : <MailOpenIcon size={18} />,
            onSelect: onToggleSeen,
          },
          {
            label: t(flagged ? 'list.unstar' : 'list.star'),
            icon: <StarIcon filled={flagged} size={18} />,
            onSelect: onToggleFlagged,
          },
          // A separator under nothing reads as a rendering fault, so it comes only with a group.
          ...(actions.length ? ['separator' as const, ...actions] : []),
        ]}
      />
    </div>
  )
}
