import { type ReactNode, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu, { type MenuEntry } from '../../../components/DropdownMenu'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import TrashIcon from '../../../icons/TrashIcon'
import KebabIcon from '../../../icons/KebabIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import FolderMoveIcon from '../../../icons/FolderMoveIcon'
import CopyIcon from '../../../icons/CopyIcon'
import SearchIcon from '../../../icons/SearchIcon'
import StarIcon from '../../../icons/StarIcon'
import LoaderIcon from '../../../icons/LoaderIcon'

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
  searchOpen: boolean
  onToggleSearch: () => void
  /** The folder's starred filter, which is a search on this folder carrying `flagged`. */
  starred: boolean
  onToggleStarred: () => void
  /** Switching it on needs a folder to search; switching it off never does. */
  starredDisabled?: boolean
  /** All-folders results: rows carry no checkbox, so the master must not promise one. */
  selectionDisabled?: boolean
  /** The drawer's hamburger below 1024px, nothing on desktop. */
  leading?: ReactNode
  /** Refresh, which loses its home in the folder column once that column is a drawer. */
  refresh?: ToolbarAction
}

/** Dumb band: renders selection state and calls handlers. All role/enablement logic is
    computed by MessageList and passed in as props. */
export default function SelectionToolbar(props: SelectionToolbarProps) {
  const { t } = useTranslation('mail')
  const { title, count, allSelected, indeterminate, onToggleAll, overCap, deleteLabel } = props
  const master = useRef<HTMLInputElement>(null)
  useEffect(() => { if (master.current) master.current.indeterminate = indeterminate }, [indeterminate])

  // A selection action is off when nothing is selected, over the cap, or its role forbids it.
  // The cap message wins the tooltip, then the role reason. Shared by the direct buttons and
  // the kebab's selection-bound entries, so disabledReason binds identically in both places.
  function selectionState(action: ToolbarAction) {
    const disabled = count === 0 || overCap || !!action.disabledReason
    const tip = overCap ? t('toolbar.cap') : action.disabledReason
    return { disabled, tip }
  }

  // The tooltip names the action; a disabled reason (or the cap) takes over when it applies.
  function actionProps(action: ToolbarAction, label: string) {
    const { disabled, tip } = selectionState(action)
    return { disabled, title: tip ?? label, onClick: action.onRun }
  }

  // Icons before each label, matching the reader header's kebab so the two menus read as one system.
  function kebabItem(label: string, icon: ReactNode, action: ToolbarAction) {
    const { disabled, tip } = selectionState(action)
    return { label, icon, onSelect: action.onRun, disabled, title: tip }
  }

  const kebab: MenuEntry[] = [
    ...(props.refresh
      ? [{ label: t('folders.refresh'), icon: <LoaderIcon size={18} />, onSelect: props.refresh.onRun },
        'separator' as const]
      : []),
    kebabItem(t('toolbar.markRead'), <MailOpenIcon size={18} />, props.markRead),
    kebabItem(t('toolbar.markUnread'), <MailIcon size={18} />, props.markUnread),
    'separator',
    kebabItem(t('toolbar.moveTo'), <FolderMoveIcon size={18} />, props.move),
    kebabItem(t('toolbar.copyTo'), <CopyIcon size={18} />, props.copy),
    'separator',
    { label: t('toolbar.emptyFolder'), icon: <TrashIcon size={18} />, onSelect: props.emptyFolder.onRun,
      disabled: !!props.emptyFolder.disabledReason, title: props.emptyFolder.disabledReason },
  ]

  return (
    <div className={`selection-toolbar${count > 0 ? ' is-selecting' : ''}`}>
      {props.leading}
      <input
        ref={master}
        type="checkbox"
        className="selection-master"
        aria-label={t('toolbar.selectAll')}
        checked={allSelected}
        onChange={onToggleAll}
        disabled={props.selectionDisabled}
      />
      <span className="selection-heading">
        <span className="selection-title">
          {count > 0 ? t('toolbar.selected', { count }) : title}
        </span>
        {/* Beside the name rather than in the actions: it filters the view, it acts on nothing. */}
        <button
          type="button"
          className={`selection-btn selection-star${props.starred ? ' is-on' : ''}`}
          aria-label={t(props.starred ? 'toolbar.showAll' : 'toolbar.showStarred')}
          title={t(props.starred ? 'toolbar.showAll' : 'toolbar.showStarred')}
          aria-pressed={props.starred}
          disabled={props.starredDisabled}
          onClick={props.onToggleStarred}
        >
          <StarIcon size={18} filled={props.starred} />
        </button>
      </span>
      <div className="selection-actions">
        <button type="button" className="selection-btn selection-archive" aria-label={t('toolbar.archive')} {...actionProps(props.archive, t('toolbar.archive'))}>
          <ArchiveIcon size={20} />
        </button>
        <button type="button" className="selection-btn selection-junk" aria-label={t('toolbar.junk')} {...actionProps(props.junk, t('toolbar.junk'))}>
          <JunkIcon size={20} />
        </button>
        <button type="button" className="selection-btn is-danger selection-delete" aria-label={deleteLabel} {...actionProps(props.del, deleteLabel)}>
          <TrashIcon size={20} />
        </button>
        <button
          type="button"
          className={`selection-btn selection-search${props.searchOpen ? ' is-active' : ''}`}
          aria-label={t('toolbar.search')}
          title={t('toolbar.search')}
          onClick={props.onToggleSearch}
        >
          <SearchIcon size={20} />
        </button>
        <DropdownMenu ariaLabel={t('toolbar.more')} className="selection-btn" trigger={<KebabIcon />} items={kebab} />
      </div>
    </div>
  )
}
