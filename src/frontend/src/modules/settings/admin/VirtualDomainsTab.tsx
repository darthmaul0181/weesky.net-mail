import { useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from 'i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import type { AddToast } from '../../../hooks/useToasts'
import TrashIcon from '../../../icons/TrashIcon'
import PencilIcon from '../../../icons/PencilIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useDismiss } from '../../../hooks/useDismiss'
import ListLoadFailed from '../../../components/ListLoadFailed'
import { useRovingFocus } from '../../../hooks/useRovingFocus'
import {
  useAddVirtualDomainOwner, useAdminUsers, useAdminVirtualDomains, useListLoad, useRemoveVirtualDomainOwner,
} from './useAdminLists'

interface Props {
  addToast: AddToast
}

export function VirtualDomainsTab({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const virtualDomainsQuery = useAdminVirtualDomains()
  const usersQuery = useAdminUsers()
  const virtualDomains = virtualDomainsQuery.data ?? []
  const users = usersQuery.data ?? []
  const addOwner = useAddVirtualDomainOwner()
  const removeOwner = useRemoveVirtualDomainOwner()
  const saving = addOwner.isPending || removeOwner.isPending
  const [editingDomainId, setEditingDomainId] = useState<string | null>(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [listDismissed, setListDismissed] = useState(false)
  const editRef = useRef<HTMLDivElement>(null)
  const comboRef = useRef<HTMLDivElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)
  // Escape and an outside press unmount the editor holding the focus, and the pencil that opened it
  // is not drawn while that row is being edited — a `refocusRef` would be null. The id is
  // remembered instead, and the pencil claims the focus as it mounts again.
  const returnTo = useRef<string | null>(null)
  const domainsFirstLoad = useListLoad(virtualDomainsQuery, addToast, i18next.t('admin:virtual.loadFailed'))
  const usersFirstLoad = useListLoad(
    usersQuery, addToast, i18next.t('admin:virtual.usersFailed'), virtualDomainsQuery)
  const firstLoad = domainsFirstLoad || usersFirstLoad

  const cancelEdit = useCallback(() => {
    returnTo.current = editingDomainId
    setEditingDomainId(null)
    setSearchQuery('')
    setListDismissed(false)
  }, [editingDomainId])

  useDismiss({ open: editingDomainId !== null, rootRef: editRef, onDismiss: cancelEdit })
  // The box and its matches are one surface for the arrows: ↓ walks out of the field into the list.
  // The field autofocuses itself, so the walk only ever moves focus that is already inside.
  const comboKeys = useRovingFocus({ active: editingDomainId !== null, containerRef: comboRef })

  async function handleSelect(domainId: string, userId: number) {
    // Read before the write: `saving` disables the rows and the picked one leaves with the list,
    // either of which drops the focus it holds — the box is where it belongs once it is gone.
    const fromList = comboRef.current?.contains(document.activeElement)
    try {
      await addOwner.mutateAsync({ domainId, userId })
      setSearchQuery('')
    } catch (err) {
      addToast(apiErrorMessage(err, t('virtual.setOwnerFailed')), 'error')
    } finally {
      if (fromList) searchRef.current?.focus()
    }
  }

  async function handleUnlink(domainId: string, userId: number) {
    // Read before the write, as handleSelect does: the chip leaves with its own ✕.
    const fromEditor = editRef.current?.contains(document.activeElement)
    try {
      await removeOwner.mutateAsync({ domainId, userId })
    } catch (err) {
      addToast(apiErrorMessage(err, t('virtual.removeOwnerFailed')), 'error')
    } finally {
      if (fromEditor) searchRef.current?.focus()
    }
  }

  const editingVirtualDomain = virtualDomains.find(o => o.domainId === editingDomainId)
  const editingOwnerIds = new Set((editingVirtualDomain?.owners ?? []).map(own => own.ownerId))

  const term = searchQuery.trim().toLowerCase()
  const filteredUsers = term
    ? users.filter(u => {
        if (editingOwnerIds.has(u.id)) return false
        const email = `${u.userName}@${u.domainName}`.toLowerCase()
        const name = (u.fullName ?? '').toLowerCase()
        return email.includes(term) || name.includes(term)
      })
    : []

  const listOpen = filteredUsers.length > 0 && !listDismissed

  if (firstLoad) return <LoadingBlock />
  if (!virtualDomainsQuery.data) return <ListLoadFailed>{t('virtual.loadFailedBody')}</ListLoadFailed>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">{t('virtual.title', { total: virtualDomains.length })}</span>
      </div>
      <div className="admin-list">
        {virtualDomains.length === 0 && (
          <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '13px' }}>
            {t('virtual.empty')}
          </div>
        )}
        {virtualDomains.map(o => (
          <div key={o.domainId} className="admin-list-item" style={{ alignItems: 'flex-start', paddingTop: '10px', paddingBottom: '10px' }}>
            <span className="admin-list-item-email" style={{ paddingTop: '4px' }}>{o.domainName} <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>({o.domainId})</span></span>
            {editingDomainId === o.domainId ? (
              <div ref={editRef} style={{ flex: 1, paddingLeft: '30px' }}>
                {o.owners.length > 0 && (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginBottom: '6px' }}>
                    {o.owners.map(own => (
                      <span key={own.ownerId} className="ownership-tile">
                        {own.ownerEmail}
                        <button
                          className="ownership-tile-remove"
                          title={t('virtual.removeOwner')}
                          disabled={saving}
                          onMouseDown={e => e.preventDefault()}
                          onClick={() => void handleUnlink(o.domainId, own.ownerId)}
                        >
                          <TrashIcon />
                        </button>
                      </span>
                    ))}
                  </div>
                )}
                <div style={{ position: 'relative' }} ref={comboRef}>
                  <input
                    className="search-input"
                    ref={searchRef}
                    type="text"
                    placeholder={t('virtual.searchUser')}
                    value={searchQuery}
                    onChange={e => { setSearchQuery(e.target.value); setListDismissed(false) }}
                    // eslint-disable-next-line jsx-a11y/no-autofocus -- an inline owner-search field, not a dialog Modal: opening it autofocuses like SearchBar's does
                    autoFocus
                    style={{ width: '100%', padding: '5px 8px', fontSize: '13px' }}
                    onKeyDown={e => {
                      if (e.key !== 'Escape') { comboKeys(e); return }
                      // Marked as spent so the layer below leaves it alone: one Escape answers
                      // the list when it is showing, and the edit itself once it is not.
                      e.preventDefault()
                      if (listOpen) setListDismissed(true)
                      else cancelEdit()
                    }}
                  />
                  {listOpen && (
                    <div className="ownership-dropdown">
                      {filteredUsers.slice(0, 10).map(u => (
                        <button
                          key={u.id}
                          className="ownership-dropdown-option"
                          disabled={saving}
                          onKeyDown={comboKeys}
                          // The press only keeps the caret in the box; picking is the click, so
                          // Enter and the space bar on a walked-to match do it too.
                          onMouseDown={e => e.preventDefault()}
                          onClick={() => void handleSelect(o.domainId, u.id)}
                        >
                          <span style={{ fontWeight: 600 }}>{u.userName}@{u.domainName}</span>
                          {u.fullName && <span style={{ color: 'var(--text-muted)', fontSize: '12px', marginLeft: '8px' }}>{u.fullName}</span>}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ) : (
              <div style={{ flex: 1, paddingLeft: '30px', display: 'flex', flexWrap: 'wrap', gap: '4px', paddingTop: '2px' }}>
                {o.owners.length === 0
                  ? <span style={{ color: 'var(--text-muted)', fontSize: '13px' }}>—</span>
                  : o.owners.map(own => (
                      <span key={own.ownerId} className="ownership-tile">{own.ownerEmail}</span>
                    ))
                }
              </div>
            )}
            <div className="admin-list-item-actions">
              {editingDomainId !== o.domainId && (
                <button className="admin-icon-btn" title={t('virtual.editOwner')}
                  ref={el => {
                    if (el && returnTo.current === o.domainId) { returnTo.current = null; el.focus() }
                  }}
                  onClick={() => {
                  setEditingDomainId(o.domainId)
                  setSearchQuery('')
                  // Reachable from the keyboard: Tab out of one row's box, Enter here, and a flag
                  // left standing would open this row with its list already suppressed.
                  setListDismissed(false)
                }}>
                  <PencilIcon />
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

export default VirtualDomainsTab
