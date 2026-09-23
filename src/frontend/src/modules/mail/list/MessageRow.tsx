import { memo, useRef } from 'react'
import type {
  CSSProperties, DragEvent, HTMLAttributes, KeyboardEvent, ReactNode,
} from 'react'
import { useTranslation } from 'react-i18next'
import type { RowAction } from '../../../hooks/usePreferences'
import type { MailMessageSummary } from '../api/mailTypes'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import JunkIcon from '../../../icons/JunkIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon'
import { formatListDate } from './formatDate'
import { useLongPress } from '../../../hooks/useLongPress'

// A component only so the long-press hook can live outside the rows' `.map()`. A held press still
// ends in a click on touch browsers, which `onClickCapture` eats.
function Row({ onLongPress, children, ...rest }:
  { onLongPress?: () => void; children: ReactNode } & HTMLAttributes<HTMLDivElement>) {
  const fired = useRef(false)
  const { onPointerDown, ...press } = useLongPress(() => {
    if (!onLongPress) return  // A cross-folder result: no selection to enter, so no click to eat.
    fired.current = true
    onLongPress()
  })
  return (
    <div
      {...rest}
      {...press}
      onPointerDown={event => { fired.current = false; onPointerDown(event) }}
      onClickCapture={event => {
        if (!fired.current) return
        fired.current = false
        // Both are load-bearing and neither replaces the other: stopPropagation keeps the click
        // from the checkbox's own listener, preventDefault is what cancels the input's native
        // activation — without it the box still toggles and undoes the selection just made.
        event.preventDefault()
        event.stopPropagation()
      }}
    >
      {children}
    </div>
  )
}

export interface CheckGesture {
  was: boolean
  whole: boolean
  shift: boolean
}

/** The uids one row acts on: a conversation head's whole fold, or the single message. */
export function rowUidsOf(message: MailMessageSummary, members?: MailMessageSummary[]): number[] {
  return members ? members.map(one => one.uid) : [message.uid]
}

// Built once and never rebuilt, so a row never re-renders for a re-created callback; each takes the
// uids it acts on, so nothing here closes over a row.
export interface RowCallbacks {
  open: (message: MailMessageSummary) => void
  /** `was` is the box's state before the press, `whole` a conversation head answering for its
      members, `shift` the range gesture. */
  check: (uids: number[], rowIndex: number, how: CheckGesture) => void
  setFlag: (uids: number[], flag: 'seen' | 'flagged', value: boolean) => void
  archive: (uids: number[]) => void
  junk: (uids: number[]) => void
  /** `label` names the row in the trash's confirm dialog. */
  remove: (uids: number[], label: string) => void
  toggleThread: (groupKey: number) => void
  dragStart: (event: DragEvent<HTMLDivElement>, uids: number[]) => void
  dragEnd: () => void
}

export interface MessageRowProps {
  message: MailMessageSummary
  /** The whole fold, on a collapsed conversation head alone: its dot, star and paperclip
      aggregate these, and every control acts on all of them at once. */
  members?: MailMessageSummary[]
  /** The group this row belongs to, which `toggleThread` names. */
  groupKey: number
  expanded: boolean
  member: boolean
  /** Over the flattened members, the order `loadedUids` publishes, so a shift-range stays
      coherent across both shapes. */
  rowIndex: number
  /** What the grid numbers: the rows actually drawn. */
  ariaRow: number
  /** The local calendar day the date label is read against — `useToday()`'s, so a list left open
      overnight redraws its rows once instead of printing yesterday's time for ever. */
  today: string
  wide: boolean
  drafts: boolean
  crossFolder: boolean
  showsPreview: boolean
  rowActions: readonly RowAction[]
  checked: boolean
  open: boolean
  leaving: boolean
  dragging: boolean
  archiveOff: boolean
  archiveReason: string
  junkOff: boolean
  junkReason: string
  trashOff: boolean
  trashReason: string
  deleteLabel: string
  on: RowCallbacks
}

const NO_ACTIONS: readonly RowAction[] = []

