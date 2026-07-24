import { useState } from 'react'
import { useAliases } from '../../mail/queries'
import { MAX_DISPLAY_NAME_LENGTH } from './identityRows'

interface Props {
  taken: string[]
  defaultName: string
  onAdd: (address: string, displayName: string) => void
  onClose: () => void
}

/** Where the hundred aliases live — a filterable picker, never the From menu. */
export default function AddIdentityDialog({ taken, defaultName, onAdd, onClose }: Props) {
  const { data: aliases, isLoading, isError } = useAliases()
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [name, setName] = useState(defaultName)

  // A set, lowercased like the addresses it is matched against: the alias list runs to 100+ rows.
  const takenSet = new Set(taken.map(address => address.toLowerCase()))
  const available = (aliases ?? [])
    .map(a => `${a.name}@${a.domain}`.toLowerCase())
    .filter(address => !takenSet.has(address))
  const needle = query.trim().toLowerCase()
  const matches = available.filter(address => address.includes(needle))

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal identity-add-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Add identity</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <label className="identity-add-label" htmlFor="identity-search">Search your aliases</label>
        <input
          id="identity-search" type="text" autoFocus value={query}
          onChange={e => setQuery(e.target.value)}
        />
        {/* A failed fetch must not read as "you have no aliases": on a mailbox carrying a
            hundred of them that looks like data loss rather than a network blip. */}
        <div className="identity-add-count">
          {isLoading ? 'Loading…'
            : isError ? 'Could not load your aliases.'
              : `${matches.length} of ${available.length} aliases`}
        </div>
        <ul className="identity-add-list">
          {matches.map(address => (
            <li key={address}>
              <button
                type="button"
                className={`identity-add-option${selected === address ? ' is-selected' : ''}`}
                aria-pressed={selected === address}
                onClick={() => setSelected(address)}
              >
                {address}
              </button>
            </li>
          ))}
        </ul>
        <label className="identity-add-label" htmlFor="identity-name">Display name</label>
        <input
          id="identity-name" type="text" value={name} maxLength={MAX_DISPLAY_NAME_LENGTH}
          onChange={e => setName(e.target.value)}
        />
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button
            type="button" className="btn btn-primary"
            disabled={!selected || name.trim() === ''}
            onClick={() => onAdd(selected!, name.trim())}
          >
            Add
          </button>
        </div>
      </div>
    </div>
  )
}
