import { useState } from 'react'
import type { CSSProperties, DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { collator } from '../../../lib/intl'
import { roleLabel } from '../roleLabel'
import type { MailFolderNode } from '../api/mailTypes'
import { DRAG_MIME, canDropInto, parseDrag, type DragPayload } from '../list/dragMessages'
import { isSystemFolder } from './folderNodes'
import { folderIcon } from './folderIcon'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onSelect: (path: string) => void
  /** Drop dragged messages into a folder. Absent where the tree is not a drop target. */
  onDropMessages?: (targetPath: string, payload: DragPayload) => void
  /** `mail.showFolderIcons`, resolved by the caller — the tree holds no data of its own. */
  showIcons?: boolean
}

/** The well-known folders, in the order a reader reaches for them. */
const SPECIAL_ORDER = ['inbox', 'drafts', 'sent', 'archive', 'junk', 'trash']

// The same collator call as folderNodes.sortFolders, so the tree and the settings list never
// disagree: localeCompare(undefined, …) reads the browser's language, not the UI's.
function byName(a: MailFolderNode, b: MailFolderNode): number {
  return collator({ sensitivity: 'base', numeric: true }).compare(a.name, b.name)
}

// Role folders in reading order, then the rest by name. Not `sortFolders`: `folderNodes.sortFolders`
// answers the opposite question, and two orders must not share a name.
export function splitByRole(folders: MailFolderNode[]): {
  system: MailFolderNode[]
  others: MailFolderNode[]
} {
  const rank = (folder: MailFolderNode) => {
    const index = SPECIAL_ORDER.indexOf(folder.specialUse ?? '')
    return index === -1 ? SPECIAL_ORDER.length : index
  }

  return {
    system: folders.filter(f => f.specialUse).sort((a, b) => rank(a) - rank(b) || byName(a, b)),
    others: folders.filter(f => !f.specialUse).sort(byName),
  }
}

/** Children are ordinary folders; they sort by name like the block they hang under. */
export function sortChildren(folders: MailFolderNode[]): MailFolderNode[] {
  return [...folders].sort(byName)
}

// A role folder is shown subscribed or not: the user cannot hide it anyway. Dovecot leaves INBOX
// unsubscribed and Proximus subscribes nothing, so the flag says nothing about a role folder.
export function isVisible(folder: MailFolderNode): boolean {
  return folder.subscribed || isSystemFolder(folder)
}

// No unread prompt for trash or junk: nobody is behind on deleted mail, and a junk count advertises
// what the filter was meant to spare.
export function showsUnreadCount(folder: MailFolderNode): boolean {
  return folder.specialUse !== 'trash' && folder.specialUse !== 'junk'
}

function FolderRow({
  folder,
  selectedPath,
  onSelect,
  onDropMessages,
  showIcons,
}: {
  folder: MailFolderNode
  selectedPath: string | null
  onSelect: (path: string) => void
  onDropMessages?: (targetPath: string, payload: DragPayload) => void
  showIcons?: boolean
}) {
  const { t } = useTranslation('mail')
  const [open, setOpen] = useState(folder.specialUse === 'inbox')
  const [dropReady, setDropReady] = useState(false)
  const visibleChildren = sortChildren(folder.children.filter(isVisible))
  const isActive = folder.path === selectedPath
  const label = folder.specialUse ? roleLabel(folder.specialUse, t) : folder.name
  // Only when a badge is actually rendered does the accessible name grow a suffix — an unread
  // count on trash or junk never reaches the screen, so it must not reach assistive tech either.
  const showsBadge = Boolean(folder.unread) && showsUnreadCount(folder)

  // The source folder rides in the payload, but the browser withholds it until drop, so the
  // eligibility check during a hover reads the open folder instead — which is that same source.
  const droppable = Boolean(onDropMessages) && canDropInto(folder, selectedPath)

  function onDragOver(event: DragEvent<HTMLDivElement>) {
    if (!droppable || !event.dataTransfer.types.includes(DRAG_MIME)) return
    event.preventDefault()  // The default is "no drop"; preventing it opens the folder up.
    event.dataTransfer.dropEffect = 'move'
    setDropReady(true)
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    setDropReady(false)
    if (!droppable) return
    event.preventDefault()
    const payload = parseDrag(event.dataTransfer.getData(DRAG_MIME))
    if (payload) onDropMessages!(folder.path, payload)
  }

  return (
    <>
      <div
        className={dropReady ? 'folder-line drop-ready' : 'folder-line'}
        onDragOver={onDragOver}
        onDragLeave={() => setDropReady(false)}
        onDrop={onDrop}
      >
        {visibleChildren.length > 0 ? (
          <button
            type="button"
            className={open ? 'folder-toggle is-open' : 'folder-toggle'}
            aria-label={t(open ? 'folders.collapse' : 'folders.expand', { name: folder.name })}
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
          className={[
            'folder-row',
            folder.specialUse ? 'is-system' : '',
            isActive ? 'is-active' : '',
          ].filter(Boolean).join(' ')}
          aria-current={isActive ? 'true' : undefined}
          // The drop badge's text, which mail.css draws through `content`. It travels as a custom
          // property because a stylesheet literal cannot be translated — and was English here.
          style={{ '--drop-label': `"${t('folders.dropHere')}"` } as CSSProperties}
          // The label and badge spans concatenate into the accessible name with no separator
          // ("Inbox4"), which is a real number losing its meaning, not a decorative artifact —
          // so instead of hiding the count from assistive tech, spell it out as words.
          aria-label={showsBadge
            ? t('folders.unreadAria', { label, count: folder.unread })
            : undefined}
          // The role label replaces the name; the real mailbox name stays one hover away, so
          // the user never loses track of which physical folder they are looking at.
          title={folder.specialUse ? folder.name : undefined}
          // A container-only folder holds no messages, so selecting it would show nothing.
          disabled={!folder.selectable}
          onClick={() => folder.selectable && onSelect(folder.path)}
        >
          {/* Decorative: the row is already named by its label, so it adds nothing to say. */}
          {showIcons && folderIcon(folder.specialUse)}
          <span className="folder-row-name">{label}</span>
          {showsBadge ? <span className="folder-row-count">{folder.unread}</span> : null}
        </button>
      </div>

      {open && visibleChildren.length > 0 && (
        <div className="folder-children">
          {visibleChildren.map(child => (
            <FolderRow key={child.path} folder={child} selectedPath={selectedPath}
              onSelect={onSelect} onDropMessages={onDropMessages} showIcons={showIcons} />
          ))}
        </div>
      )}
    </>
  )
}

/** Unsubscribed folders are hidden — that is what the subscription state is for, except for
 *  the inbox, which is always shown (see isVisible). */
export default function FolderTree(
  { folders, selectedPath, onSelect, onDropMessages, showIcons }: Props,
) {
  const { t } = useTranslation('mail')
  const { system, others } = splitByRole(folders.filter(isVisible))

  return (
    <nav aria-label={t('folders.navLabel')}>
      {system.map(folder => (
        <FolderRow key={folder.path} folder={folder} selectedPath={selectedPath}
          onSelect={onSelect} onDropMessages={onDropMessages} showIcons={showIcons} />
      ))}

      {/* Only between two populated blocks: a rule under nothing reads as a fault. */}
      {system.length > 0 && others.length > 0 && <hr className="folder-separator" />}

      {others.map(folder => (
        <FolderRow key={folder.path} folder={folder} selectedPath={selectedPath}
          onSelect={onSelect} onDropMessages={onDropMessages} showIcons={showIcons} />
      ))}
    </nav>
  )
}