// A plain message, a collapsed thread head or an unfolded member. It reads its own catalogue: labels
// interpolating its sender, subject, date or count would cost the list on every render.
function MessageRow({
  message, members, groupKey, expanded, member, rowIndex, ariaRow, wide, drafts, crossFolder,
  today, showsPreview, rowActions, checked, open, leaving, dragging, archiveOff, archiveReason,
  junkOff, junkReason, trashOff, trashReason, deleteLabel, on,
}: MessageRowProps) {
  const { t } = useTranslation('mail')
  const thread = members !== undefined
  const unread = thread ? members.some(one => !one.seen) : !message.seen
  const flagged = thread ? members.some(one => one.flagged) : message.flagged
  const attachments = thread ? members.some(one => one.hasAttachments) : message.hasAttachments
  const rowUids = rowUidsOf(message, members)

  const classes = ['message-row']
  if (wide) classes.push('is-line')
  if (member) classes.push('is-thread-member')
  if (unread) classes.push('is-unread')
  if (open) classes.push('is-selected')
  if (dragging) classes.push('is-dragging')

  const from = drafts
    ? (message.to.length > 0
        ? message.to.map(a => a.name || a.address).join(', ')
        : t('list.noRecipient'))
    : (message.fromName || message.fromAddress)
  const subject = message.subject || t('list.noSubject')
  const when = formatListDate(message.date, today)
  const seenLabel = t(unread ? 'toolbar.markRead' : 'toolbar.markUnread')
  const priorityLabel = message.priority === 'high' ? t('list.highPriority')
    : message.priority === 'low' ? t('list.lowPriority') : null
  // A word in the Draft badge's slot, not a glyph in the subject line: a glyph there sat
  // in the text flow, so a marked row started its subject 17px right of every other one
  // and the column lost the axis the eye scans down.
  const priorityMark = priorityLabel && (
    <span className={`message-row-priority is-${message.priority}`} title={priorityLabel}>
      {t(message.priority === 'high' ? 'list.high' : 'list.low')}
    </span>
  )
  // The row is no longer one button, so the checkbox, the star and the actions answer for
  // themselves — but the name is not shortened here: a name change is audible where a role
  // change is not, and the two together would make a regression impossible to bisect.
  const label = t('list.rowLabel', {
    prefix: `${thread ? `${t('list.threadCount', { count: members.length })}. ` : ''}`
      + `${unread ? t('list.aria.unread') : ''}`
      + `${drafts ? t('list.aria.draft') : ''}`
      + `${priorityLabel ? t('list.aria.priority', { label: priorityLabel }) : ''}`,
    from,
    subject,
    attachments: attachments ? t('list.aria.hasAttachments') : '',
    when,
  })

  // Cross-folder results neutralize row selection and actions: the row lives in another
  // folder, so a checkbox, star or cluster acting on this one would act on the wrong mailbox.
  const check = crossFolder ? null : (
    <input
      type="checkbox"
      className="message-row-check"
      aria-label={t(thread ? 'list.selectThread' : 'list.selectMessage', { from })}
      checked={checked}
      onClick={event => {
        event.stopPropagation()
        on.check(rowUids, rowIndex, { was: checked, whole: thread, shift: event.shiftKey })
      }}
      onChange={() => {}}
    />
  )

  const star = crossFolder ? null : (
    <button
      type="button"
      className={`row-btn row-star${flagged ? ' is-on' : ''}`}
      aria-label={t(flagged ? 'list.unstar' : 'list.star')}
      disabled={leaving}
      onClick={event => { event.stopPropagation(); on.setFlag(rowUids, 'flagged', !flagged) }}
    >
      <StarIcon filled={flagged} size={18} />
    </button>
  )

  // Withheld here is the user's own choice, made in Settings. A button whose role no
  // folder holds is still drawn, disabled, with its reason: that absence would read as
  // a bug, this one was asked for.
  const buttons: Record<RowAction, ReactNode> = {
    seen: (
      <button
        key="seen"
        type="button"
        className="row-btn"
        aria-label={seenLabel}
        title={seenLabel}
        disabled={leaving}
        onClick={event => { event.stopPropagation(); on.setFlag(rowUids, 'seen', unread) }}
      >
        {unread ? <MailOpenIcon size={18} /> : <MailIcon size={18} />}
      </button>
    ),
    archive: (
      <button
        key="archive"
        type="button"
        className="row-btn"
        aria-label={t('toolbar.archive')}
        disabled={leaving || archiveOff}
        title={archiveOff ? archiveReason : t('toolbar.archive')}
        onClick={event => { event.stopPropagation(); on.archive(rowUids) }}
      >
        <ArchiveIcon size={18} />
      </button>
    ),
    junk: (
      <button
        key="junk"
        type="button"
        className="row-btn"
        aria-label={t('toolbar.junk')}
        disabled={leaving || junkOff}
        title={junkOff ? junkReason : t('toolbar.junk')}
        onClick={event => { event.stopPropagation(); on.junk(rowUids) }}
      >
        <JunkIcon size={18} />
      </button>
    ),
    delete: (
      <button
        key="delete"
        type="button"
        className="row-btn is-danger"
        aria-label={deleteLabel}
        disabled={leaving || trashOff}
        title={trashOff ? trashReason : deleteLabel}
        onClick={event => { event.stopPropagation(); on.remove(rowUids, subject) }}
      >
        <TrashIcon size={18} />
      </button>
    ),
  }

  // One value behind both the cluster and the width the row reserves for it: the reserve
  // is what ends the line above in an ellipsis, and a count it derived on its own could
  // disagree with what is actually drawn.
  const shownActions = crossFolder ? NO_ACTIONS : rowActions
  const cluster = shownActions.length === 0 ? null : (
    <div className="message-row-cluster">{shownActions.map(action => buttons[action])}</div>
  )

  // After the date in both skins: how many messages the row stands for, and the way in.
  const threadBits = thread && (
    <>
      <span
        className="message-row-thread-count"
        title={t('list.threadCount', { count: members.length })}
      >
        {members.length}
      </span>
      <button
        type="button"
        className={`row-btn thread-toggle${expanded ? ' is-open' : ''}`}
        aria-expanded={expanded}
        aria-label={t(expanded ? 'list.collapseThread' : 'list.expandThread')}
        onClick={event => { event.stopPropagation(); on.toggleThread(groupKey) }}
      >
        <ChevronRightIcon size={14} />
      </button>
    </>
  )

  // The thread toggle handles its own keys; the cell only opens when the cell itself has focus.
  function onRowKey(event: KeyboardEvent<HTMLDivElement>) {
    if (event.target !== event.currentTarget) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      on.open(message)
    }
  }

  // The slot is the box that collapses; the row inside it only fades. A thread member has its
  // own, so one member can leave without taking the fold's other lines with it. It is
  // presentational, or the grid would own a box of its own instead of the rows.
  return (
    <div
      className={`message-row-slot${leaving ? ' is-leaving' : ''}`}
      role="presentation"
    >
    <Row
      role="row"
      aria-rowindex={ariaRow}
      className={classes.join(' ')}
      style={{ '--row-actions': shownActions.length } as CSSProperties}
      draggable={!crossFolder}
      onClick={() => on.open(message)}
      onDragStart={event => on.dragStart(event, rowUids)}
      onDragEnd={() => on.dragEnd()}
      // Entering selection with no visible checkbox to aim at: the row itself is the target.
      onLongPress={crossFolder ? undefined
        : () => on.check(rowUids, rowIndex, { was: checked, whole: thread, shift: false })}
    >
      {/* Four cells, always four: a row may own nothing but cells, and a count that moved with
          a setting or with the thread would change the grid's shape under the arrows. The
          empty ones hold no widget, which is how the walk steps over them. */}
      <div className="message-row-select" role="gridcell">{check}</div>
      {/* The cell that replaces the old role=button row: it carries the composed name, the keys
          that open the message, and the thread toggle, which unfolds its own content. The click
          stays the row's, so the padding around this box opens the message as it always did. */}
      <div
        className="message-row-content"
        role="gridcell"
        tabIndex={-1}
        // Where the grid already is, so Tab into the list lands on a cell Enter means something
        // on rather than on the first row's checkbox.
        aria-current={open || undefined}
        aria-label={label}
        onKeyDown={onRowKey}
      >
        {wide ? (
          <>
            {unread && <span className="message-row-unread-dot" />}
            {drafts && <span className="message-row-draft">{t('list.draft')}</span>}
            {priorityMark}
            <span className="message-row-from">{from}</span>
            {attachments && <PaperclipIcon size={13} title={t('list.hasAttachments')} />}
            <span className="message-row-line">
              {subject}
              {showsPreview && message.preview && (
                <span className="message-row-line-preview"> — {message.preview}</span>
              )}
            </span>
            <span className="message-row-date">{when}</span>
            {threadBits}
          </>
        ) : (
          <>
            <div className="message-row-top">
              {unread && <span className="message-row-unread-dot" />}
              {drafts && <span className="message-row-draft">{t('list.draft')}</span>}
              {priorityMark}
              <span className="message-row-from">{from}</span>
              {attachments && <PaperclipIcon size={13} title={t('list.hasAttachments')} />}
              <span className="message-row-date">{when}</span>
              {threadBits}
            </div>
            <div className="message-row-subject">{subject}</div>
            {/* Always rendered when previews are on, even empty: a message with no body
                would otherwise make a shorter row than its neighbours and break the rhythm
                of the column. The reserved height lives in CSS. */}
            {showsPreview && <div className="message-row-preview">{message.preview}</div>}
          </>
        )}
      </div>
      {/* Each skin's own drawn order, because that is the order the arrows walk: the wide row
          ends on the star, the narrow one carries it top-right above the cluster. The other way
          round, revealing the actions moved the star out from under the pointer aimed at it. */}
      {wide ? (
        <>
          <div className="message-row-actions" role="gridcell">{cluster}</div>
          <div className="message-row-flag" role="gridcell">{star}</div>
        </>
      ) : (
        <>
          <div className="message-row-flag" role="gridcell">{star}</div>
          <div className="message-row-actions" role="gridcell">{cluster}</div>
        </>
      )}
    </Row>
    </div>
  )
}

// Every prop is a primitive, a reference the list memoises or this row's own message, so the shallow
// compare lets one ticked checkbox redraw one row instead of the whole folder.
export default memo(MessageRow)
