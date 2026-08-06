import { useEffect, useState } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import GlobeIcon from '../../../icons/GlobeIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import ExternalDomainDialog from './ExternalDomainDialog'
import { useDeleteExternalDomain, useExternalDomains, type ExternalDomain } from './useExternalDomains'

interface Props {
  addToast: (message: string, kind?: string) => void
}

/**
 * The admin-curated external mail providers users may attach a connected account from — the
 * only source of external endpoints in the product. The mockups show name-only tiles: every
 * configuration detail lives in the dialog, not on the tile.
 */
export default function ExternalDomainsTab({ addToast }: Props) {
  const { data: domains, isLoading, isError } = useExternalDomains()
  const deleteDomain = useDeleteExternalDomain()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<ExternalDomain | null>(null)
  const [deleting, setDeleting] = useState<ExternalDomain | null>(null)

  // An effect, not a call during render: isError stays true across every re-render the failed
  // query causes, and a render-time call would toast again on each of them.
  useEffect(() => {
    if (isError) addToast('Failed to load external domains', 'error')
  }, [isError, addToast])

  if (isLoading) return <LoadingBlock />
  if (isError || !domains) return <p>Could not load the external domains.</p>

  async function confirmDelete() {
    if (!deleting) return
    try {
      await deleteDomain.mutateAsync(deleting.id)
      addToast(`${deleting.name} deleted`)
      setDeleting(null)
    } catch (err) {
      addToast(err instanceof Error ? err.message : 'Could not delete this domain', 'error')
    }
  }

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">External domains ({domains.length})</span>
        <button type="button" className="btn btn-primary"
          style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setAdding(true)}>
          <GlobeIcon /> Add
        </button>
      </div>
      {domains.length === 0
        ? <p className="settings-note">No external domains</p>
        : (
          <div className="admin-list">
            {domains.map(domain => (
              <div key={domain.id} className="admin-list-item">
                <span className="admin-list-item-name">{domain.name}</span>
                {domain.authMode === 'OAuth2' && <span className="row-tag">OAuth</span>}
                <div className="admin-list-item-actions">
                  <button type="button" className="admin-icon-btn" title="Edit"
                    onClick={() => setEditing(domain)}>
                    <PencilIcon />
                  </button>
                  <button type="button" className="admin-icon-btn is-danger" title="Delete"
                    onClick={() => setDeleting(domain)}>
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

      {adding && (
        <ExternalDomainDialog
          onSave={() => { setAdding(false); addToast('External domain created') }}
          onClose={() => setAdding(false)}
        />
      )}
      {editing && (
        <ExternalDomainDialog
          domain={editing}
          onSave={() => { setEditing(null); addToast('External domain updated') }}
          onClose={() => setEditing(null)}
        />
      )}
      {deleting && (
        <DeleteConfirmModal entityLabel={deleting.name} loading={deleteDomain.isPending}
          onConfirm={confirmDelete} onClose={() => setDeleting(null)} />
      )}
    </div>
  )
}
