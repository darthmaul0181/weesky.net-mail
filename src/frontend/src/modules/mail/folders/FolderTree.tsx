import { useState } from 'react'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onSelect: (path: string) => void
}

/** Well-known folders first, in reading order, then everything else alphabetically. */
const SPECIAL_ORDER = ['inbox', 'drafts', 'sent', 'archive', 'junk', 'trash']

export function sortFolders(folders: MailFolderNode[]): MailFolderNode[] {
  return [...folders].sort((a, b) => {
    const rankA = a.specialUse ? SPECIAL_ORDER.indexOf(a.specialUse) : SPECIAL_ORDER.length
    const rankB = b.specialUse ? SPECIAL_ORDER.indexOf(b.specialUse) : SPECIAL_ORDER.length
    return rankA !== rankB ? rankA - rankB : a.name.localeCompare(b.name)
  })
}

/**
 * The inbox is always shown, subscribed or not. Dovecot does not mark INBOX as subscribed —
 * it is implicitly always available, so the subscription flag is meaningless for it — and
 * filtering on subscription alone would hide the one folder that can never be hidden.
 */
export function isVisible(folder: MailFolderNode): boolean {
  return folder.subscribed || folder.specialUse === 'inbox'
}

/**
 * An unread badge is a call to go and read something. That reading is worth prompting in a
 * folder you keep, and not in the two you do not: nobody is behind on their deleted mail, and
 * an unread count on junk advertises exactly what the filter was meant to spare you.
 */
export function showsUnreadCount(folder: MailFolderNode): boolean {
  return folder.specialUse !== 'trash' && folder.specialUse !== 'junk'
}

function FolderRow({
  folder,
  selectedPath,
  onSelect,
}: {
  folder: MailFolderNode
  selectedPath: string | null
  onSelect: (path: string) => void
}) {
  const [open, setOpen] = useState(folder.specialUse === 'inbox')
  const visibleChildren = sortFolders(folder.children.filter(isVisible))
  const isActive = folder.path === selectedPath

  return (
    <>
      <div className="folder-line">
        {visibleChildren.length > 0 ? (
          <button
            type="button"
            className={open ? 'folder-toggle is-open' : 'folder-toggle'}
            aria-label={`${open ? 'Collapse' : 'Expand'} ${folder.name}`}
            aria-expanded={open}
            onClick={() => setOpen(value => !value)}
          >
            <ChevronRightIcon />
          </button>
        ) : (
          <span className="folder-toggle-spacer" />
        )}

        <button
          type="button"
          className={isActive ? 'folder-row is-active' : 'folder-row'}
          aria-current={isActive ? 'true' : undefined}
          // A container-only folder holds no messages, so selecting it would show nothing.
          disabled={!folder.selectable}
          onClick={() => folder.selectable && onSelect(folder.path)}
        >
          <span className="folder-row-name">{folder.name}</span>
          {folder.unread && showsUnreadCount(folder)
            ? <span className="folder-row-count">{folder.unread}</span>
            : null}
        </button>
      </div>

      {open && visibleChildren.length > 0 && (
        <div className="folder-children">
          {visibleChildren.map(child => (
            <FolderRow key={child.path} folder={child} selectedPath={selectedPath} onSelect={onSelect} />
          ))}
        </div>
      )}
    </>
  )
}

/** Unsubscribed folders are hidden — that is what the subscription state is for, except for
 *  the inbox, which is always shown (see isVisible). */
export default function FolderTree({ folders, selectedPath, onSelect }: Props) {
  return (
    <nav aria-label="Folders">
      {sortFolders(folders.filter(isVisible)).map(folder => (
        <FolderRow key={folder.path} folder={folder} selectedPath={selectedPath} onSelect={onSelect} />
      ))}
    </nav>
  )
}
