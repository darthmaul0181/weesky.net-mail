import { useState } from 'react'
import { api } from '../../../api.js'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import GlobeIcon from '../../../icons/GlobeIcon.jsx'

export const DOMAIN_RE = /^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$/

export function AddEditDomainModal({ domain, onSave, onClose }) {
  const [id, setId] = useState(domain?.id ?? '')
  const [name, setName] = useState(domain?.name ?? '')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)
  const isEdit = !!domain
  const nameValid = DOMAIN_RE.test(name)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      if (isEdit) {
        await api.adminUpdateDomain(domain.id, { id, name })
      } else {
        await api.adminCreateDomain({ id, name })
      }
      onSave()
    } catch (err) {
      setError(err.message || 'An error occurred')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '380px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isEdit ? <><PencilIcon /> Edit domain</> : <><GlobeIcon /> Add domain</>}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error">{error}</div>}
          <div className="field">
            <label>ID (3 chars max)</label>
            <input type="text" value={id} onChange={e => setId(e.target.value.toUpperCase())}
              maxLength={3} disabled={isEdit} required />
          </div>
          <div className="field">
            <label>Domain name</label>
            <input type="text" value={name} onChange={e => setName(e.target.value)} required
              className={name && !nameValid ? 'is-error' : undefined} />
          </div>
          <button className="btn btn-primary" type="submit"
            disabled={loading || !id.trim() || !nameValid}>
            {loading ? <span className="spinner" /> : (isEdit ? 'Save changes' : 'Create domain')}
          </button>
        </form>
      </div>
    </div>
  )
}

export default AddEditDomainModal
