import { useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { flatten, indent, sortFolders } from './folders/folderNodes'
import { folderMatches } from './folders/folderFilter'
import { roleLabel } from './roleLabel'
import Modal from '../../components/Modal'
import FolderMoveIcon from '../../icons/FolderMoveIcon'
import CopyIcon from '../../icons/CopyIcon'
import type { MailFolderNode } from './api/mailTypes'

interface Props {
  mode: 'move' | 'copy'
  folders: MailFolderNode[]
  currentFolderPath: string
  onPick: (targetPath: string) => void
  onClose: () => void
}

// Unusable rows stay listed and disabled: a folder missing from the list reads as a bug, not a rule.
// Shaped as the site's list filter (search shape 1); the dialog root is the form, so Enter commits.
export default function MoveMessagesModal(
  { mode, folders, currentFolderPath, onPick, onClose }: Props) {
  const { t } = useTranslation('mail')
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const filterRef = useRef<HTMLInputElement>(null)

  // Depth comes from the whole tree, so a match keeps its true nesting even once its parent
  // has been filtered away — it reads as a child instead of pretending to be top-level.
  const all = useMemo(() => flatten(sortFolders(folders)).map(({ node, depth }) => ({
    node,
    depth,
    // Copy into the source folder is refused exactly as move is; the backend rejects both.
    disabledAs: node.path === currentFolderPath ? 'current'
      : !node.selectable ? 'container' : null,
  })), [folders, currentFolderPath])

  const rows = all.filter(row => folderMatches(row.node.name, query))
  const enabled = rows.filter(row => !row.disabledAs)
  // A selection filtered off-screen or since disabled (e.g. it became the current folder)
  // must not stay armed: acting on it would file the mail somewhere it can't be picked from.
  const target = enabled.find(row => row.node.path === selected)?.node ?? null

  // The picked folder, or else the single row the query has left standing.
  function commit() {
    const pick = target ?? (enabled.length === 1 ? enabled[0]!.node : null)
    if (pick) onPick(pick.path)
  }

  return (
    <Modal
      className="folder-pick-modal"
      icon={mode === 'move' ? <FolderMoveIcon size={17} /> : <CopyIcon size={17} />}
      title={t(mode === 'move' ? 'move.titleMove' : 'move.titleCopy')}
      onClose={onClose}
      initialFocusRef={filterRef}
      onSubmit={e => { e.preventDefault(); commit() }}
    >
      <div className="admin-list-header">
        <span className="admin-list-title">
          {t('move.destination')}
          <span className="folder-pick-count">
            {query
              ? t('move.countFiltered', { shown: rows.length, count: all.length })
              : t('move.count', { count: all.length })}
          </span>
        </span>
        <input
          className="search-input folder-pick-filter"
          type="search"
          aria-label={t('move.searchLabel')}
          placeholder={t('move.searchPlaceholder')}
          ref={filterRef}
          value={query}
          onChange={e => setQuery(e.target.value)}
          // preventDefault, so the implicit submission this keystroke would also trigger
          // cannot commit the same pick twice.
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); commit() } }}
        />
      </div>

      <div className="folder-pick-list">
        {rows.length === 0 && (
          <div className="folder-pick-empty">{t('move.noMatch', { query })}</div>
        )}
        {rows.map(({ node, depth, disabledAs }) => (
          <button
            key={node.path}
            type="button"
            className={`folder-pick-row${selected === node.path ? ' is-selected' : ''}`}
            disabled={Boolean(disabledAs)}
            onClick={() => setSelected(node.path)}
          >
            <span className="folder-pick-indent">{indent(depth)}</span>
            <span className="folder-pick-name">{node.name}</span>
            {(disabledAs ?? (node.specialUse && roleLabel(node.specialUse, t))) && (
              <span className="row-tag">
                {disabledAs
                  ? t(disabledAs === 'current' ? 'move.tagCurrent' : 'move.tagContainer')
                  : roleLabel(node.specialUse!, t)}
              </span>
            )}
          </button>
        ))}
      </div>

      <div className="folder-pick-submit">
        <button className="btn btn-primary" type="submit" disabled={!target}>
          {target
            ? t(mode === 'move' ? 'move.submitMove' : 'move.submitCopy', { name: target.name })
            : t(mode === 'move' ? 'move.actionMove' : 'move.actionCopy')}
        </button>
      </div>
    </Modal>
  )
}
