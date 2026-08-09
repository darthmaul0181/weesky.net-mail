import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAliases } from '../../mail/queries'
import { MAX_DISPLAY_NAME_LENGTH } from './identityRows'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'

const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface Props {
  mode: 'add' | 'edit'
  taken: string[]
  editAddress?: string
  initialName?: string
  /** A connected account: no alias list exists for a server we do not administer, so the address
      is typed freely and the remote server is the only authority on it. */
  freeAddress?: boolean
  onSubmit: (address: string, displayName: string) => void
  onClose: () => void
}

/** Add or rename a sending identity in the site's admin-modal shape: two rows, one action, the ✕
    as the only way out. The alias is a type-to-filter combobox (like the virtual-domain owner
    picker), fixed once an existing identity is being edited. */
export default function IdentityDialog({
  mode, taken, editAddress, initialName = '', freeAddress = false, onSubmit, onClose,
}: Props) {
  const { t } = useTranslation('settings')
  const isEdit = mode === 'edit'
  const { data: aliases, isLoading, isError } = useAliases(!freeAddress)
  const [query, setQuery] = useState(isEdit ? editAddress ?? '' : '')
  const [selected, setSelected] = useState<string | null>(isEdit ? editAddress ?? null : null)
  const [name, setName] = useState(initialName)
  const [open, setOpen] = useState(false)

  const takenSet = new Set(taken.map(a => a.toLowerCase()))
  // The row being renamed is its own address' only holder — it must not read as a duplicate.
  if (isEdit && editAddress) takenSet.delete(editAddress.toLowerCase())
  const available = (aliases ?? [])
    .map(a => `${a.name}@${a.domain}`.toLowerCase())
    .filter(a => !takenSet.has(a))
  const needle = query.trim().toLowerCase()
  const matches = available.filter(a => a.includes(needle)).slice(0, 10)

  // A typed address validates itself; a picked alias is only ever set by the dropdown.
  const address = freeAddress
    ? (EMAIL_SHAPE.test(needle) && !takenSet.has(needle) ? needle : null)
    : selected
  const canSubmit = address !== null && name.trim() !== ''
  function submit() { if (canSubmit) onSubmit(address!, name.trim()) }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal identity-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">
            {isEdit ? <PencilIcon /> : <PersonPlusIcon />}
            {t(isEdit ? 'identities.editIdentity' : 'identities.addIdentity')}
          </span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>

        {freeAddress ? (
          <>
            <div className="field-h">
              <label htmlFor="identity-address">{t('identities.address')}</label>
              <input
                id="identity-address" type="email" autoComplete="off"
                autoFocus={!isEdit} disabled={isEdit} value={query}
                onChange={e => setQuery(e.target.value)}
                onKeyDown={e => { if (e.key === 'Escape') onClose() }}
              />
            </div>
            {!isEdit && (
              <p className="identity-combo-hint">{t('identities.freeAddressHint')}</p>
            )}
          </>
        ) : (
          <>
            <div className="field-h">
              <label htmlFor="identity-alias">{t('identities.alias')}</label>
              <div className="identity-combo">
                <input
                  id="identity-alias" type="text" autoComplete="off"
                  placeholder={t('identities.searchAliases')} autoFocus={!isEdit} disabled={isEdit}
                  value={query}
                  onChange={e => { setQuery(e.target.value); setSelected(null); setOpen(true) }}
                  onFocus={() => setOpen(true)}
                  onBlur={() => setOpen(false)}
                  onKeyDown={e => { if (e.key === 'Escape') onClose() }}
                />
                {open && matches.length > 0 && (
                  <div className="ownership-dropdown">
                    {matches.map(match => (
                      <button
                        key={match} type="button" className="ownership-dropdown-option"
                        onMouseDown={e => {
                          e.preventDefault(); setQuery(match); setSelected(match); setOpen(false)
                        }}
                      >
                        {match}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>
            {/* A failed alias fetch must read as a network blip, not "you have no aliases". */}
            {!isEdit && (isLoading || isError) && (
              <p className="identity-combo-hint">
                {t(isLoading ? 'identities.aliasesLoading' : 'identities.aliasesLoadFailed')}
              </p>
            )}
          </>
        )}

        <div className="field-h">
          <label htmlFor="identity-name">{t('identities.displayName')}</label>
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
            {t(isEdit ? 'actions.save' : 'actions.add', { ns: 'common' })}
          </button>
        </div>
      </div>
    </div>
  )
}
