import { useEffect, useRef } from 'react'
import DropdownMenu, { type MenuEntry } from '../../../components/DropdownMenu'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import TrashIcon from '../../../icons/TrashIcon'
import FolderMoveIcon from '../../../icons/FolderMoveIcon'
import KebabIcon from '../../../icons/KebabIcon'

export interface ToolbarAction {
  onRun: () => void
  disabledReason?: string
}
export interface SelectionToolbarProps {
  title: string
  count: number
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  overCap: boolean
  deleteLabel: string
  archive: ToolbarAction
  junk: ToolbarAction
  del: ToolbarAction
  move: ToolbarAction
  copy: ToolbarAction
  markRead: ToolbarAction
  markUnread: ToolbarAction
  emptyFolder: ToolbarAction
}

const CAP = 'Select 200 or fewer'

/** Dumb band: renders selection state and calls handlers. All role/enablement logic is
    computed by MessageList and passed in as props. */
export default function SelectionToolbar(props: SelectionToolbarProps) {
  const { title, count, allSelected, indeterminate, onToggleAll, overCap, deleteLabel } = props
  const master = useRef<HTMLInputElement>(null)
  useEffect(() => { if (master.current) master.current.indeterminate = indeterminate }, [indeterminate])

  // A selection action is off when nothing is selected, over the cap, or its role forbids it.
  // The cap message wins the tooltip, then the role reason. Shared by the direct buttons and
  // the kebab's selection-bound entries, so disabledReason binds identically in both places.
  function selectionState(action: ToolbarAction) {
    const disabled = count === 0 || overCap || !!action.disabledReason
    const tip = overCap ? CAP : action.disabledReason
    return { disabled, tip }
  }

  function actionProps(action: ToolbarAction) {
    const { disabled, tip } = selectionState(action)
    return { disabled, title: tip, onClick: action.onRun }
  }

  function kebabItem(label: string, action: ToolbarAction) {
    const { disabled, tip } = selectionState(action)
    return { label, onSelect: action.onRun, disabled, title: tip }
  }

  const kebab: MenuEntry[] = [
    kebabItem('Mark as read', props.markRead),
    kebabItem('Mark as unread', props.markUnread),
    kebabItem('Copy to…', props.copy),
    'separator',
    { label: 'Empty folder', onSelect: props.emptyFolder.onRun,
      disabled: !!props.emptyFolder.disabledReason, title: props.emptyFolder.disabledReason },
  ]

  return (
    <div className="selection-toolbar">
      <input
        ref={master}
        type="checkbox"
        className="selection-master"
        aria-label="Select all"
        checked={allSelected}
        onChange={onToggleAll}
      />
      <span className="selection-title">{count > 0 ? `${count} selected` : title}</span>
      <div className="selection-actions">
        <button type="button" className="row-btn" aria-label="Archive" {...actionProps(props.archive)}>
          <ArchiveIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label="Report as junk" {...actionProps(props.junk)}>
          <JunkIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label={deleteLabel} {...actionProps(props.del)}>
          <TrashIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label="Move to…" {...actionProps(props.move)}>
          <FolderMoveIcon size={16} />
        </button>
        <DropdownMenu ariaLabel="More actions" className="row-btn" trigger={<KebabIcon />} items={kebab} />
      </div>
    </div>
  )
}
