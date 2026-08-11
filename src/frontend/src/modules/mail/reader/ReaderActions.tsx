import type { ReactNode } from 'react'
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
  /** The phone's foot-of-screen shape: five named cells instead of an icon cluster. */
  bar?: boolean
}

/** Icon AND tooltip describe the action to come, not the current state — the validated design
    (spec 2026-07-22-reader-actions): the sun promises the sender's colours, the moon the way
    back. Pairing them to the current state instead is the rejected alternative, not a bug. */
export default function ReaderActions({
  showColourToggle, originalColours, onToggleColours, seen, flagged, onToggleSeen, onToggleFlagged,
  deleteLabel, deleteDisabled, onDelete, actions,
  onReply, onReplyAll, onForward, preparing, bar = false,
}: Props) {
  const { t } = useTranslation('mail')

  const colourEntry: MenuEntry = {
    label: t(originalColours ? 'reader.matchTheme' : 'reader.originalColours'),
    icon: originalColours ? <MoonIcon size={18} /> : <SunIcon size={18} />,
    onSelect: onToggleColours,
  }

  const menuItems: MenuEntry[] = [
    // The bar spends no cell on the toggle: it is conditional on the theme AND on the body being
    // HTML, and a bar whose number of cells moves with either reads as a rendering fault.
    ...(bar && showColourToggle ? [colourEntry, 'separator' as const] : []),
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
  ]

  if (bar) {
    const cell = (
      label: string, name: string, icon: ReactNode,
      onClick: () => void, disabled = false, danger = false, title?: string,
    ) => (
      <button
        type="button"
        className={`reader-bar-item${danger ? ' is-danger' : ''}`}
        aria-label={name}
        title={title}
        disabled={disabled}
        onClick={onClick}
      >
        {icon}
        <span className="reader-bar-label">{label}</span>
      </button>
    )

    return (
      <div className="reader-actions is-bar">
        {cell(t('reader.bar.reply'), t('reader.reply'), <ReplyIcon size={21} />, onReply, preparing)}
        {cell(t('reader.bar.replyAll'), t('reader.replyAll'), <ReplyAllIcon size={21} />, onReplyAll, preparing)}
        {cell(t('reader.bar.forward'), t('reader.forward'), <ForwardIcon size={21} />, onForward, preparing)}
        {/* The visible word stays generic where the accessible name does not: inside the trash
            this expunges, and one cell has no room to say so twice. */}
        {cell(t('reader.bar.delete'), deleteLabel, <TrashIcon size={21} />, onDelete, deleteDisabled,
          true, deleteDisabled ? t('actions.noTrashFolder') : undefined)}
        <DropdownMenu
          direction="up"
          ariaLabel={t('reader.messageActions')}
          className="reader-bar-item"
          trigger={<><KebabIcon size={21} /><span className="reader-bar-label">{t('reader.bar.more')}</span></>}
          items={menuItems}
        />
      </div>
    )
  }

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
        items={menuItems}
      />
    </div>
  )
}
