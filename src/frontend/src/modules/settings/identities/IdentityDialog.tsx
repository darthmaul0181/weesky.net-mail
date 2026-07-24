import { useState } from 'react'
import { useAliases } from '../../mail/queries'
import { MAX_DISPLAY_NAME_LENGTH } from './identityRows'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'

interface Props {
  mode: 'add' | 'edit'
  taken: string[]
  editAddress?: string
  initialName?: string
  onSubmit: (address: string, displayName: string) => void
  onClose: () => void
}

/** Add or rename a sending identity in the site's admin-modal shape: two rows, one action, the ✕
    as the only way out. The alias is a type-to-filter combobox (like the virtual-domain owner
    picker), fixed once an existing identity is being edited. */
export default function IdentityDialog({
  mode, taken, editAddress, initialName = '', onSubmit, onClose,
}: Props) {
  const isEdit = mode === 'edit'
  const { data: aliases, isLoading, isError } = useAliases()
  const [query, setQuery] = useState(isEdit ? editAddress ?? '' : '')
  const [selected, setSelected] = useState<string | null>(isEdit ? editAddress ?? null : null)
  const [name, setName] = useState(initialName)
  const [open, setOpen] = useState(false)

  const takenSet = new Set(taken.map(a => a.toLowerCase()))
  const available = (aliases ?? [])
    .map(a => `${a.name}@${a.domain}`.toLowerCase())
    .filter(a => !takenSet.has(a))
  const needle = query.trim().toLowerCase()
  const matches = available.filter(a => a.includes(needle)).slice(0, 10)

  const canSubmit = selected !== null && name.trim() !== ''
  function submit() { if (canSubmit) onSubmit(selected!, name.trim()) }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal identity-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">
            {isEdit ? <PencilIcon /> : <PersonPlusIcon />}{isEdit ? 'Edit identity' : 'Add identity'}
          </span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        <div className="field-h">
          <label htmlFor="identity-alias">Alias</label>
          <div className="identity-combo">
            <input
              id="identity-alias" type="text" autoComplete="off"
              placeholder="Search your aliases…" autoFocus={!isEdit} disabled={isEdit}
              value={query}
              onChange={e => { setQuery(e.target.value); setSelected(null); setOpen(true) }}
              onFocus={() => setOpen(true)}
              onBlur={() => setOpen(false)}
              onKeyDown={e => { if (e.key === 'Escape') onClose() }}
            />
            {open && matches.length > 0 && (
              <div className="ownership-dropdown">
                {matches.map(address => (
                  <button
                    key={address} type="button" className="ownership-dropdown-option"
                    onMouseDown={e => {
                      e.preventDefault(); setQuery(address); setSelected(address); setOpen(false)
                    }}
                  >
                    {address}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
        {/* A failed alias fetch must read as a network blip, not "you have no aliases". */}
        {!isEdit && (isLoading || isError) && (
          <p className="identity-combo-hint">
            {isLoading ? 'Loading your aliases…' : 'Could not load your aliases.'}
          </p>
        )}

        <div className="field-h">
          <label htmlFor="identity-name">Display name</label>
          <input
            id="identity-name" type="text" value={name} maxLength={MAX_DISPLAY_NAME_LENGTH}
            autoFocus={isEdit}
            onChange={e => setName(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') submit() }}
          />
        </div>

        <div className="identity-modal-actions">
          <button type="button" className="btn btn-primary" style={{ width: 'auto' }}
            disabled={!canSubmit} onClick={submit}>
            {isEdit ? 'Save' : 'Add'}
          </button>
        </div>
      </div>
    </div>
  )
}
