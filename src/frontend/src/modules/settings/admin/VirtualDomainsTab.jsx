import { useState, useEffect, useRef } from 'react'
import { api } from '../../../api.js'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'

export function VirtualDomainsTab({ addToast }) {
  const [virtualDomains, setVirtualDomains] = useState([])
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [editingDomainId, setEditingDomainId] = useState(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [saving, setSaving] = useState(false)
  const editRef = useRef(null)

  async function load() {
    setLoading(true)
    try {
      const [o, u] = await Promise.all([api.adminGetVirtualDomains(), api.adminGetUsers()])
      setVirtualDomains(o ?? [])
      setUsers(u ?? [])
    } catch {
      addToast('Failed to load virtual domains', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  useEffect(() => {
    if (!editingDomainId) return
    function handleClick(e) {
      if (!editRef.current?.contains(e.target)) {
        setEditingDomainId(null)
        setSearchQuery('')
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [editingDomainId])

  async function handleSelect(domainId, userId) {
    setSaving(true)
    try {
      const updated = await api.adminAddVirtualDomainOwner(domainId, userId)
      setSearchQuery('')
      setVirtualDomains(prev => prev.map(o => o.domainId === domainId ? updated : o))
    } catch (err) {
      addToast(err.message || 'Failed to set owner', 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleUnlink(domainId, userId) {
    setSaving(true)
    try {
      await api.adminRemoveVirtualDomainOwner(domainId, userId)
      setVirtualDomains(prev => prev.map(o =>
        o.domainId === domainId
          ? { ...o, owners: o.owners.filter(own => own.ownerId !== userId) }
          : o
      ))
    } catch (err) {
      addToast(err.message || 'Failed to remove owner', 'error')
    } finally {
      setSaving(false)
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

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><span className="spinner" /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Virtual alias domains ({virtualDomains.length})</span>
      </div>
      <div className="admin-list">
        {virtualDomains.length === 0 && (
          <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '13px' }}>
            No virtual alias domains
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
                          title="Remove owner"
                          disabled={saving}
                          onMouseDown={e => { e.preventDefault(); handleUnlink(o.domainId, own.ownerId) }}
                        >
                          <TrashIcon />
                        </button>
                      </span>
                    ))}
                  </div>
                )}
                <div style={{ position: 'relative' }}>
                  <input
                    className="search-input"
                    type="text"
                    placeholder="Search user…"
                    value={searchQuery}
                    onChange={e => setSearchQuery(e.target.value)}
                    autoFocus
                    style={{ width: '100%', padding: '5px 8px', fontSize: '13px' }}
                    onKeyDown={e => {
                      if (e.key === 'Escape') { setEditingDomainId(null); setSearchQuery('') }
                    }}
                  />
                  {filteredUsers.length > 0 && (
                    <div className="ownership-dropdown">
                      {filteredUsers.slice(0, 10).map(u => (
                        <button
                          key={u.id}
                          className="ownership-dropdown-option"
                          disabled={saving}
                          onMouseDown={e => { e.preventDefault(); handleSelect(o.domainId, u.id) }}
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
                <button className="admin-icon-btn" title="Edit owner" onClick={() => {
                  setEditingDomainId(o.domainId)
                  setSearchQuery('')
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
