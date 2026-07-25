import { useEffect, useMemo, useState } from 'react'
import { flatten, indent, sortFolders } from './folders/folderNodes'
import { folderMatches } from './folders/folderFilter'
import { roleLabel } from './roleLabel'
import type { MailFolderNode } from './api/mailTypes'

interface Props {
  mode: 'move' | 'copy'
  folders: MailFolderNode[]
  currentFolderPath: string
  onPick: (targetPath: string) => void
  onClose: () => void
}

/**
 * The picker behind "Move to…" and "Copy to…". Unusable rows stay listed and disabled: a list
 * where an expected folder is simply missing reads as a bug rather than as a rule.
 */
export default function MoveMessagesModal(
  { mode, folders, currentFolderPath, onPick, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

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
  const verb = mode === 'move' ? 'Move' : 'Copy'
  // A selection filtered off-screen or since disabled (e.g. it became the current folder)
  // must not stay armed: acting on it would file the mail somewhere it can't be picked from.
  const target = enabled.some(row => row.node.path === selected) ? selected : null

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '460px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{verb} to folder</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        <label className="folder-pick-label" htmlFor="move-folder-search">Search folders</label>
        <input
          id="move-folder-search"
          className="folder-pick-search"
          type="text"
          value={query}
          placeholder="Search folders…"
          autoFocus
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => {
            if (e.key === 'Enter' && enabled.length === 1) onPick(enabled[0].node.path)
          }}
        />

        <div className="folder-pick-count">
          {query ? `${rows.length} of ${all.length} folders` : `${all.length} folders`}
        </div>

        <div className="folder-pick-list">
          {rows.length === 0 && (
            <div className="folder-pick-empty">No folder matches “{query}”.</div>
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
              {(disabledAs ?? (node.specialUse && roleLabel(node.specialUse))) && (
                <span className="folder-pick-badge">
                  {disabledAs ?? roleLabel(node.specialUse!)}
                </span>
              )}
            </button>
          ))}
        </div>

        <div className="folder-pick-actions">
          <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button
            className="btn btn-primary"
            style={{ width: 'auto' }}
            disabled={!target}
            onClick={() => target && onPick(target)}
          >
            {verb}
          </button>
        </div>
      </div>
    </div>
  )
}
